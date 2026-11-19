using System.Security.Claims;
using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Web.Controllers;

[Authorize(Policy = "StudentAccess")]
public sealed class StudentController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ICourseService courseService,
    IAdmissionApplicationService admissionService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Courses(CancellationToken cancellationToken)
    {
        var courses = await courseService.GetActiveAsync(cancellationToken);
        return View(courses.Select(course => new CourseListItemViewModel(
            course.CourseId, course.Code, course.Name, course.DurationMonths,
            course.IsActive, course.CreatedUtc, course.UpdatedUtc)).ToArray());
    }

    [HttpGet]
    public async Task<IActionResult> CourseDetails(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var course = await courseService.GetActiveByIdAsync(id, cancellationToken);
        return course is null
            ? NotFound()
            : View(new CourseDetailsViewModel(
                course.CourseId, course.Code, course.Name, course.Description, course.DurationMonths,
                course.IsActive, course.CreatedUtc, course.UpdatedUtc));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Challenge();
        }

        var user = await userManager.FindByIdAsync(userId);
        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (user is null || profile is null)
        {
            return NotFound();
        }

        return View(new StudentProfileViewModel(user.FirstName, user.LastName, user.Email ?? string.Empty));
    }

    [HttpGet]
    public async Task<IActionResult> MyApplications(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        var applications = await admissionService.GetStudentApplicationsAsync(userId, cancellationToken);
        return View(applications.Select(a => new StudentApplicationListItemViewModel(
            a.ApplicationId, a.Reference, a.CourseCode, a.CourseName, a.CreatedUtc, a.SubmittedUtc, a.Status)).ToArray());
    }

    [HttpGet]
    public async Task<IActionResult> CreateApplication(int courseId, CancellationToken cancellationToken)
    {
        if (courseId <= 0) return NotFound();
        var course = await courseService.GetActiveByIdAsync(courseId, cancellationToken);
        return course is null ? NotFound() : View(new CreateApplicationViewModel
        {
            CourseId = course.CourseId, CourseCode = course.Code, CourseName = course.Name
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateApplication(CreateApplicationViewModel model, string action, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (!ModelState.IsValid) return View(model);

        var result = await admissionService.CreateAsync(userId,
            new CreateApplicationInput(model.CourseId, model.PersonalStatement, string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.ErrorField ?? string.Empty, result.Error ?? "The application could not be saved.");
            return View(model);
        }

        TempData["SuccessMessage"] = result.StudentApplication!.Status == AdmissionStatus.Submitted
            ? "Application submitted." : "Application saved as a draft.";
        return RedirectToAction(nameof(ApplicationDetails), new { id = result.StudentApplication.ApplicationId });
    }

    [HttpGet]
    public async Task<IActionResult> ApplicationDetails(long id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        var application = await admissionService.GetStudentApplicationAsync(id, userId, cancellationToken);
        return application is null ? NotFound() : View(new StudentApplicationDetailsViewModel(
            application.ApplicationId, application.Reference, application.CourseCode, application.CourseName,
            application.PersonalStatement, application.CreatedUtc, application.SubmittedUtc, application.Status,
            application.StatusHistory));
    }

    [HttpGet]
    public async Task<IActionResult> EditApplication(long id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var application = await admissionService.GetStudentApplicationAsync(id, userId, cancellationToken);
        if (application is null || application.Status != AdmissionStatus.Draft)
        {
            return NotFound();
        }

        return View(new EditDraftApplicationViewModel
        {
            ApplicationId = application.ApplicationId,
            Reference = application.Reference,
            CourseCode = application.CourseCode,
            CourseName = application.CourseName,
            PersonalStatement = application.PersonalStatement,
            RowVersion = application.RowVersion
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditApplication(EditDraftApplicationViewModel model, string action, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();
        if (!ModelState.IsValid) return View(model);

        var result = await admissionService.UpdateDraftAsync(
            userId,
            model.ApplicationId,
            model.PersonalStatement,
            string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase),
            model.RowVersion,
            cancellationToken);

        if (!result.Succeeded)
        {
            if (result.Conflict)
            {
                ModelState.AddModelError(string.Empty, result.Error!);
                return View(model);
            }

            return result.ErrorField is null
                ? NotFound()
                : AddApplicationError(model, result.ErrorField, result.Error!);
        }

        TempData["SuccessMessage"] = result.StudentApplication!.Status == AdmissionStatus.Submitted
            ? "Application submitted." : "Draft updated.";
        return RedirectToAction(nameof(ApplicationDetails), new { id = result.StudentApplication.ApplicationId });
    }

    private IActionResult AddApplicationError(EditDraftApplicationViewModel model, string field, string error)
    {
        ModelState.AddModelError(field, error);
        return View(model);
    }
}
