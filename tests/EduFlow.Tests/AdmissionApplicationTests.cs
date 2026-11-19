using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Tests;

public sealed class AdmissionWorkflowTests
{
    [Fact]
    public async Task StudentCanCreateDraftAndSubmitIt()
    {
        await using var db = CreateDb();
        var course = AddCourse(db, true);
        var service = Service(db);

        var draft = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, null, false), CancellationToken.None);
        var submitted = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "My statement", true), CancellationToken.None);

        Assert.True(draft.Succeeded);
        Assert.True(submitted.Succeeded);
        Assert.Equal(AdmissionStatus.Submitted, submitted.StudentApplication!.Status);
        Assert.Single(await db.ApplicationStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task ExistingDraftLoadsWithSavedStatementAndCanBeEditedWithoutDuplication()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "Original statement", false), CancellationToken.None);

        var loaded = await service.GetStudentApplicationAsync(created.StudentApplication!.ApplicationId, "student-1", CancellationToken.None);
        var updated = await service.UpdateDraftAsync("student-1", loaded!.ApplicationId, "Updated statement", false, loaded.RowVersion, CancellationToken.None);

        Assert.Equal("Original statement", loaded.PersonalStatement);
        Assert.True(updated.Succeeded);
        Assert.Equal(loaded.ApplicationId, updated.StudentApplication!.ApplicationId);
        Assert.Equal("Updated statement", updated.StudentApplication.PersonalStatement);
        Assert.Single(await db.AdmissionApplications.ToListAsync());
    }

    [Fact]
    public async Task ExistingDraftCanBeSubmittedThroughDraftUpdate()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "Draft statement", false), CancellationToken.None);
        var submitted = await service.UpdateDraftAsync("student-1", created.StudentApplication!.ApplicationId, "Final statement", true, created.StudentApplication.RowVersion, CancellationToken.None);

        Assert.True(submitted.Succeeded);
        Assert.Equal(AdmissionStatus.Submitted, submitted.StudentApplication!.Status);
        Assert.NotNull(submitted.StudentApplication.SubmittedUtc);
        Assert.Single(await db.ApplicationStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task SubmittedDraftCannotBeSubmittedAgain()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "Statement", false), CancellationToken.None);
        var submitted = await service.UpdateDraftAsync("student-1", created.StudentApplication!.ApplicationId, "Statement", true, created.StudentApplication.RowVersion, CancellationToken.None);
        var secondAttempt = await service.UpdateDraftAsync("student-1", created.StudentApplication.ApplicationId, "Changed", true, submitted.StudentApplication!.RowVersion, CancellationToken.None);

        Assert.False(secondAttempt.Succeeded);
        Assert.Single(await db.ApplicationStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task StudentCannotEditAnotherStudentsDraft()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "Private draft", false), CancellationToken.None);
        var result = await service.UpdateDraftAsync("student-2", created.StudentApplication!.ApplicationId, "Tampered", false, created.StudentApplication.RowVersion, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Private draft", (await db.AdmissionApplications.SingleAsync()).PersonalStatement);
    }

    [Fact]
    public async Task SubmittedApplicationCannotBeEditedAsDraft()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "Submitted", true), CancellationToken.None);
        var result = await service.UpdateDraftAsync("student-1", created.StudentApplication!.ApplicationId, "Changed", false, created.StudentApplication.RowVersion, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Submitted", (await db.AdmissionApplications.SingleAsync()).PersonalStatement);
    }

    [Fact]
    public async Task DuplicateSubmittedApplicationIsRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        Assert.True((await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None)).Succeeded);
        var duplicate = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "B", true), CancellationToken.None);
        Assert.False(duplicate.Succeeded);
    }

    [Fact]
    public void DatabaseModelDefinesUniqueStudentCourseIndex()
    {
        using var db = CreateDb();
        var index = db.Model.FindEntityType(typeof(AdmissionApplication))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(AdmissionApplication.StudentUserId), nameof(AdmissionApplication.CourseId)]));
        Assert.True(index.IsUnique);
        Assert.Equal("UX_AdmissionApplications_StudentUserId_CourseId", index.GetDatabaseName());
    }

    [Fact]
    public async Task InactiveCourseIsRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, false); var service = Service(db);
        var result = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmissionWithoutStatementIsRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var result = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, " ", true), CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Equal("PersonalStatement", result.ErrorField);
    }

    [Fact]
    public async Task OverlongReviewMessageIsRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var result = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId,
            new ReviewApplicationInput(AdmissionStatus.UnderReview, new string('x', 501), null, created.StudentApplication.RowVersion), CancellationToken.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StudentReadsOnlyOwnApplicationsAndPublicHistory()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var first = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        await service.ReviewAsync("officer", first.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, "Received", "secret", first.StudentApplication.RowVersion), CancellationToken.None);
        var own = await service.GetStudentApplicationsAsync("student-1", CancellationToken.None);
        var other = await service.GetStudentApplicationAsync(first.StudentApplication.ApplicationId, "student-2", CancellationToken.None);
        Assert.Single(own); Assert.Null(other); Assert.Equal("Received", own[0].StatusHistory[1].PublicMessage);
        Assert.DoesNotContain("secret", own[0].StatusHistory.Select(h => h.PublicMessage));
    }

    [Fact]
    public async Task ValidReviewTransitionsPersistHistory()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var underReview = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, null, "check", created.StudentApplication.RowVersion), CancellationToken.None);
        var approved = await service.ReviewAsync("officer", created.StudentApplication.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Approved, "Welcome", null, underReview.ReviewApplication!.RowVersion), CancellationToken.None);
        Assert.True(approved.Succeeded); Assert.Equal(3, await db.ApplicationStatusHistory.CountAsync());
    }

    [Fact]
    public async Task InvalidReviewTransitionIsRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var result = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Approved, null, null, created.StudentApplication.RowVersion), CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Single(await db.ApplicationStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task UnderReviewCanBeRejected()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var review = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, null, null, created.StudentApplication.RowVersion), CancellationToken.None);
        var rejected = await service.ReviewAsync("officer", created.StudentApplication.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Rejected, "Not this time", null, review.ReviewApplication!.RowVersion), CancellationToken.None);
        Assert.True(rejected.Succeeded); Assert.Equal(AdmissionStatus.Rejected, rejected.ReviewApplication!.Status);
    }

    [Fact]
    public async Task ApprovedApplicationCannotBeReviewedAgain()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var review = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, null, null, created.StudentApplication.RowVersion), CancellationToken.None);
        var approved = await service.ReviewAsync("officer", created.StudentApplication.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Approved, null, null, review.ReviewApplication!.RowVersion), CancellationToken.None);
        var again = await service.ReviewAsync("officer", created.StudentApplication.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Rejected, null, null, approved.ReviewApplication!.RowVersion), CancellationToken.None);
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task OfficerQueueExcludesApprovedApplications()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var review = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, null, null, created.StudentApplication.RowVersion), CancellationToken.None);
        await service.ReviewAsync("officer", created.StudentApplication.ApplicationId, new ReviewApplicationInput(AdmissionStatus.Approved, null, null, review.ReviewApplication!.RowVersion), CancellationToken.None);
        Assert.Empty(await service.GetReviewQueueAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task DraftIsReturnedToItsOwner()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "draft", false), CancellationToken.None);
        var details = await service.GetStudentApplicationAsync(created.StudentApplication!.ApplicationId, "student-1", CancellationToken.None);
        Assert.Equal(AdmissionStatus.Draft, details!.Status); Assert.Equal("draft", details.PersonalStatement);
    }

    [Fact]
    public async Task PublicStudentProjectionContainsNoInternalNoteField()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        var created = await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, "A", true), CancellationToken.None);
        var result = await service.ReviewAsync("officer", created.StudentApplication!.ApplicationId, new ReviewApplicationInput(AdmissionStatus.UnderReview, null, "private", created.StudentApplication.RowVersion), CancellationToken.None);
        var publicDetails = await service.GetStudentApplicationAsync(created.StudentApplication.ApplicationId, "student-1", CancellationToken.None);
        Assert.DoesNotContain("private", publicDetails!.StatusHistory.Select(h => h.PublicMessage));
        Assert.NotNull(result.ReviewApplication);
    }

    [Fact]
    public async Task ReviewQueueCanIncludeAllApplicationsForAdministrator()
    {
        await using var db = CreateDb(); var course = AddCourse(db, true); var service = Service(db);
        await service.CreateAsync("student-1", new CreateApplicationInput(course.CourseId, null, false), CancellationToken.None);
        var all = await service.GetReviewQueueAsync(true, CancellationToken.None);
        var reviewable = await service.GetReviewQueueAsync(false, CancellationToken.None);
        Assert.Single(all); Assert.Empty(reviewable);
    }

    private static AdmissionApplicationService Service(ApplicationDbContext db) => new(new AdmissionApplicationRepository(db));
    private static Course AddCourse(ApplicationDbContext db, bool active)
    {
        var course = new Course { Code = Guid.NewGuid().ToString("N")[..8], Name = "Course", Description = "Description", DurationMonths = 12, IsActive = active };
        db.Courses.Add(course); db.SaveChanges(); return course;
    }
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
