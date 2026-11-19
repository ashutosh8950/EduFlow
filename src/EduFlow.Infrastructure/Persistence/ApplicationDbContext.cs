using EduFlow.Application.Domain;
using EduFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<AdmissionApplication> AdmissionApplications => Set<AdmissionApplication>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistory => Set<ApplicationStatusHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.LastName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.IsActive).HasDefaultValue(true);
            entity.Property(user => user.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("EmailIndex")
                .HasFilter("[NormalizedEmail] IS NOT NULL");
        });

        builder.Entity<StudentProfile>(entity =>
        {
            entity.ToTable("StudentProfiles");
            entity.HasKey(profile => profile.UserId);
            entity.Property(profile => profile.UserId).HasMaxLength(450);
            entity.Property(profile => profile.AddressLine).HasMaxLength(200);
            entity.Property(profile => profile.City).HasMaxLength(100);
            entity.Property(profile => profile.Region).HasMaxLength(100);
            entity.Property(profile => profile.PostalCode).HasMaxLength(20);
            entity.Property(profile => profile.UpdatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<ApplicationUser>()
                .WithOne()
                .HasForeignKey<StudentProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Course>(entity =>
        {
            entity.ToTable("Courses", table => table.HasCheckConstraint(
                "CK_Courses_DurationMonths",
                "[DurationMonths] IS NULL OR [DurationMonths] > 0"));
            entity.HasKey(course => course.CourseId);
            entity.Property(course => course.Code).HasMaxLength(20).IsRequired();
            entity.Property(course => course.Name).HasMaxLength(150).IsRequired();
            entity.Property(course => course.Description).HasMaxLength(2000).IsRequired();
            entity.Property(course => course.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(course => course.Code).IsUnique();
            entity.HasIndex(course => course.IsActive);
        });

        builder.Entity<AdmissionApplication>(entity =>
        {
            entity.ToTable("AdmissionApplications", table => table.HasCheckConstraint(
                "CK_AdmissionApplications_Status",
                "[Status] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')"));
            entity.HasKey(application => application.ApplicationId);
            entity.Property(application => application.StudentUserId).HasMaxLength(450).IsRequired();
            entity.Property(application => application.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(application => application.PersonalStatement).HasMaxLength(4000);
            entity.Property(application => application.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(application => application.RowVersion).IsRowVersion();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(application => application.StudentUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(application => application.Course)
                .WithMany()
                .HasForeignKey(application => application.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(application => application.StatusHistory)
                .WithOne()
                .HasForeignKey(history => history.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(application => new { application.StudentUserId, application.CreatedUtc });
            entity.HasIndex(application => new { application.Status, application.CreatedUtc });
            entity.HasIndex(application => new { application.CourseId, application.Status });
            entity.HasIndex(application => new { application.StudentUserId, application.CourseId })
                .IsUnique()
                .HasDatabaseName("UX_AdmissionApplications_StudentUserId_CourseId");
        });

        builder.Entity<ApplicationStatusHistory>(entity =>
        {
            entity.ToTable("ApplicationStatusHistory", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApplicationStatusHistory_FromStatus",
                    "[FromStatus] IS NULL OR [FromStatus] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')");
                table.HasCheckConstraint(
                    "CK_ApplicationStatusHistory_ToStatus",
                    "[ToStatus] IN ('Draft', 'Submitted', 'UnderReview', 'Approved', 'Rejected', 'Withdrawn')");
            });
            entity.HasKey(history => history.HistoryId);
            entity.Property(history => history.ChangedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(history => history.ToStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(history => history.PublicMessage).HasMaxLength(500);
            entity.Property(history => history.InternalNote).HasMaxLength(2000);
            entity.Property(history => history.ChangedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(history => history.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(history => new { history.ApplicationId, history.ChangedAtUtc });
        });

        builder.Entity<IdentityRole>().HasData(
            new IdentityRole
            {
                Id = "00000000-0000-0000-0000-000000000001",
                Name = RoleNames.Student,
                NormalizedName = "STUDENT",
                ConcurrencyStamp = "00000000-0000-0000-0000-000000000001"
            },
            new IdentityRole
            {
                Id = "00000000-0000-0000-0000-000000000002",
                Name = RoleNames.AdmissionsOfficer,
                NormalizedName = "ADMISSIONSOFFICER",
                ConcurrencyStamp = "00000000-0000-0000-0000-000000000002"
            },
            new IdentityRole
            {
                Id = "00000000-0000-0000-0000-000000000003",
                Name = RoleNames.Administrator,
                NormalizedName = "ADMINISTRATOR",
                ConcurrencyStamp = "00000000-0000-0000-0000-000000000003"
            });
    }
}
