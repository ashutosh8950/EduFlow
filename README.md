# EduFlow

EduFlow is a modular-monolith ASP.NET Core MVC application for student admission and education management. It provides role-based workflows for students, Admissions Officers, and Administrators, with SQL Server persistence and ASP.NET Core Identity authentication.

EduFlow is an academic/project implementation. It is not presented as production-ready software.

## Objectives

EduFlow is intended to provide a secure, traceable admission workflow that allows an institution to:

- Publish and manage courses.
- Allow students to create, save, resume, and submit admission applications.
- Keep students limited to their own application data.
- Give authorized Admissions Officers a controlled review queue.
- Record status transitions and reviewer messages.
- Preserve admission data in SQL Server with database-level duplicate protection.

## Implemented Features

### Students

- Register as a Student through `/Account/Register`.
- Sign in and sign out through `/Account/Login` and POST `/Account/Logout`.
- View the student profile at `/Student/Profile`.
- Browse active courses at `/Student/Courses`.
- View an active course at `/Student/CourseDetails/{id}`.
- Create an application at `/Student/CreateApplication?courseId={courseId}`.
- Save an application as Draft.
- View applications at `/Student/MyApplications`.
- Open application details at `/Student/ApplicationDetails/{id}`.
- Edit/resume a Draft at `/Student/EditApplication/{id}`.
- Save changes to the existing Draft.
- Submit the existing Draft without creating a second application.
- View public status history and public admissions messages.

The current implementation provides profile viewing only. A student profile edit workflow is not currently implemented.

### Admissions Officers

- Access the review queue at `/Admissions`.
- Open application details at `/Admissions/Details/{id}`.
- Review an application at `/Admissions/Review/{id}`.
- Move `Submitted` applications to `UnderReview`.
- Approve or reject applications in `UnderReview`.
- Add optional public messages.
- Add optional internal notes.

Admissions Officers are initialized through local .NET User Secrets configuration. There is no public Admissions Officer registration endpoint.

### Administrators

- Access the administration page at `/Administration`.
- View all applications through `/Admissions`.
- Review applications using the existing review policy.
- Manage courses through `/Courses`.
- Create, edit, view, activate, and deactivate courses.

The current `/Administration` landing page is intentionally minimal. Course management and application review are exposed through their dedicated routes.

## Application Workflow

The implemented admission status flow is:

```text
Draft -> Submitted -> UnderReview -> Approved
                                  \-> Rejected
```

Rules enforced by the application service:

- Only active courses accept new applications.
- A student can have at most one application for a course.
- A Draft can be edited and submitted.
- Only Draft applications can be edited by students.
- Only `Submitted` applications can move to `UnderReview`.
- Only `UnderReview` applications can move to `Approved` or `Rejected`.
- Each transition is recorded in `ApplicationStatusHistory`.
- Review operations use UTC timestamps, transactions, and RowVersion concurrency checks.

The domain enum and database constraint also contain `Withdrawn`, but no student withdrawal workflow is exposed by the current UI.

## Technology Stack

| Technology | Version or detail |
|---|---|
| .NET | Target framework `net10.0` |
| ASP.NET Core MVC | .NET 10 shared framework |
| Entity Framework Core | `10.0.11` |
| SQL Server provider | `Microsoft.EntityFrameworkCore.SqlServer 10.0.11` |
| SQL Server client | `Microsoft.Data.SqlClient 6.1.8` |
| ASP.NET Core Identity | `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.11` |
| OpenAPI | `Microsoft.AspNetCore.OpenApi 10.0.11` |
| UI | Razor Views with bundled Bootstrap and jQuery assets |
| Tests | xUnit `2.9.3`, Microsoft.NET.Test.Sdk `17.14.1`, EF Core InMemory `10.0.11` |

The repository pins the local `dotnet-ef` tool to `10.0.11` in `dotnet-tools.json`.

## Architecture

EduFlow uses a modular monolith with separate application, infrastructure, web, and test projects.

