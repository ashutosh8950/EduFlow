using EduFlow.Application.Domain;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Web.Identity;

public sealed class AdministratorInitializer(
    IConfiguration configuration,
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<AdministratorInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAccountAsync("AdminBootstrap", RoleNames.Administrator, "EduFlow", "Administrator", cancellationToken);
        await InitializeAccountAsync("AdmissionsOfficerBootstrap", RoleNames.AdmissionsOfficer, "EduFlow", "Admissions Officer", cancellationToken);
    }

    private async Task InitializeAccountAsync(string section, string roleName, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var email = configuration[$"{section}:Email"]?.Trim();
        var password = configuration[$"{section}:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("{Role} initialization skipped because bootstrap credentials are not configured.", roleName);
            return;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!roleResult.Succeeded && !await roleManager.RoleExistsAsync(roleName))
            {
                throw new InvalidOperationException($"{roleName} role initialization failed.");
            }
        }

        var administrator = await userManager.FindByEmailAsync(email);
        var isNewAdministrator = administrator is null;
        if (administrator is null)
        {
            administrator = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(administrator, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException("Administrator account initialization failed.");
            }
        }
        else if (!await userManager.IsInRoleAsync(administrator, roleName))
        {
            throw new InvalidOperationException($"Bootstrap email belongs to an account without the {roleName} role.");
        }

        if (isNewAdministrator)
        {
            var assignmentResult = await userManager.AddToRoleAsync(administrator, roleName);
            if (!assignmentResult.Succeeded)
            {
                throw new InvalidOperationException($"{roleName} role assignment failed.");
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
