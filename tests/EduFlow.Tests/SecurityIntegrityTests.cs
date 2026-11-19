using System.Reflection;
using EduFlow.Application.Domain;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.Tests;

public class SecurityIntegrityTests
{
    [Fact]
    public async Task ActiveUserPassesPreSignInCheck()
    {
        await using var provider = CreateServiceProvider();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();

        var user = new ApplicationUser { IsActive = true, UserName = "active", Email = "active@example.com" };
        var result = await InvokePreSignInCheckAsync(signInManager, user);

        Assert.Null(result);
    }

    [Fact]
    public async Task InactiveUserIsRejectedByPreSignInCheck()
    {
        await using var provider = CreateServiceProvider();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();

        var user = new ApplicationUser { IsActive = false, UserName = "inactive", Email = "inactive@example.com" };
        var result = await InvokePreSignInCheckAsync(signInManager, user);

        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        Assert.Equal(SignInResult.NotAllowed, result);
    }

    [Fact]
    public async Task DuplicateEmailRegistrationIsRejected()
    {
        await using var provider = CreateServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var first = new ApplicationUser
        {
            UserName = "first",
            Email = "student@example.com",
            NormalizedEmail = "STUDENT@EXAMPLE.COM"
        };
        var duplicate = new ApplicationUser
        {
            UserName = "second",
            Email = "STUDENT@EXAMPLE.COM",
            NormalizedEmail = "STUDENT@EXAMPLE.COM"
        };

        var firstResult = await userManager.CreateAsync(first);
        var duplicateResult = await userManager.CreateAsync(duplicate);

        Assert.True(firstResult.Succeeded);
        Assert.False(duplicateResult.Succeeded);
        Assert.Contains(duplicateResult.Errors, error => error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EFModelRequiresUniqueNormalizedEmail()
    {
        using var context = CreateDbContext();
        var userEntity = context.Model.FindEntityType(typeof(ApplicationUser));

        Assert.NotNull(userEntity);
        var emailIndex = userEntity!.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(ApplicationUser.NormalizedEmail)));

        Assert.True(emailIndex.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", emailIndex.GetFilter());
    }

    [Fact]
    public void StatusHistoryConstraintsAllowNullAndOnlyKnownStatuses()
    {
        using var context = CreateDbContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var historyEntity = model.FindEntityType(typeof(ApplicationStatusHistory));

        Assert.NotNull(historyEntity);
        var constraints = historyEntity!.GetCheckConstraints().ToDictionary(
            constraint => constraint.Name!,
            constraint => constraint.Sql!);

        Assert.Equal(
            "[FromStatus] IS NULL OR [FromStatus] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')",
            constraints["CK_ApplicationStatusHistory_FromStatus"]);
        Assert.Equal(
            "[ToStatus] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')",
            constraints["CK_ApplicationStatusHistory_ToStatus"]);
        Assert.Contains("[FromStatus] IS NULL", constraints["CK_ApplicationStatusHistory_FromStatus"]);
        Assert.DoesNotContain("Unknown", constraints["CK_ApplicationStatusHistory_FromStatus"]);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(local);Database=EduFlowModelOnly;Trusted_Connection=True")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddAuthentication();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer("Server=(local);Database=EduFlowTests;Trusted_Connection=True"));
        services.AddSingleton<IUserStore<ApplicationUser>, TestUserStore>();
        services.AddSingleton<IRoleStore<IdentityRole>, TestRoleStore>();
        services.AddSingleton<IUserEmailStore<ApplicationUser>>(provider =>
            (IUserEmailStore<ApplicationUser>)provider.GetRequiredService<IUserStore<ApplicationUser>>());
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddSignInManager<ActiveAccountSignInManager>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private static async Task<SignInResult?> InvokePreSignInCheckAsync(
        SignInManager<ApplicationUser> signInManager,
        ApplicationUser user)
    {
        var method = typeof(ActiveAccountSignInManager).GetMethod(
            "PreSignInCheck",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task<SignInResult?>)method!.Invoke(signInManager, new object[] { user })!;
        return await task;
    }

    private sealed class TestRoleStore : IRoleStore<IdentityRole>
    {
        public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
            => Task.FromResult<IdentityRole?>(null);

        public Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
            => Task.FromResult<IdentityRole?>(null);

        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(role.NormalizedName);

        public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(role.Id);

        public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(role.Name);

        public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }
    }

    private sealed class TestUserStore : IUserEmailStore<ApplicationUser>
    {
        private readonly Dictionary<string, ApplicationUser> usersById = new();
        private readonly Dictionary<string, ApplicationUser> usersByName = new();

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.Id = string.IsNullOrEmpty(user.Id) ? Guid.NewGuid().ToString() : user.Id;
            usersById[user.Id] = user;
            usersByName[user.NormalizedUserName!] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            usersById[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            usersById.Remove(user.Id);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            usersById.TryGetValue(userId, out var user);
            return Task.FromResult(user);
        }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            usersByName.TryGetValue(normalizedUserName, out var user);
            return Task.FromResult(user);
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.NormalizedUserName);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Email);

        public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.EmailConfirmed);

        public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.NormalizedEmail);

        public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
        {
            user.Email = email;
            return Task.CompletedTask;
        }

        public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }

        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            var user = usersById.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.NormalizedEmail, normalizedEmail, StringComparison.Ordinal));
            return Task.FromResult(user);
        }

        public void Dispose()
        {
        }
    }
}
