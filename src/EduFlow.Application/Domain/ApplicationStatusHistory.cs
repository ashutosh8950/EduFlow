namespace EduFlow.Application.Domain;

public sealed class ApplicationStatusHistory
{
    public long HistoryId { get; set; }
    public long ApplicationId { get; set; }
    public string ChangedByUserId { get; set; } = string.Empty;
    public AdmissionStatus? FromStatus { get; set; }
    public AdmissionStatus ToStatus { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? PublicMessage { get; set; }
    public string? InternalNote { get; set; }
}