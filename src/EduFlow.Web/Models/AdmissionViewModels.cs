using System.ComponentModel.DataAnnotations;
using EduFlow.Application.Domain;
using EduFlow.Application.Services;

namespace EduFlow.Web.Models;

public sealed class CreateApplicationViewModel
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;

    [Display(Name = "Personal statement")]
    [StringLength(4000, ErrorMessage = "Personal statement must be 4,000 characters or fewer.")]
    public string? PersonalStatement { get; set; }
}

public sealed class EditDraftApplicationViewModel
{
    public long ApplicationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];

    [Display(Name = "Personal statement")]
    [StringLength(4000, ErrorMessage = "Personal statement must be 4,000 characters or fewer.")]
    public string? PersonalStatement { get; set; }
}

public sealed class ReviewApplicationViewModel
{
    public long ApplicationId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string? PersonalStatement { get; set; }
    public AdmissionStatus Status { get; set; }
    public byte[] RowVersion { get; set; } = [];

    [StringLength(500)]
    [Display(Name = "Public message")]
    public string? PublicMessage { get; set; }

    [StringLength(2000)]
    [Display(Name = "Internal note")]
    public string? InternalNote { get; set; }

    public AdmissionStatus ToStatus { get; set; }
}

public sealed record StudentApplicationListItemViewModel(
    long ApplicationId, string Reference, string CourseCode, string CourseName,
    DateTimeOffset CreatedUtc, DateTimeOffset? SubmittedUtc, AdmissionStatus Status);

public sealed record StudentApplicationDetailsViewModel(
    long ApplicationId, string Reference, string CourseCode, string CourseName, string? PersonalStatement,
    DateTimeOffset CreatedUtc, DateTimeOffset? SubmittedUtc, AdmissionStatus Status,
    IReadOnlyList<PublicApplicationHistoryDto> StatusHistory);

public sealed record ReviewApplicationListItemViewModel(
    long ApplicationId, string Reference, string CourseCode, string CourseName,
    DateTimeOffset CreatedUtc, DateTimeOffset? SubmittedUtc, AdmissionStatus Status);

public sealed record ReviewHistoryViewModel(
    AdmissionStatus? FromStatus, AdmissionStatus ToStatus, DateTimeOffset ChangedAtUtc,
    string? PublicMessage, string? InternalNote);

public sealed record ReviewApplicationDetailsViewModel(
    long ApplicationId, string Reference, string CourseCode, string CourseName, string? PersonalStatement,
    DateTimeOffset CreatedUtc, DateTimeOffset? SubmittedUtc, AdmissionStatus Status,
    byte[] RowVersion, IReadOnlyList<ApplicationHistoryDto> StatusHistory);
