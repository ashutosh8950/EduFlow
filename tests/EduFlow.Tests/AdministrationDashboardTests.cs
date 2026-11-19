using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EduFlow.Web.Controllers;

namespace EduFlow.Tests;

public sealed class AdministrationDashboardTests
{
    [Fact]
    public async Task EmptyDatabaseReturnsZeroStatisticsAndNoRecentApplications()
    {
        await using var db = CreateDb();
        var dashboard = await Service(db).GetAdministratorDashboardAsync(8, CancellationToken.None);

        Assert.Equal(0, dashboard.TotalApplications);
        Assert.Equal(0, dashboard.SubmittedApplications);
        Assert.Equal(0, dashboard.UnderReviewApplications);
        Assert.Equal(0, dashboard.ApprovedApplications);
        Assert.Equal(0, dashboard.RejectedApplications);
        Assert.Equal(0, dashboard.ActiveCourses);
        Assert.Empty(dashboard.RecentApplications);
    }

    [Fact]
    public async Task DashboardReturnsStatusCountsActiveCoursesAndRecentApplications()
    {
        await using var db = CreateDb();
        var user = new ApplicationUser
        {
            Id = "student-1",
            FirstName = "Ada",
            LastName = "Lovelace",
            UserName = "ada@example.com",
            NormalizedUserName = "ADA@EXAMPLE.COM",
            Email = "ada@example.com",
            NormalizedEmail = "ADA@EXAMPLE.COM"
        };
        var activeCourse = new Course { Code = "ACTIVE-1", Name = "Active Course", Description = "Active", IsActive = true };
        var inactiveCourse = new Course { Code = "INACTIVE-1", Name = "Inactive Course", Description = "Inactive", IsActive = false };
        db.Users.Add(user);
        db.Courses.AddRange(activeCourse, inactiveCourse);
        db.AdmissionApplications.AddRange(
            Application(user.Id, activeCourse, AdmissionStatus.Draft, 1),
            Application(user.Id, activeCourse, AdmissionStatus.Submitted, 2),
            Application(user.Id, activeCourse, AdmissionStatus.UnderReview, 3),
            Application(user.Id, inactiveCourse, AdmissionStatus.Approved, 4),
            Application(user.Id, inactiveCourse, AdmissionStatus.Rejected, 5));
        await db.SaveChangesAsync();

        var dashboard = await Service(db).GetAdministratorDashboardAsync(3, CancellationToken.None);

        Assert.Equal(5, dashboard.TotalApplications);
        Assert.Equal(1, dashboard.SubmittedApplications);
        Assert.Equal(1, dashboard.UnderReviewApplications);
        Assert.Equal(1, dashboard.ApprovedApplications);
        Assert.Equal(1, dashboard.RejectedApplications);
        Assert.Equal(1, dashboard.ActiveCourses);
        Assert.Equal(3, dashboard.RecentApplications.Count);
        Assert.Equal("Ada Lovelace", dashboard.RecentApplications[0].StudentName);
        Assert.Equal("Inactive Course", dashboard.RecentApplications[0].CourseName);
    }

    [Fact]
    public void AdministrationDashboardRequiresAdministratorPolicy()
    {
        var authorize = typeof(AdministrationController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("AdministratorOnly", authorize.Policy);
    }

    private static AdmissionApplicationService Service(ApplicationDbContext db) =>
        new(new AdmissionApplicationRepository(db));

    private static AdmissionApplication Application(string userId, Course course, AdmissionStatus status, int day) =>
        new()
        {
            StudentUserId = userId,
            CourseId = course.CourseId,
            Course = course,
            Status = status,
            CreatedUtc = new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero),
            UpdatedUtc = new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero),
            SubmittedUtc = status is AdmissionStatus.Draft ? null : new DateTimeOffset(2026, 11, day, 1, 0, 0, TimeSpan.Zero)
        };

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
