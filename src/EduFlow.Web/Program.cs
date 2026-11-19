using EduFlow.Application.Domain;
using EduFlow.Infrastructure;
using EduFlow.Web.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AdministratorInitializer>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdministratorOnly", policy =>
        policy.RequireRole(RoleNames.Administrator));
    options.AddPolicy("CanManageCourses", policy =>
        policy.RequireRole(RoleNames.Administrator));
    options.AddPolicy("CanReviewApplications", policy =>
        policy.RequireRole(RoleNames.Administrator, RoleNames.AdmissionsOfficer));
    options.AddPolicy("StudentAccess", policy =>
        policy.RequireRole(RoleNames.Student));
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var administratorInitializer = scope.ServiceProvider.GetRequiredService<AdministratorInitializer>();
    await administratorInitializer.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program;
