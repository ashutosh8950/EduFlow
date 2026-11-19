namespace EduFlow.Application.Domain;

public sealed class StudentProfile
{
    public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}