```text
EduFlow.sln
├── src
│   ├── EduFlow.Application
│   │   ├── Domain
│   │   └── Services
│   ├── EduFlow.Infrastructure
│   │   ├── Identity
│   │   └── Persistence
│   │       └── Migrations
│   └── EduFlow.Web
│       ├── Controllers
│       ├── Identity
│       ├── Models
│       ├── Views
│       └── wwwroot
└── tests
    └── EduFlow.Tests
```

Responsibilities are separated as follows:

- `EduFlow.Application`: domain entities, status rules, service contracts, DTOs, and application services.
- `EduFlow.Infrastructure`: EF Core persistence, repositories, Identity integration, SQL Server configuration, and migrations.
- `EduFlow.Web`: MVC controllers, view models, Razor views, authorization policies, and application bootstrap.
- `EduFlow.Tests`: xUnit tests for authentication, authorization, course management, admission workflow, and integrity rules.

## Database Design

The EF Core context is `ApplicationDbContext` in `EduFlow.Infrastructure.Persistence`.

Main entities include:

| Entity/table | Purpose |
|---|---|
| `AspNetUsers` | ASP.NET Core Identity users and EduFlow user fields |
| `AspNetRoles` | Student, AdmissionsOfficer, and Administrator roles |
| `StudentProfiles` | Student profile records linked to Identity users |
| `Courses` | Course catalog and active/inactive state |
| `AdmissionApplications` | Student applications, statements, statuses, timestamps, and RowVersion |
| `ApplicationStatusHistory` | Status transitions, public messages, and internal reviewer notes |

Admission applications have a unique SQL Server index named `UX_AdmissionApplications_StudentUserId_CourseId` on `(StudentUserId, CourseId)`. This protects the one-application-per-student-per-course rule even when concurrent requests reach the database.

## Authentication, Authorization, and Security

The application uses ASP.NET Core Identity with cookie authentication.

Configured policies in `Program.cs` include:

- `StudentAccess`: Student role only.
- `CanReviewApplications`: AdmissionsOfficer or Administrator.
- `AdministratorOnly`: Administrator role only.
- `CanManageCourses`: Administrator role only.

Important protections include:

- Student identity is derived from the authenticated claims principal.
- Student application queries are scoped by authenticated student ID.
- Students cannot access another student's application details or drafts.
- Students cannot access admissions review actions.
- Review transitions are validated server-side.
- State-changing MVC actions use antiforgery validation.
- Internal notes are kept out of student DTOs, student views, and student-facing history projections.
- Account sign-in checks the active-account state.
- Identity cookies are HTTP-only, Secure, SameSite=Lax, and configured with an eight-hour lifetime.
- SQL Server RowVersion is used for review and draft-update concurrency handling.
- Review status changes are saved inside a database transaction.
- Duplicate applications are protected by both service validation and a SQL Server unique index.

## Prerequisites

Install or enable:

1. .NET 10 SDK.
2. SQL Server Express with the instance `localhost\SQLEXPRESS`.
3. Windows Authentication access to the `EduFlow` database.
4. PowerShell or another terminal capable of running the .NET CLI.

The repository includes the local EF CLI tool manifest. Restore it before using `dotnet ef`:

```powershell
dotnet tool restore
```

## SQL Server Configuration

The Development configuration points to:

```text
Server=localhost\SQLEXPRESS;Database=EduFlow;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

This is a local Windows Authentication development configuration. Do not copy production credentials or connection strings into the repository.

Restore packages:

```powershell
dotnet restore EduFlow.sln
```

List migrations without changing the database:

```powershell
dotnet ef migrations list `
  --project src/EduFlow.Infrastructure/EduFlow.Infrastructure.csproj `
  --startup-project src/EduFlow.Web/EduFlow.Web.csproj
```

Apply migrations to the configured local database:

```powershell
dotnet ef database update `
  --project src/EduFlow.Infrastructure/EduFlow.Infrastructure.csproj `
  --startup-project src/EduFlow.Web/EduFlow.Web.csproj
