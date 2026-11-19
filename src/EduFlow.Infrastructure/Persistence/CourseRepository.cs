using EduFlow.Application.Domain;
using EduFlow.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Persistence;

public sealed class CourseRepository(ApplicationDbContext dbContext) : ICourseRepository
{
    public async Task<IReadOnlyList<Course>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.Courses.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(course => course.IsActive);
        }

        return await query.OrderBy(course => course.Name).ToListAsync(cancellationToken);
    }

    public Task<Course?> GetByIdAsync(int courseId, CancellationToken cancellationToken) =>
        dbContext.Courses.SingleOrDefaultAsync(course => course.CourseId == courseId, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string normalizedCode,
        int? excludedCourseId,
        CancellationToken cancellationToken) =>
        dbContext.Courses.AnyAsync(course =>
            course.Code.ToUpper() == normalizedCode &&
            (!excludedCourseId.HasValue || course.CourseId != excludedCourseId.Value),
            cancellationToken);

    public async Task<bool> SaveAsync(Course course, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            dbContext.Courses.Add(course);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            dbContext.Entry(course).State = isNew ? EntityState.Detached : EntityState.Modified;
            return false;
        }
    }

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}