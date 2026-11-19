using EduFlow.Application.Domain;

namespace EduFlow.Application.Services;

public sealed record PublicApplicationHistoryDto(
    AdmissionStatus? FromStatus,
    AdmissionStatus ToStatus,
    DateTimeOffset ChangedAtUtc,
    string? PublicMessage);

public sealed record ApplicationHistoryDto(
    AdmissionStatus? FromStatus,
    AdmissionStatus ToStatus,
    DateTimeOffset ChangedAtUtc,
    string? PublicMessage,
    string? InternalNote,
    string ChangedByUserId);

public sealed record StudentApplicationDto(
    long ApplicationId,
    string Reference,
    int CourseId,
    string CourseCode,
    string CourseName,
    string? PersonalStatement,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    AdmissionStatus Status,
    byte[] RowVersion,
    IReadOnlyList<PublicApplicationHistoryDto> StatusHistory);

public sealed record ReviewApplicationDto(
    long ApplicationId,
    string Reference,
    string StudentUserId,
    int CourseId,
    string CourseCode,
    string CourseName,
    string? PersonalStatement,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    AdmissionStatus Status,
    byte[] RowVersion,
    IReadOnlyList<ApplicationHistoryDto> StatusHistory);

public sealed record CreateApplicationInput(int CourseId, string? PersonalStatement, bool Submit);

public sealed record ReviewApplicationInput(
    AdmissionStatus ToStatus,
    string? PublicMessage,
    string? InternalNote,
    byte[] RowVersion);

public sealed record ApplicationOperationResult(
    StudentApplicationDto? StudentApplication,
    ReviewApplicationDto? ReviewApplication,
    string? Error,
    string? ErrorField,
    bool Conflict = false)
{
    public bool Succeeded => Error is null && (StudentApplication is not null || ReviewApplication is not null);
}

public sealed record AdministratorRecentApplicationData(
    long ApplicationId,
    string StudentName,
    string CourseName,
    AdmissionStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc);

public sealed record AdministratorDashboardData(
    int TotalApplications,
    int SubmittedApplications,
    int UnderReviewApplications,
    int ApprovedApplications,
    int RejectedApplications,
    int ActiveCourses,
    IReadOnlyList<AdministratorRecentApplicationData> RecentApplications);

