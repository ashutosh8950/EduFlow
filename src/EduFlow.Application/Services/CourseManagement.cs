using EduFlow.Application.Domain;

namespace EduFlow.Application.Services;

public sealed record CourseDto(
    int CourseId,
    string Code,
    string Name,
    string Description,
    int? DurationMonths,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc);

public sealed record CourseInput(
    string? Code,
    string? Name,
    string? Description,
    int? DurationMonths,
    bool IsActive);

public sealed record CourseOperationResult(CourseDto? Course, string? Error, string? ErrorField)
{
    public bool Succeeded => Course is not null && Error is null;
}

public interface ICourseService
{
    Task<IReadOnlyList<CourseDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CourseDto>> GetActiveAsync(CancellationToken cancellationToken);
    Task<CourseDto?> GetByIdAsync(int courseId, CancellationToken cancellationToken);
    Task<CourseDto?> GetActiveByIdAsync(int courseId, CancellationToken cancellationToken);
    Task<CourseOperationResult> CreateAsync(CourseInput input, CancellationToken cancellationToken);
    Task<CourseOperationResult> UpdateAsync(int courseId, CourseInput input, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(int courseId, bool isActive, CancellationToken cancellationToken);
}

public interface ICourseRepository
{
    Task<IReadOnlyList<Course>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken);
    Task<Course?> GetByIdAsync(int courseId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string normalizedCode, int? excludedCourseId, CancellationToken cancellationToken);
    Task<bool> SaveAsync(Course course, bool isNew, CancellationToken cancellationToken);
}

public sealed class CourseService(ICourseRepository repository) : ICourseService
{
    public async Task<IReadOnlyList<CourseDto>> GetAllAsync(CancellationToken cancellationToken) =>
        (await repository.GetAllAsync(activeOnly: false, cancellationToken)).Select(ToDto).ToArray();

    public async Task<IReadOnlyList<CourseDto>> GetActiveAsync(CancellationToken cancellationToken) =>
        (await repository.GetAllAsync(activeOnly: true, cancellationToken)).Select(ToDto).ToArray();

    public async Task<CourseDto?> GetByIdAsync(int courseId, CancellationToken cancellationToken)
    {
        var course = await repository.GetByIdAsync(courseId, cancellationToken);
        return course is null ? null : ToDto(course);
    }

    public async Task<CourseDto?> GetActiveByIdAsync(int courseId, CancellationToken cancellationToken)
    {
        var course = await repository.GetByIdAsync(courseId, cancellationToken);
        return course is null || !course.IsActive ? null : ToDto(course);
    }

    public async Task<CourseOperationResult> CreateAsync(CourseInput input, CancellationToken cancellationToken)
    {
        var validationError = Validate(input);
        if (validationError is not null)
        {
            return validationError;
        }

        var code = input.Code!.Trim().ToUpperInvariant();
        if (await repository.CodeExistsAsync(code, null, cancellationToken))
        {
            return DuplicateCode();
        }

        var course = new Course
        {
            Code = code,
            Name = input.Name!.Trim(),
            Description = input.Description!.Trim(),
            DurationMonths = (short)input.DurationMonths!.Value,
            IsActive = input.IsActive
        };

        if (!await repository.SaveAsync(course, isNew: true, cancellationToken))
        {
            return DuplicateCode();
        }

        return new CourseOperationResult(ToDto(course), null, null);
    }

    public async Task<CourseOperationResult> UpdateAsync(
        int courseId,
        CourseInput input,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(input);
        if (validationError is not null)
        {
            return validationError;
        }

        var course = await repository.GetByIdAsync(courseId, cancellationToken);
        if (course is null)
        {
            return new CourseOperationResult(null, null, null);
        }

        var code = input.Code!.Trim().ToUpperInvariant();
        if (await repository.CodeExistsAsync(code, courseId, cancellationToken))
        {
            return DuplicateCode();
        }

        course.Code = code;
        course.Name = input.Name!.Trim();
        course.Description = input.Description!.Trim();
        course.DurationMonths = (short)input.DurationMonths!.Value;
        course.UpdatedUtc = DateTimeOffset.UtcNow;

        if (!await repository.SaveAsync(course, isNew: false, cancellationToken))
        {
            return DuplicateCode();
        }

        return new CourseOperationResult(ToDto(course), null, null);
    }

    public async Task<bool> SetActiveAsync(int courseId, bool isActive, CancellationToken cancellationToken)
    {
        var course = await repository.GetByIdAsync(courseId, cancellationToken);
        if (course is null)
        {
            return false;
        }

        if (course.IsActive == isActive)
        {
            return true;
        }

        course.IsActive = isActive;
        course.UpdatedUtc = DateTimeOffset.UtcNow;
        return await repository.SaveAsync(course, isNew: false, cancellationToken);
    }

    private static CourseOperationResult? Validate(CourseInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
        {
            return Invalid("Course code is required.", nameof(input.Code));
        }
        if (input.Code.Trim().Length > 20)
        {
            return Invalid("Course code must be 20 characters or fewer.", nameof(input.Code));
        }
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Invalid("Course name is required.", nameof(input.Name));
        }
        if (input.Name.Trim().Length > 150)
        {
            return Invalid("Course name must be 150 characters or fewer.", nameof(input.Name));
        }
        if (string.IsNullOrWhiteSpace(input.Description))
        {
            return Invalid("Description is required.", nameof(input.Description));
        }
        if (input.Description.Trim().Length > 2000)
        {
            return Invalid("Description must be 2,000 characters or fewer.", nameof(input.Description));
        }
        if (input.DurationMonths is null or < 1 or > short.MaxValue)
        {
            return Invalid("Duration must be a positive number of months.", nameof(input.DurationMonths));
        }

        return null;
    }

    private static CourseOperationResult Invalid(string message, string field) =>
        new(null, message, field[(field.LastIndexOf('.') + 1)..]);

    private static CourseOperationResult DuplicateCode() =>
        new(null, "A course with this code already exists.", nameof(CourseInput.Code));

    private static CourseDto ToDto(Course course) => new(
        course.CourseId,
        course.Code,
        course.Name,
        course.Description,
        course.DurationMonths,
        course.IsActive,
        course.CreatedUtc,
        course.UpdatedUtc);
}