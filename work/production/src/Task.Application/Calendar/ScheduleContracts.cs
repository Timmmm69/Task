namespace Task.Application.Calendar;

/// <summary>Kind of a schedule item (OpenAPI <c>ScheduleItem.itemType</c>).</summary>
public enum ScheduleItemType
{
    /// <summary>Task item.</summary>
    Task = 0,

    /// <summary>Calendar event item.</summary>
    CalendarEvent = 1,
}

/// <summary>User-facing priority of a task item (OpenAPI <c>ScheduleItem.priority</c>).</summary>
public enum ScheduleItemPriority
{
    /// <summary>Low priority.</summary>
    Low = 0,

    /// <summary>Normal priority.</summary>
    Normal = 1,

    /// <summary>High priority.</summary>
    High = 2,

    /// <summary>Critical priority.</summary>
    Critical = 3,
}

/// <summary>Severity of a schedule overlap (OpenAPI <c>ScheduleConflict.severity</c>).</summary>
public enum ScheduleConflictSeverity
{
    /// <summary>Informational overlap; not produced by the current slice.</summary>
    Info = 0,

    /// <summary>Overlap shorter than 30 minutes.</summary>
    Warning = 1,

    /// <summary>Overlap of at least 30 minutes.</summary>
    Blocking = 2,
}

/// <summary>
/// Single entry of the unified calendar projection (OpenAPI
/// <c>ScheduleItem</c>). <paramref name="StartAtUtc"/> and
/// <paramref name="EndAtUtc"/> are carried over from the stored row; an
/// all-day event carries no instants, and <paramref name="LocalDate"/> is
/// always the local calendar date of the item.
/// </summary>
/// <param name="ObjectId">Identity of the underlying task or calendar event.</param>
/// <param name="ItemType">Kind of the underlying object.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="LocalDate">Calendar date without a time zone.</param>
/// <param name="StartAtUtc">Start instant with the UTC offset; null for all-day events.</param>
/// <param name="EndAtUtc">End instant with the UTC offset; null for all-day events.</param>
/// <param name="IsAllDay">Whether the item covers a full local day.</param>
/// <param name="ProjectId">Project identity; only calendar events carry one.</param>
/// <param name="Status">Stored status string, e.g. <c>scheduled</c> or <c>new</c>.</param>
/// <param name="Priority">Task priority; null for calendar events.</param>
public sealed record ScheduleItem(
    Guid ObjectId,
    ScheduleItemType ItemType,
    string Title,
    DateOnly LocalDate,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    bool IsAllDay,
    Guid? ProjectId,
    string Status,
    ScheduleItemPriority? Priority,
    Guid? RecurrenceSeriesId = null,
    string? Description = null);

/// <summary>
/// Page of unified calendar schedule items (OpenAPI <c>SchedulePage</c>).
/// Cursor pagination is not implemented by this slice: <see cref="NextCursor"/>
/// is always null and <see cref="RangeStart"/>/<see cref="RangeEnd"/> echo the
/// requested window.
/// </summary>
/// <param name="Items">Items intersecting the requested window, sorted by
/// interval start, item type and object identity.</param>
/// <param name="NextCursor">Opaque cursor for the next page; always null here.</param>
/// <param name="RangeStart">Requested window start with the UTC offset.</param>
/// <param name="RangeEnd">Requested window end with the UTC offset.</param>
public sealed record SchedulePage(
    IReadOnlyList<ScheduleItem> Items,
    string? NextCursor,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd);

/// <summary>
/// Overlap of two schedule items with positive duration (OpenAPI
/// <c>ScheduleConflict</c>). The overlap interval is half-open:
/// <c>[OverlapStart, OverlapEnd)</c>.
/// </summary>
/// <param name="LeftObjectId">Object with the earlier interval start; ties are
/// broken by object identity.</param>
/// <param name="RightObjectId">Object with the later interval start.</param>
/// <param name="OverlapStart">Overlap start instant with the UTC offset.</param>
/// <param name="OverlapEnd">Overlap end instant with the UTC offset.</param>
/// <param name="Severity">Overlap severity; at least 30 minutes is blocking.</param>
public sealed record ScheduleConflict(
    Guid LeftObjectId,
    Guid RightObjectId,
    DateTimeOffset OverlapStart,
    DateTimeOffset OverlapEnd,
    ScheduleConflictSeverity Severity);
