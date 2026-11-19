using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Persistence;

public sealed class AdmissionApplicationRepository(ApplicationDbContext dbContext) : IAdmissionApplicationRepository
{
    public async Task<IReadOnlyList<AdmissionApplication>> GetForStudentAsync(string studentUserId, CancellationToken cancellationToken) =>
        await LoadApplications(dbContext.AdmissionApplications.AsNoTracking().Where(a => a.StudentUserId == studentUserId))
            .OrderByDescending(a => a.CreatedUtc).ToListAsync(cancellationToken);

    public Task<AdmissionApplication?> GetForStudentAsync(long applicationId, string studentUserId, CancellationToken cancellationToken) =>
        LoadApplications(dbContext.AdmissionApplications.AsNoTracking().Where(a => a.ApplicationId == applicationId && a.StudentUserId == studentUserId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<AdmissionApplication?> GetDraftForStudentAsync(long applicationId, string studentUserId, CancellationToken cancellationToken) =>
        LoadApplications(dbContext.AdmissionApplications.Where(a => a.ApplicationId == applicationId &&
            a.StudentUserId == studentUserId && a.Status == AdmissionStatus.Draft)).SingleOrDefaultAsync(cancellationToken);

    public Task<AdmissionApplication?> GetByIdAsync(long applicationId, CancellationToken cancellationToken) =>
        LoadApplications(dbContext.AdmissionApplications.Where(a => a.ApplicationId == applicationId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AdmissionApplication>> GetReviewQueueAsync(bool allStatuses, CancellationToken cancellationToken)
    {
        var query = dbContext.AdmissionApplications.AsNoTracking();
        if (!allStatuses)
        {
            query = query.Where(a => a.Status == AdmissionStatus.Submitted || a.Status == AdmissionStatus.UnderReview);
        }

        return await LoadApplications(query).OrderBy(a => a.CreatedUtc).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsForCourseAsync(string studentUserId, int courseId, CancellationToken cancellationToken) =>
        dbContext.AdmissionApplications.AnyAsync(a => a.StudentUserId == studentUserId && a.CourseId == courseId, cancellationToken);

    public Task<AdmissionApplication?> GetDraftForCourseAsync(string studentUserId, int courseId, CancellationToken cancellationToken) =>
        dbContext.AdmissionApplications.Include(a => a.Course).Include(a => a.StatusHistory)
            .SingleOrDefaultAsync(a => a.StudentUserId == studentUserId && a.CourseId == courseId && a.Status == AdmissionStatus.Draft, cancellationToken);

    public Task<Course?> GetActiveCourseAsync(int courseId, CancellationToken cancellationToken) =>
        dbContext.Courses.SingleOrDefaultAsync(c => c.CourseId == courseId && c.IsActive, cancellationToken);

    public async Task<bool> AddAsync(AdmissionApplication application, CancellationToken cancellationToken)
    {
        if (application.Course is not null)
        {
            dbContext.Entry(application.Course).State = EntityState.Unchanged;
        }
        dbContext.AdmissionApplications.Add(application);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> UpdateAsync(AdmissionApplication application, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        dbContext.Entry(application).Property(a => a.RowVersion).OriginalValue = originalRowVersion;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> SaveReviewAsync(AdmissionApplication application, ApplicationStatusHistory history, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        dbContext.Entry(application).Property(a => a.RowVersion).OriginalValue = originalRowVersion;
        dbContext.ApplicationStatusHistory.Add(history);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private static IQueryable<AdmissionApplication> LoadApplications(IQueryable<AdmissionApplication> query) =>
        query.Include(a => a.Course).Include(a => a.StatusHistory);

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } ||
        exception.InnerException?.InnerException is SqlException { Number: 2601 or 2627 };
}
