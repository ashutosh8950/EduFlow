using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Persistence;

public sealed class StudentProfileService(ApplicationDbContext dbContext) : IStudentProfileService
{
    public async Task<StudentProfileDto?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var profile = await dbContext.StudentProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        return new StudentProfileDto(
            user.FirstName,
            user.LastName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            profile?.DateOfBirth,
            profile?.AddressLine,
            profile?.City,
            profile?.Region,
            profile?.PostalCode);
    }

    public async Task<bool> UpdateAsync(
        string userId,
        UpdateStudentProfileInput input,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var profile = await dbContext.StudentProfiles
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (profile is null)
        {
            profile = new StudentProfile { UserId = userId };
            dbContext.StudentProfiles.Add(profile);
        }

        user.FirstName = input.FirstName.Trim();
        user.LastName = input.LastName.Trim();
        user.PhoneNumber = Normalize(input.PhoneNumber);
        profile.DateOfBirth = input.DateOfBirth;
        profile.AddressLine = Normalize(input.AddressLine);
        profile.City = Normalize(input.City);
        profile.Region = Normalize(input.Region);
        profile.PostalCode = Normalize(input.PostalCode);
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