```

The repository currently contains these migrations:

```text
20261118094338_InitialCreate
20261118103716_SecurityAndStatusIntegrity
20261118161704_AdmissionApplicationUniqueStudentCourseIndex
```

Review the migration operations before applying migrations to any shared or existing database.

## Bootstrap Accounts with User Secrets

Bootstrap credentials are intentionally not stored in source control. The Web project has a User Secrets ID configured in `EduFlow.Web.csproj`.

### Administrator

Set local Administrator bootstrap values using placeholders of your own:

```powershell
dotnet user-secrets --project src/EduFlow.Web set "AdminBootstrap:Email" "admin@example.test"
dotnet user-secrets --project src/EduFlow.Web set "AdminBootstrap:Password" "<strong-local-password>"
```

### Admissions Officer

Set local Admissions Officer bootstrap values:

```powershell
dotnet user-secrets --project src/EduFlow.Web set "AdmissionsOfficerBootstrap:Email" "officer@example.test"
dotnet user-secrets --project src/EduFlow.Web set "AdmissionsOfficerBootstrap:Password" "<strong-local-password>"
```

The bootstrap initializer is idempotent. It creates a new account only when the configured email is not present and assigns only its intended role. It does not promote an existing account unexpectedly. Do not commit User Secrets or include real credentials in documentation, screenshots, issues, or pull requests.

## Run Locally

From the repository root:

```powershell
dotnet restore EduFlow.sln
dotnet run --project src/EduFlow.Web/EduFlow.Web.csproj --launch-profile https
```

The configured HTTPS profile uses:

```text
https://localhost:7274
```

It also exposes the HTTP development endpoint on `http://localhost:5113`, which redirects to HTTPS through the application middleware.

If the local ASP.NET Core development certificate is not trusted, run this Windows command and restart the browser:

```powershell
dotnet dev-certs https --trust
```

## Run Tests

Build the solution:

```powershell
dotnet build EduFlow.sln
```

Run all tests:

```powershell
dotnet test EduFlow.sln
```

The current test project uses EF Core InMemory for many application-level tests. Those tests should not be interpreted as a replacement for SQL Server integration or browser end-to-end testing.

Optional package vulnerability audit:

```powershell
dotnet list EduFlow.sln package --vulnerable --include-transitive
```

## API and OpenAPI

The versioned course API is implemented by `CourseApiController`:

| Method | Endpoint | Authorization | Purpose |
|---|---|---|---|
| `GET` | `/api/v1/courses` | Student role | List active courses |
| `GET` | `/api/v1/courses/{id}` | Student role | Get an active course |

The Web project calls `AddOpenApi()` and maps OpenAPI in Development with `MapOpenApi()`. With the standard ASP.NET Core document naming, the development document is available at:

```text
https://localhost:7274/openapi/v1.json
```

The API and OpenAPI document require the application to be running and the configured authentication/authorization rules to be satisfied where applicable.

## Current Limitations and Future Improvements

The following items are outside the current implemented scope or remain candidates for future work:

- Student profile editing.
- A richer Administrator dashboard and statistics view.
- Browser-based end-to-end test automation against SQL Server.
- Email notifications.
- File uploads.
- Payments.
- Deployment and hosting automation.
- Production secret management and operational monitoring.
- Expanded API coverage beyond the course endpoints.
- More polished, EduFlow-specific homepage content and visual branding.

The application should be evaluated and hardened further before any production deployment. In particular, production environments need appropriate secrets management, HTTPS certificate management, logging policy, database backup/recovery planning, monitoring, and security review.

## Project Structure Overview

```text
src/
├── EduFlow.Application
│   ├── Domain/       AdmissionApplication, Course, StudentProfile, status and role definitions
│   └── Services/     Admission and course business services/contracts
├── EduFlow.Infrastructure
│   ├── Identity/     ApplicationUser and active-account sign-in behavior
│   └── Persistence/  ApplicationDbContext, repositories, SQL Server setup, migrations
├── EduFlow.Web
│   ├── Controllers/  Account, Student, Admissions, Administration, Courses, Home
│   ├── Models/       MVC input and output view models
│   ├── Views/        Razor UI for account, students, admissions, administration, and courses
│   └── Identity/     Administrator and Admissions Officer bootstrap initialization
└── tests/
    └── EduFlow.Tests  Authentication, authorization, course, admission workflow, and integrity tests
```

## Author

[Ashutosh Gupta](https://github.com/ashutosh8950)