public interface IAdmissionApplicationRepository
{
    Task<IReadOnlyList<AdmissionApplication>> GetForStudentAsync(string studentUserId, CancellationToken cancellationToken);
    Task<AdmissionApplication?> GetForStudentAsync(long applicationId, string studentUserId, CancellationToken cancellationToken);
    Task<AdmissionApplication?> GetDraftForStudentAsync(long applicationId, string studentUserId, CancellationToken cancellationToken);
    Task<AdmissionApplication?> GetByIdAsync(long applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionApplication>> GetReviewQueueAsync(bool allStatuses, CancellationToken cancellationToken);
    Task<bool> ExistsForCourseAsync(string studentUserId, int courseId, CancellationToken cancellationToken);
    Task<AdmissionApplication?> GetDraftForCourseAsync(string studentUserId, int courseId, CancellationToken cancellationToken);
    Task<Course?> GetActiveCourseAsync(int courseId, CancellationToken cancellationToken);
    Task<bool> AddAsync(AdmissionApplication application, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(AdmissionApplication application, byte[] originalRowVersion, CancellationToken cancellationToken);
    Task<bool> SaveReviewAsync(AdmissionApplication application, ApplicationStatusHistory history, byte[] originalRowVersion, CancellationToken cancellationToken);
    Task<AdministratorDashboardData> GetAdministratorDashboardAsync(int recentLimit, CancellationToken cancellationToken);
}

public interface IAdmissionApplicationService
{
    Task<IReadOnlyList<StudentApplicationDto>> GetStudentApplicationsAsync(string studentUserId, CancellationToken cancellationToken);
    Task<StudentApplicationDto?> GetStudentApplicationAsync(long applicationId, string studentUserId, CancellationToken cancellationToken);
    Task<ApplicationOperationResult> UpdateDraftAsync(string studentUserId, long applicationId, string? personalStatement, bool submit, byte[] rowVersion, CancellationToken cancellationToken);
    Task<ApplicationOperationResult> CreateAsync(string studentUserId, CreateApplicationInput input, CancellationToken cancellationToken);
    Task<ApplicationOperationResult> ReviewAsync(string reviewerUserId, long applicationId, ReviewApplicationInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReviewApplicationDto>> GetReviewQueueAsync(bool allStatuses, CancellationToken cancellationToken);
    Task<ReviewApplicationDto?> GetReviewApplicationAsync(long applicationId, CancellationToken cancellationToken);
    Task<AdministratorDashboardData> GetAdministratorDashboardAsync(int recentLimit, CancellationToken cancellationToken);
}

public sealed class AdmissionApplicationService(IAdmissionApplicationRepository repository) : IAdmissionApplicationService
{
    public async Task<IReadOnlyList<StudentApplicationDto>> GetStudentApplicationsAsync(string studentUserId, CancellationToken cancellationToken) =>
        (await repository.GetForStudentAsync(studentUserId, cancellationToken)).Select(ToStudentDto).ToArray();

    public async Task<StudentApplicationDto?> GetStudentApplicationAsync(long applicationId, string studentUserId, CancellationToken cancellationToken)
    {
        var application = await repository.GetForStudentAsync(applicationId, studentUserId, cancellationToken);
        return application is null ? null : ToStudentDto(application);
    }

    public async Task<ApplicationOperationResult> CreateAsync(string studentUserId, CreateApplicationInput input, CancellationToken cancellationToken)
    {
        var course = await repository.GetActiveCourseAsync(input.CourseId, cancellationToken);
        if (course is null)
        {
            return Failure("The selected course is not active or does not exist.", "CourseId");
        }

        var existingDraft = await repository.GetDraftForCourseAsync(studentUserId, input.CourseId, cancellationToken);
        if (existingDraft is not null && input.Submit)
        {
            existingDraft.PersonalStatement = input.PersonalStatement?.Trim();
            if (string.IsNullOrWhiteSpace(existingDraft.PersonalStatement))
            {
                return Failure("A personal statement is required before submission.", "PersonalStatement");
            }
            var submittedAt = DateTimeOffset.UtcNow;
            existingDraft.Status = AdmissionStatus.Submitted;
            existingDraft.SubmittedUtc = submittedAt;
            existingDraft.UpdatedUtc = submittedAt;
            existingDraft.StatusHistory.Add(new ApplicationStatusHistory
            {
                ApplicationId = existingDraft.ApplicationId, ChangedByUserId = studentUserId,
                FromStatus = AdmissionStatus.Draft, ToStatus = AdmissionStatus.Submitted, ChangedAtUtc = submittedAt
            });
            if (!await repository.UpdateAsync(existingDraft, existingDraft.RowVersion, cancellationToken))
            {
                return new ApplicationOperationResult(null, null, "This application was changed by another request. Refresh and try again.", null, true);
            }
            return new ApplicationOperationResult(ToStudentDto(existingDraft), null, null, null);
        }
        if (existingDraft is not null || await repository.ExistsForCourseAsync(studentUserId, input.CourseId, cancellationToken))
        {
            return Failure("You already have an application for this course.", "CourseId");
        }

        var statement = input.PersonalStatement?.Trim();
        if (statement?.Length > 4000)
        {
            return Failure("Personal statement must be 4,000 characters or fewer.", "PersonalStatement");
        }
        if (input.Submit && string.IsNullOrWhiteSpace(statement))
        {
            return Failure("A personal statement is required before submission.", "PersonalStatement");
        }

        var now = DateTimeOffset.UtcNow;
        var application = new AdmissionApplication
        {
            StudentUserId = studentUserId,
            CourseId = course.CourseId,
            Course = course,
            PersonalStatement = statement,
            Status = input.Submit ? AdmissionStatus.Submitted : AdmissionStatus.Draft,
            CreatedUtc = now,
            SubmittedUtc = input.Submit ? now : null,
            UpdatedUtc = now
        };

        if (input.Submit)
        {
            application.StatusHistory.Add(new ApplicationStatusHistory
            {
                ChangedByUserId = studentUserId,
                FromStatus = AdmissionStatus.Draft,
                ToStatus = AdmissionStatus.Submitted,
                ChangedAtUtc = now
            });
        }

        if (!await repository.AddAsync(application, cancellationToken))
        {
            return Failure("You already have an application for this course.", "CourseId");
        }
        return new ApplicationOperationResult(ToStudentDto(application), null, null, null);
    }

    public async Task<ApplicationOperationResult> ReviewAsync(string reviewerUserId, long applicationId, ReviewApplicationInput input, CancellationToken cancellationToken)
    {
        var application = await repository.GetByIdAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return Failure("Application not found.", null);
        }

        var valid = application.Status switch
        {
            AdmissionStatus.Submitted when input.ToStatus == AdmissionStatus.UnderReview => true,
            AdmissionStatus.UnderReview when input.ToStatus is AdmissionStatus.Approved or AdmissionStatus.Rejected => true,
            _ => false
        };
        if (!valid)
        {
            return Failure($"The transition from {application.Status} to {input.ToStatus} is not permitted.", null);
        }
        if (input.PublicMessage?.Length > 500 || input.InternalNote?.Length > 2000)
        {
            return Failure("The review message is too long.", input.PublicMessage?.Length > 500 ? "PublicMessage" : "InternalNote");
        }

        var history = new ApplicationStatusHistory
        {
            ApplicationId = application.ApplicationId,
            ChangedByUserId = reviewerUserId,
            FromStatus = application.Status,
            ToStatus = input.ToStatus,
            ChangedAtUtc = DateTimeOffset.UtcNow,
            PublicMessage = input.PublicMessage?.Trim(),
            InternalNote = input.InternalNote?.Trim()
        };
        application.Status = input.ToStatus;
        application.UpdatedUtc = history.ChangedAtUtc;

        if (!await repository.SaveReviewAsync(application, history, input.RowVersion, cancellationToken))
        {
            return new ApplicationOperationResult(null, null, "This application was changed by another reviewer. Refresh and try again.", null, true);
        }

        return new ApplicationOperationResult(null, ToReviewDto(application), null, null);
    }

    public async Task<ApplicationOperationResult> UpdateDraftAsync(
        string studentUserId,
        long applicationId,
        string? personalStatement,
        bool submit,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        var application = await repository.GetDraftForStudentAsync(applicationId, studentUserId, cancellationToken);
        if (application is null)
        {
            return Failure("Draft application not found.", null);
        }

        var statement = personalStatement?.Trim();
        if (statement?.Length > 4000)
        {
            return Failure("Personal statement must be 4,000 characters or fewer.", "PersonalStatement");
        }
        if (submit && string.IsNullOrWhiteSpace(statement))
        {
            return Failure("A personal statement is required before submission.", "PersonalStatement");
        }

        var now = DateTimeOffset.UtcNow;
        application.PersonalStatement = statement;
        application.UpdatedUtc = now;
        if (submit)
        {
            application.Status = AdmissionStatus.Submitted;
            application.SubmittedUtc = now;
            application.StatusHistory.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.ApplicationId,
                ChangedByUserId = studentUserId,
                FromStatus = AdmissionStatus.Draft,
                ToStatus = AdmissionStatus.Submitted,
                ChangedAtUtc = now
            });
        }

        if (!await repository.UpdateAsync(application, rowVersion, cancellationToken))
        {
            return new ApplicationOperationResult(null, null, "This application was changed by another request. Refresh and try again.", null, true);
        }

        return new ApplicationOperationResult(ToStudentDto(application), null, null, null);
    }

