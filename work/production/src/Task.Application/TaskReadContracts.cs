using Task.Domain;

namespace Task.Application;

/// <summary>Database-backed projection used by the read-only Task API.</summary>
public sealed record TaskReadProjection(
    Guid Id,
    Guid OrganizationId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Title,
    Guid AuthorUserId,
    TaskWorkStatus Status,
    TaskPriority Priority,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? DeadlineAtUtc, TaskCardContent? Content = null, DateTimeOffset? CompletedAtUtc = null, Guid? RecurrenceSeriesId = null);

/// <summary>
/// Security binding and opaque continuation supplied to one task-list query.
/// The page size and normalized query are intentionally fixed by this first
/// read increment.
/// </summary>
public sealed record TaskReadPageRequest(
    Guid OrganizationId,
    Guid UserAccountId,
    long AuthorizationScopeVersion,
    string? Cursor = null);

/// <summary>One active-task page. Exact totals are deliberately not counted.</summary>
public sealed record TaskReadPage(
    IReadOnlyList<TaskReadProjection> Items,
    string? NextCursor,
    long? Total = null);

/// <summary>
/// Stable failure raised for malformed, stale, or differently-bound task cursors.
/// HTTP adapters map this condition to SEARCH_CURSOR_INVALID without exposing details.
/// </summary>
public sealed class TaskReadCursorException : Exception
{
    public TaskReadCursorException()
        : base("Task cursor is invalid.")
    {
    }

    internal TaskReadCursorException(Exception innerException)
        : base("Task cursor is invalid.", innerException)
    {
    }
}
