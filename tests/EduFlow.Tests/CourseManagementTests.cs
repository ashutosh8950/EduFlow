using System.Security.Claims;
using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.Tests;

public sealed class CourseManagementTests
{
    [Fact]
    public async Task CreateCoursePersistsNormalizedCourse()
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));

        var result = await service.CreateAsync(ValidCourse(code: " cs-101 "), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("CS-101", result.Course!.Code);
        Assert.Equal("Introduction to computing", result.Course.Name);
        Assert.Single(await context.Courses.ToListAsync());
    }

    [Fact]
    public async Task DuplicateCourseCodeIsRejectedIgnoringCase()
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));
        Assert.True((await service.CreateAsync(ValidCourse("BIO-12"), CancellationToken.None)).Succeeded);

        var result = await service.CreateAsync(ValidCourse("bio-12"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Code", result.ErrorField);
        Assert.Contains("already exists", result.Error);
        Assert.Single(await context.Courses.ToListAsync());
    }

    [Theory]
    [InlineData("", "Name", "Description", 12, "Code")]
    [InlineData("BIO-12", "", "Description", 12, "Name")]
    [InlineData("BIO-12", "Name", "Description", 0, "DurationMonths")]
    [InlineData("BIO-12", "Name", "Description", 32768, "DurationMonths")]
    [InlineData("BIO-12", "Name", "", 12, "Description")]
    public async Task InvalidCourseDataIsRejected(
        string code,
        string name,
        string description,
        int durationMonths,
        string expectedField)
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));

        var result = await service.CreateAsync(
            new CourseInput(code, name, description, durationMonths, true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedField, result.ErrorField);
        Assert.NotEmpty(result.Error!);
        Assert.Empty(await context.Courses.ToListAsync());
    }

    [Fact]
    public async Task EditCourseUpdatesFieldsAndUtcTimestamp()
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));
        var created = await service.CreateAsync(ValidCourse("BIO-12"), CancellationToken.None);

        var result = await service.UpdateAsync(created.Course!.CourseId,
            new CourseInput("BIO-13", "Biology II", "Advanced biology", 24, true),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("BIO-13", result.Course!.Code);
        Assert.Equal("Biology II", result.Course.Name);
        Assert.Equal("Advanced biology", result.Course.Description);
        Assert.Equal(24, result.Course.DurationMonths);
        Assert.NotNull(result.Course.UpdatedUtc);
    }

    [Fact]
    public async Task DeactivationPreservesCourseAndUpdatesTimestamp()
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));
        var created = await service.CreateAsync(ValidCourse("BIO-12"), CancellationToken.None);

        var updated = await service.SetActiveAsync(created.Course!.CourseId, false, CancellationToken.None);

        Assert.True(updated);
        var course = await context.Courses.SingleAsync();
        Assert.False(course.IsActive);
        Assert.NotNull(course.UpdatedUtc);
    }

    [Fact]
    public async Task StudentCatalogOnlyContainsActiveCourses()
    {
        await using var context = CreateDbContext();
        var service = new CourseService(new CourseRepository(context));
        var active = await service.CreateAsync(ValidCourse("BIO-12"), CancellationToken.None);
        var inactive = await service.CreateAsync(ValidCourse("BIO-13") with { IsActive = false }, CancellationToken.None);

        var catalog = await service.GetActiveAsync(CancellationToken.None);
        var inactiveDetails = await service.GetActiveByIdAsync(inactive.Course!.CourseId, CancellationToken.None);

        Assert.Single(catalog);
        Assert.Equal(active.Course!.CourseId, catalog[0].CourseId);
        Assert.Null(inactiveDetails);
    }

    [Fact]
    public async Task StudentIsDeniedAdministratorCourseManagement()
    {
        var authorization = CreateAuthorizationService();
        var student = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Student)], "test"));

        var result = await authorization.AuthorizeAsync(student, null, "AdministratorOnly");

        Assert.False(result.Succeeded);
        Assert.Equal("AdministratorOnly", Assert.Single(GetAuthorizeAttributes(typeof(CoursesController))).Policy);
    }

    [Fact]
    public async Task AdministratorIsAuthorizedToManageCourses()
    {
        var authorization = CreateAuthorizationService();
        var administrator = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Administrator)], "test"));

        var result = await authorization.AuthorizeAsync(administrator, null, "AdministratorOnly");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UnauthenticatedUserIsDeniedProtectedCourseApi()
    {
        var authorization = CreateAuthorizationService();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await authorization.AuthorizeAsync(anonymous, null, "StudentAccess");

        Assert.False(result.Succeeded);
        Assert.Equal("StudentAccess", Assert.Single(GetAuthorizeAttributes(typeof(CourseApiController))).Policy);
    }

    private static CourseInput ValidCourse(string code) => new(
        code,
        "Introduction to computing",
        "An overview of computing concepts and practices.",
        12,
        true);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IAuthorizationService CreateAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdministratorOnly", policy => policy.RequireRole(RoleNames.Administrator));
            options.AddPolicy("StudentAccess", policy => policy.RequireRole(RoleNames.Student));
        });
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static IReadOnlyList<AuthorizeAttribute> GetAuthorizeAttributes(Type controllerType) =>
        controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToArray();
}