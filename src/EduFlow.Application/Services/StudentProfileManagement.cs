namespace EduFlow.Application.Services;

public sealed record StudentProfileDto(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    string? AddressLine,
    string? City,
    string? Region,
    string? PostalCode);

public sealed record UpdateStudentProfileInput(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    string? AddressLine,
    string? City,
    string? Region,
    string? PostalCode);

public interface IStudentProfileService
{
    Task<StudentProfileDto?> GetAsync(string userId, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(string userId, UpdateStudentProfileInput input, CancellationToken cancellationToken);
}
