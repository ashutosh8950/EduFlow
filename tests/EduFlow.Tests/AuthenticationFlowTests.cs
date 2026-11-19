using System.Security.Claims;
using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using EduFlow.Infrastructure.Identity;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Web.Controllers;
using EduFlow.Web.Identity;
using EduFlow.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace EduFlow.Tests;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public async Task StudentRegistrationCreatesUserRoleAndProfile()
    {
        await using var services = CreateServices();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var controller = CreateAccountController(services);

        var result = await controller.Register(ValidRegistration(), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        var user = await services.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("student@example.com");
        Assert.NotNull(user);
        Assert.True(await services.GetRequiredService<UserManager<ApplicationUser>>().IsInRoleAsync(user!, RoleNames.Student));
        Assert.Single(await dbContext.StudentProfiles.ToListAsync());
    }

    [Fact]
    public async Task DuplicateEmailRegistrationReturnsValidationError()
    {
        await using var services = CreateServices();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        await userManager.CreateAsync(NewUser("student@example.com"), "Strong-Pass123!");
        var controller = CreateAccountController(services);

        var result = await controller.Register(ValidRegistration(), CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(RegisterViewModel.Email)]!.Errors,
            error => error.ErrorMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await services.GetRequiredService<ApplicationDbContext>().StudentProfiles.ToListAsync());
    }

    [Fact]
    public async Task SuccessfulLoginRedirectsToLocalReturnUrl()
    {
        await using var services = CreateServices();
        services.GetRequiredService<TestSignInManager>().Result = SignInResult.Success;
        var controller = CreateAccountController(services);

        var result = await controller.Login(new LoginViewModel
        {
            Email = "student@example.com",
            Password = "Strong-Pass123!",
            ReturnUrl = "/Student/Profile"
        });

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Student/Profile", redirect.Url);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("Inactive")]
    public async Task LoginFailuresDoNotAuthenticate(string failure)
    {
        await using var services = CreateServices();
        services.GetRequiredService<TestSignInManager>().Result = failure == "Inactive"
            ? SignInResult.NotAllowed
            : SignInResult.Failed;
        var controller = CreateAccountController(services);

        var result = await controller.Login(new LoginViewModel
        {
            Email = "student@example.com",
            Password = "incorrect-password"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage == "Invalid email or password.");
    }

    [Fact]
    public async Task LogoutSignsOut()
    {
        await using var services = CreateServices();
        var signInManager = services.GetRequiredService<TestSignInManager>();
        var result = await CreateAccountController(services).Logout();

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, signInManager.SignOutCalls);
    }

    [Fact]
    public async Task StudentCannotAccessAdministratorEndpoint()
    {
        var services = CreateAuthorizationServices();
        using var provider = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Student)], "test"));
        var authorize = typeof(AdministrationController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();

        var result = await provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, "AdministratorOnly");

        Assert.Equal("AdministratorOnly", authorize.Policy);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StudentProfileIsScopedToAuthenticatedUser()
    {
        await using var services = CreateServices();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var firstStudent = NewUser("first@example.com");
        firstStudent.FirstName = "First";
        var secondStudent = NewUser("second@example.com");
        secondStudent.FirstName = "Second";
        Assert.True((await userManager.CreateAsync(firstStudent)).Succeeded);
        Assert.True((await userManager.CreateAsync(secondStudent)).Succeeded);
        dbContext.StudentProfiles.AddRange(
            new StudentProfile { UserId = firstStudent.Id },
            new StudentProfile { UserId = secondStudent.Id });
        await dbContext.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, firstStudent.Id)], "test"))
        };
        var controller = new StudentController(
            dbContext,
            userManager,
            new CourseService(new CourseRepository(dbContext)),
            new AdmissionApplicationService(new AdmissionApplicationRepository(dbContext)))
        {
            ControllerContext = new ControllerContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor())),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        var result = Assert.IsType<ViewResult>(await controller.Profile(CancellationToken.None));

        Assert.Equal("First", Assert.IsType<StudentProfileViewModel>(result.Model).FirstName);
    }

    [Fact]
    public async Task AdministratorCanAccessAdministratorEndpoint()
    {
        var services = CreateAuthorizationServices();
        using var provider = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Administrator)], "test"));

        var result = await provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, "AdministratorOnly");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AdministratorInitializationSkipsMissingCredentials()
    {
        await using var services = CreateServices();
        var initializer = CreateInitializer(services, new Dictionary<string, string?>());

        await initializer.InitializeAsync();

        Assert.Empty(services.GetRequiredService<TestUserStore>().Users);
    }

    [Fact]
    public async Task AdministratorInitializationCreatesAndReusesAccount()
    {
        await using var services = CreateServices();
        var initializer = CreateInitializer(services, new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = "admin@example.com",
            ["AdminBootstrap:Password"] = "Strong-Admin123!"
        });

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await userManager.FindByEmailAsync("admin@example.com");
        Assert.NotNull(administrator);
        Assert.Single(services.GetRequiredService<TestUserStore>().Users);
        Assert.True(await userManager.IsInRoleAsync(administrator!, RoleNames.Administrator));
    }

    [Fact]
    public async Task AdministratorInitializationDoesNotPromoteExistingStudent()
    {
        await using var services = CreateServices();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var student = NewUser("admin@example.com");
        Assert.True((await userManager.CreateAsync(student, "Strong-Student123!")).Succeeded);
        await userManager.AddToRoleAsync(student, RoleNames.Student);
        var initializer = CreateInitializer(services, new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = "admin@example.com",
            ["AdminBootstrap:Password"] = "Strong-Admin123!"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync());

        Assert.False(await userManager.IsInRoleAsync(student, RoleNames.Administrator));
    }

    [Fact]
    public async Task InactiveAccountIsRejectedBySignInManager()
    {
        await using var services = CreateServices<ActiveAccountSignInManager>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = NewUser("inactive@example.com");
        user.IsActive = false;
        var created = await userManager.CreateAsync(user, "Strong-Pass123!");
        Assert.True(created.Succeeded);

        var result = await services.GetRequiredService<SignInManager<ApplicationUser>>()
            .CheckPasswordSignInAsync(user, "Strong-Pass123!", lockoutOnFailure: false);

        Assert.Equal(SignInResult.NotAllowed, result);
    }

    private static RegisterViewModel ValidRegistration() => new()
    {
        FirstName = "Student",
        LastName = "Example",
        Email = "student@example.com",
        Password = "Strong-Pass123!",
        ConfirmPassword = "Strong-Pass123!"
    };

    private static ApplicationUser NewUser(string email) => new()
    {
        FirstName = "Existing",
        LastName = "User",
        UserName = email,
        Email = email,
        IsActive = true
    };

    private static AccountController CreateAccountController(IServiceProvider services)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        var controller = new AccountController(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<SignInManager<ApplicationUser>>(),
            services.GetRequiredService<ApplicationDbContext>(),
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
        return controller;
    }

    private static AdministratorInitializer CreateInitializer(
        IServiceProvider services,
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AdministratorInitializer(
            configuration,
            services.GetRequiredService<ApplicationDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole>>(),
            NullLogger<AdministratorInitializer>.Instance);
    }

    private static ServiceProvider CreateServices<TSignInManager>()
        where TSignInManager : SignInManager<ApplicationUser>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<TestUserStore>();
        services.AddSingleton<IUserStore<ApplicationUser>>(provider => provider.GetRequiredService<TestUserStore>());
        services.AddSingleton<IRoleStore<IdentityRole>, TestRoleStore>();
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddSignInManager<TSignInManager>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateServices() => CreateServices<TestSignInManager>();

    private static ServiceCollection CreateAuthorizationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => options.AddPolicy("AdministratorOnly", policy =>
            policy.RequireRole(RoleNames.Administrator)));
        return services;
    }

    private sealed class TestSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        public SignInResult Result { get; set; } = SignInResult.Failed;
        public int SignOutCalls { get; private set; }

        public override Task<SignInResult> PasswordSignInAsync(
            string userName,
            string password,
            bool isPersistent,
            bool lockoutOnFailure) => Task.FromResult(Result);

        public override Task SignOutAsync()
        {
            SignOutCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestUserStore : IUserEmailStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>,
        IUserRoleStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
    {
        private readonly Dictionary<string, ApplicationUser> users = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> passwords = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> rolesByUser = new(StringComparer.Ordinal);
        private static readonly string[] AvailableRoles = [RoleNames.Student, RoleNames.AdmissionsOfficer, RoleNames.Administrator];

        public IReadOnlyCollection<ApplicationUser> Users => users.Values;

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.Id = string.IsNullOrEmpty(user.Id) ? Guid.NewGuid().ToString() : user.Id;
            users[user.Id] = user;
            rolesByUser[user.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            users[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            users.Remove(user.Id);
            passwords.Remove(user.Id);
            rolesByUser.Remove(user.Id);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult(users.GetValueOrDefault(userId));

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult(users.Values.FirstOrDefault(user => user.NormalizedUserName == normalizedUserName));

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
        public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
        public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
        public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken) { user.Email = email; return Task.CompletedTask; }
        public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
        public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
        public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }
        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
            => Task.FromResult(users.Values.FirstOrDefault(user => user.NormalizedEmail == normalizedEmail));
        public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken) { passwords[user.Id] = passwordHash ?? string.Empty; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(passwords.GetValueOrDefault(user.Id));
        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(passwords.ContainsKey(user.Id));

        public Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
        {
            rolesByUser[user.Id].Add(normalizedRoleName);
            return Task.CompletedTask;
        }

        public Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
        {
            rolesByUser[user.Id].Remove(normalizedRoleName);
            return Task.CompletedTask;
        }

        public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult<IList<string>>(AvailableRoles.Where(role => rolesByUser[user.Id].Contains(role.ToUpperInvariant())).ToList());

        public Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
            => Task.FromResult(rolesByUser[user.Id].Contains(normalizedRoleName));

        public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
            => Task.FromResult<IList<ApplicationUser>>(users.Values.Where(user => rolesByUser[user.Id].Contains(normalizedRoleName)).ToList());

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);
        public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) { user.AccessFailedCount++; return Task.FromResult(user.AccessFailedCount); }
        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) { user.AccessFailedCount = 0; return Task.CompletedTask; }
        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);
        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);
        public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken) { user.LockoutEnabled = enabled; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class TestRoleStore : IRoleStore<IdentityRole>
    {
        private readonly Dictionary<string, IdentityRole> roles = AvailableRoles
            .ToDictionary(name => name.ToUpperInvariant(), name => new IdentityRole(name), StringComparer.Ordinal);

        private static readonly string[] AvailableRoles = [RoleNames.Student, RoleNames.AdmissionsOfficer, RoleNames.Administrator];

        public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
        {
            role.Id = string.IsNullOrEmpty(role.Id) ? Guid.NewGuid().ToString() : role.Id;
            roles[role.NormalizedName!] = role;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken) { roles[role.NormalizedName!] = role; return Task.FromResult(IdentityResult.Success); }
        public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken) { roles.Remove(role.NormalizedName!); return Task.FromResult(IdentityResult.Success); }
        public Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) => Task.FromResult(roles.Values.FirstOrDefault(role => role.Id == roleId));
        public Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult(roles.GetValueOrDefault(normalizedRoleName));
        public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);
        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);
        public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
        public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken) { role.NormalizedName = normalizedName; return Task.CompletedTask; }
        public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken) { role.Name = roleName; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
