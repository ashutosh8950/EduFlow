using EduFlow.Application.Domain;

namespace EduFlow.Web.Models;

public sealed record AdministrationDashboardViewModel(
    int TotalApplications,
    int SubmittedApplications,
    int UnderReviewApplications,
    int ApprovedApplications,
    int RejectedApplications,
    int ActiveCourses,
    IReadOnlyList<AdministrationRecentApplicationViewModel> RecentApplications);

public sealed record AdministrationRecentApplicationViewModel(
    long ApplicationId,
    string Reference,
    string StudentName,
    string CourseName,
    AdmissionStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc);
