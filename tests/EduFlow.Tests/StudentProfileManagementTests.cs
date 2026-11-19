using System.ComponentModel.DataAnnotations;
using EduFlow.Application.Domain;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Web.Controllers;
using EduFlow.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Tests;

public sealed class StudentProfileManagementTests
{
    [Fact]
    public async Task StudentCanRetrieveAndUpdateOwnProfile()
    {
        await using var db = CreateDb();
        var user = AddUser(db, "student-1", "Old", "Name");
        db.StudentProfiles.Add(new StudentProfile { UserId = user.Id, City = "Old City" });
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db);

        var before = await service.GetAsync(user.Id, CancellationToken.None);
        var updated = await service.UpdateAsync(user.Id, new(
            "New", "Name", "+91 9000000000", new DateOnly(2000, 1, 2),
            "12 Main Street", "New City", "Region", "12345"), CancellationToken.None);
        var after = await service.GetAsync(user.Id, CancellationToken.None);

        Assert.Equal("Old", before!.FirstName);
        Assert.True(updated);
        Assert.Equal("New", after!.FirstName);
        Assert.Equal("+91 9000000000", after.PhoneNumber);
        Assert.Equal(new DateOnly(2000, 1, 2), after.DateOfBirth);
        Assert.Equal("New City", after.City);
    }

    [Fact]
    public async Task ProfileUpdateIsScopedToTheRequestedAuthenticatedUser()
    {
        await using var db = CreateDb();
        var first = AddUser(db, "student-1", "First", "Student");
        var second = AddUser(db, "student-2", "Second", "Student");
        db.StudentProfiles.AddRange(
            new StudentProfile { UserId = first.Id, City = "First City" },
            new StudentProfile { UserId = second.Id, City = "Second City" });
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db);

        await service.UpdateAsync(second.Id, new(
            "Changed", "Student", null, null, null, "Changed City", null, null), CancellationToken.None);

        var firstProfile = await service.GetAsync(first.Id, CancellationToken.None);
        var secondProfile = await service.GetAsync(second.Id, CancellationToken.None);
        Assert.Equal("First", firstProfile!.FirstName);
        Assert.Equal("First City", firstProfile.City);
        Assert.Equal("Changed", secondProfile!.FirstName);
        Assert.Equal("Changed City", secondProfile.City);
    }

    [Fact]
    public void ProfileViewModelRejectsInvalidInput()
    {
        var model = new EditStudentProfileViewModel
        {
            FirstName = string.Empty,
            LastName = "Valid",
            AddressLine = new string('x', 201)
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(EditStudentProfileViewModel.FirstName)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(EditStudentProfileViewModel.AddressLine)));
    }

    [Fact]
    public void StudentControllerRequiresStudentAccessPolicy()
    {
        var authorize = typeof(StudentController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("StudentAccess", authorize.Policy);
    }

    [Fact]
    public void ProfileUpdateActionRequiresAntiforgery()
    {
        var action = typeof(StudentController)
            .GetMethods()
            .Single(method => method.Name == "EditProfile" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(EditStudentProfileViewModel)));

        Assert.NotNull(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    private static ApplicationUser AddUser(ApplicationDbContext db, string id, string firstName, string lastName)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.com",
            NormalizedUserName = $"{id.ToUpperInvariant()}@EXAMPLE.COM",
            Email = $"{id}@example.com",
            NormalizedEmail = $"{id.ToUpperInvariant()}@EXAMPLE.COM",
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };
        db.Users.Add(user);
        return user;
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
