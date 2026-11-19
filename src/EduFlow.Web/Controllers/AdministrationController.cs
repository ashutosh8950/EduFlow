using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EduFlow.Application.Services;
using EduFlow.Web.Models;

namespace EduFlow.Web.Controllers;

[Authorize(Policy = "AdministratorOnly")]
public sealed class AdministrationController : Controller
{
    private readonly IAdmissionApplicationService applicationService;

    public AdministrationController(IAdmissionApplicationService applicationService)
    {
        this.applicationService = applicationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await applicationService.GetAdministratorDashboardAsync(8, cancellationToken);
        var viewModel = new AdministrationDashboardViewModel(
            dashboard.TotalApplications,
            dashboard.SubmittedApplications,
            dashboard.UnderReviewApplications,
            dashboard.ApprovedApplications,
            dashboard.RejectedApplications,
            dashboard.ActiveCourses,
            dashboard.RecentApplications.Select(application => new AdministrationRecentApplicationViewModel(
                application.ApplicationId,
                $"APP-{application.ApplicationId:D8}",
                application.StudentName,
                application.CourseName,
                application.Status,
                application.CreatedUtc,
                application.SubmittedUtc)).ToArray());

        return View(viewModel);
    }
}