    public async Task<IReadOnlyList<ReviewApplicationDto>> GetReviewQueueAsync(bool allStatuses, CancellationToken cancellationToken) =>
        (await repository.GetReviewQueueAsync(allStatuses, cancellationToken)).Select(ToReviewDto).ToArray();

    public Task<AdministratorDashboardData> GetAdministratorDashboardAsync(int recentLimit, CancellationToken cancellationToken) =>
        repository.GetAdministratorDashboardAsync(recentLimit, cancellationToken);

    public async Task<ReviewApplicationDto?> GetReviewApplicationAsync(long applicationId, CancellationToken cancellationToken)
    {
        var application = await repository.GetByIdAsync(applicationId, cancellationToken);
        return application is null ? null : ToReviewDto(application);
    }

    private static ApplicationOperationResult Failure(string error, string? field) => new(null, null, error, field);

    private static StudentApplicationDto ToStudentDto(AdmissionApplication application) => new(
        application.ApplicationId, Reference(application.ApplicationId), application.CourseId, application.Course?.Code ?? string.Empty,
        application.Course?.Name ?? string.Empty, application.PersonalStatement, application.CreatedUtc, application.SubmittedUtc,
        application.Status, application.RowVersion.ToArray(), application.StatusHistory.OrderBy(h => h.ChangedAtUtc)
            .Select(h => new PublicApplicationHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAtUtc, h.PublicMessage)).ToArray());

    private static ReviewApplicationDto ToReviewDto(AdmissionApplication application) => new(
        application.ApplicationId, Reference(application.ApplicationId), application.StudentUserId, application.CourseId,
        application.Course?.Code ?? string.Empty, application.Course?.Name ?? string.Empty, application.PersonalStatement,
        application.CreatedUtc, application.SubmittedUtc, application.Status, application.RowVersion.ToArray(),
        application.StatusHistory.OrderBy(h => h.ChangedAtUtc)
            .Select(h => new ApplicationHistoryDto(h.FromStatus, h.ToStatus, h.ChangedAtUtc, h.PublicMessage, h.InternalNote, h.ChangedByUserId)).ToArray());

    private static string Reference(long id) => $"APP-{id:D8}";
}
