namespace EduFlow.Application.Domain;

public sealed class AdmissionApplication
{
    public long ApplicationId { get; set; }
    public string StudentUserId { get; set; } = string.Empty;
    public int CourseId { get; set; }
    public AdmissionStatus Status { get; set; } = AdmissionStatus.Draft;
    public string? PersonalStatement { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Course? Course { get; set; }
    public ICollection<ApplicationStatusHistory> StatusHistory { get; set; } = new List<ApplicationStatusHistory>();
}