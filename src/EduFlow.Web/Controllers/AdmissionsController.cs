using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Web.Controllers;

[Authorize(Policy = "CanReviewApplications")]
public sealed class AdmissionsController(IAdmissionApplicationService admissionService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var allStatuses = User.IsInRole(RoleNames.Administrator);
        var applications = await admissionService.GetReviewQueueAsync(allStatuses, cancellationToken);
        return View(applications.Select(ToListItem).ToArray());
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var application = await admissionService.GetReviewApplicationAsync(id, cancellationToken);
        return application is null ? NotFound() : View(ToDetails(application));
    }

    [HttpGet]
    public async Task<IActionResult> Review(long id, CancellationToken cancellationToken)
    {
        var application = await admissionService.GetReviewApplicationAsync(id, cancellationToken);
        return application is null ? NotFound() : View(ToReviewModel(application));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(ReviewApplicationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Challenge();

        var result = await admissionService.ReviewAsync(userId, model.ApplicationId,
            new ReviewApplicationInput(model.ToStatus, model.PublicMessage, model.InternalNote, model.RowVersion), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The review could not be saved.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Application review saved.";
        return RedirectToAction(nameof(Details), new { id = model.ApplicationId });
    }

    private static ReviewApplicationListItemViewModel ToListItem(ReviewApplicationDto a) =>
        new(a.ApplicationId, a.Reference, a.CourseCode, a.CourseName, a.CreatedUtc, a.SubmittedUtc, a.Status);

    private static ReviewApplicationDetailsViewModel ToDetails(ReviewApplicationDto a) =>
        new(a.ApplicationId, a.Reference, a.CourseCode, a.CourseName, a.PersonalStatement, a.CreatedUtc, a.SubmittedUtc,
            a.Status, a.RowVersion, a.StatusHistory);

    private static ReviewApplicationViewModel ToReviewModel(ReviewApplicationDto a) =>
        new() { ApplicationId = a.ApplicationId, Reference = a.Reference, CourseCode = a.CourseCode, CourseName = a.CourseName,
            PersonalStatement = a.PersonalStatement, Status = a.Status, RowVersion = a.RowVersion,
            ToStatus = a.Status == AdmissionStatus.Submitted ? AdmissionStatus.UnderReview : AdmissionStatus.Approved };
}
