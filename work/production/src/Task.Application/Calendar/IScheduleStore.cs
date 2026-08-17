namespace Task.Application.Calendar;

/// <summary>
/// Raw schedule row as stored by the persistence layer. Tasks and calendar
/// events share one shape: event-only fields (<see cref="EventDate"/>,
/// <see cref="IsAllDay"/>, <see cref="TimeZoneId"/>, <see cref="ProjectId"/>)
/// are null/default for tasks, while <see cref="Priority"/> is only populated
/// for tasks. <see cref="Status"/> is the stored string verbatim.
/// </summary>
/// <param name="ObjectId">Identity of the underlying task or calendar event.</param>
/// <param name="ItemType">Kind of the underlying object.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="EventDate">Stored calendar date; only for calendar events.</param>
/// <param name="IsAllDay">All-day flag; only meaningful for calendar events.</param>
/// <param name="StartAtUtc">Start instant with the UTC offset; null for
/// all-day events, deadline-only tasks and tasks without a schedule.</param>
/// <param name="EndAtUtc">End instant with the UTC offset (for tasks this is
/// the deadline); null for all-day events and start-only tasks.</param>
/// <param name="TimeZoneId">System time-zone identifier of the event; null for tasks.</param>
/// <param name="ProjectId">Project identity; only calendar events carry one.</param>
/// <param name="Status">Stored status string, e.g. <c>scheduled</c> or <c>new</c>.</param>
/// <param name="Priority">Stored task priority; null for calendar events.</param>
public sealed record ScheduleItemRow(
    Guid ObjectId,
    ScheduleItemType ItemType,
    string Title,
    DateOnly? EventDate,
    bool IsAllDay,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    string? TimeZoneId,
    Guid? ProjectId,
    string Status,
    ScheduleItemPriority? Priority);

/// <summary>
/// Read-only persistence port for the unified calendar schedule. Returns the
/// union of active tasks and calendar events intersecting the requested
/// window; ordering is not guaranteed and is established by the query service.
/// The <c>users</c> and <c>projects</c> filters apply to calendar events only,
/// because tasks carry no user or project relationships in the schema.
/// </summary>
public interface IScheduleStore
{
    /// <summary>
    /// Loads schedule rows intersecting <c>[fromUtc, toUtc)</c>: calendar
    /// events (timed by instants, all-day by their local day in the event
    /// time zone) and tasks (interval, start point or deadline point).
    /// </summary>
    /// <param name="organizationId">Tenant identity; must not be empty.</param>
    /// <param name="fromUtc">Window start with the UTC offset.</param>
    /// <param name="toUtc">Window end with the UTC offset.</param>
    /// <param name="users">Optional attendee filter for calendar events; a
    /// null or empty list disables the filter.</param>
    /// <param name="projects">Optional project filter for calendar events; a
    /// null or empty list disables the filter.</param>
    /// <param name="status">Optional exact status filter applied to both
    /// tables; null or empty disables the filter.</param>
    IReadOnlyList<ScheduleItemRow> QuerySchedule(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? users,
        IReadOnlyList<Guid>? projects,
        string? status);
}
