using System.ComponentModel.DataAnnotations;

namespace EduFlow.Web.Models;

public sealed class CourseFormViewModel
{
    [Display(Name = "Course code")]
    [Required(ErrorMessage = "Course code is required.")]
    [StringLength(20, ErrorMessage = "Course code must be 20 characters or fewer.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Course name")]
    [Required(ErrorMessage = "Course name is required.")]
    [StringLength(150, ErrorMessage = "Course name must be 150 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(2000, ErrorMessage = "Description must be 2,000 characters or fewer.")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Duration in months")]
    [Required(ErrorMessage = "Duration is required.")]
    [Range(1, short.MaxValue, ErrorMessage = "Duration must be a positive number of months.")]
    public int? DurationMonths { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public sealed record CourseListItemViewModel(
    int CourseId,
    string Code,
    string Name,
    int? DurationMonths,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record CourseDetailsViewModel(
    int CourseId,
    string Code,
    string Name,
    string Description,
    int? DurationMonths,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

/// <summary>Course data returned by the versioned course API.</summary>
public sealed record CourseApiResponse(
    int CourseId,
    string Code,
    string Name,
    string Description,
    int? DurationMonths,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);