using EduFlow.Application.Services;
using EduFlow.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Web.Controllers;

[Authorize(Policy = "AdministratorOnly")]
public sealed class CoursesController(ICourseService courseService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var courses = await courseService.GetAllAsync(cancellationToken);
        return View(courses.Select(course => new CourseListItemViewModel(
            course.CourseId, course.Code, course.Name, course.DurationMonths,
            course.IsActive, course.CreatedUtc, course.UpdatedUtc)).ToArray());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var course = await courseService.GetByIdAsync(id, cancellationToken);
        return course is null
            ? NotFound()
            : View(ToDetailsViewModel(course));
    }

    [HttpGet]
    public IActionResult Create() => View(new CourseFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await courseService.CreateAsync(ToInput(model), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.ErrorField ?? string.Empty, result.Error ?? "The course could not be created.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Course created.";
        return RedirectToAction(nameof(Details), new { id = result.Course!.CourseId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var course = await courseService.GetByIdAsync(id, cancellationToken);
        return course is null
            ? NotFound()
            : View(new CourseFormViewModel
            {
                Code = course.Code,
                Name = course.Name,
                Description = course.Description,
                DurationMonths = course.DurationMonths,
                IsActive = course.IsActive
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CourseFormViewModel model, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await courseService.UpdateAsync(id, ToInput(model), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Course is null && result.Error is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(result.ErrorField ?? string.Empty, result.Error ?? "The course could not be updated.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Course updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        if (!await courseService.SetActiveAsync(id, isActive, cancellationToken))
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = isActive ? "Course activated." : "Course deactivated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static CourseInput ToInput(CourseFormViewModel model) => new(
        model.Code, model.Name, model.Description, model.DurationMonths, model.IsActive);

    private static CourseDetailsViewModel ToDetailsViewModel(CourseDto course) => new(
        course.CourseId, course.Code, course.Name, course.Description, course.DurationMonths,
        course.IsActive, course.CreatedUtc, course.UpdatedUtc);
}

[ApiController]
[Route("api/v1/courses")]
[Authorize(Policy = "StudentAccess")]
public sealed class CourseApiController(ICourseService courseService) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List active courses")]
    [EndpointDescription("Returns the active courses available to authenticated students.")]
    [ProducesResponseType(typeof(IReadOnlyList<CourseApiResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CourseApiResponse>>> GetCourses(CancellationToken cancellationToken)
    {
        var courses = await courseService.GetActiveAsync(cancellationToken);
        return Ok(courses.Select(ToResponse).ToArray());
    }

    [HttpGet("{id:int}")]
    [EndpointSummary("Get an active course")]
    [EndpointDescription("Returns details for an active course, or 404 when it does not exist or is inactive.")]
    [ProducesResponseType(typeof(CourseApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseApiResponse>> GetCourse(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var course = await courseService.GetActiveByIdAsync(id, cancellationToken);
        return course is null ? NotFound() : Ok(ToResponse(course));
    }

    private static CourseApiResponse ToResponse(CourseDto course) => new(
        course.CourseId, course.Code, course.Name, course.Description, course.DurationMonths,
        course.IsActive, course.CreatedUtc, course.UpdatedUtc);
}