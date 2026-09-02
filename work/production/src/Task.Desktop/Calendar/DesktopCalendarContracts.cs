namespace Task.Desktop.Calendar;

public enum DesktopScheduleItemType { Task, CalendarEvent }
public enum DesktopCalendarPriority { Low, Normal, High, Critical }
public enum DesktopConflictSeverity { Info, Warning, Blocking }

public sealed record DesktopScheduleItem(
    Guid ObjectId,
    DesktopScheduleItemType ItemType,
    string Title,
    DateOnly LocalDate,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    bool IsAllDay,
    Guid? ProjectId,
    string Status,
    DesktopCalendarPriority? Priority,
    Guid? RecurrenceSeriesId = null,
    string? Description = null);

public sealed record DesktopSchedulePage(
    IReadOnlyList<DesktopScheduleItem> Items,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc);

public sealed record DesktopScheduleConflict(
    Guid LeftObjectId,
    Guid RightObjectId,
    DateTimeOffset OverlapStartUtc,
    DateTimeOffset OverlapEndUtc,
    DesktopConflictSeverity Severity);

public sealed record DesktopCalendarAttendee(
    Guid Id,
    bool IsUser,
    string Role,
    string ResponseStatus,
    DateTimeOffset? RespondedAtUtc);

public sealed record DesktopCalendarEvent(
    Guid Id,
    Guid OrganizationId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? ProjectId,
    string Title,
    string? Description,
    DateOnly EventDate,
    bool IsAllDay,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    string TimeZoneId,
    string Status,
    IReadOnlyList<DesktopCalendarAttendee> Attendees,
    string EntityTag);

public sealed record DesktopCalendarEventCommand(
    Guid? ProjectId,
    string Title,
    string? Description,
    DateOnly EventDate,
    bool IsAllDay,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    string TimeZoneId,
    string Status = "scheduled",
    IReadOnlyList<DesktopCalendarAttendee>? Attendees = null,
    DateOnly? EndDate = null);

public abstract record DesktopCalendarResult<T>(string? CorrelationId)
{
    public sealed record Succeeded(T Value, string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record Forbidden(string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record NotFound(string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record AuthenticationFailure() : DesktopCalendarResult<T>((string?)null);
    public sealed record ServerUnavailable(string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record ValidationFailure(string Message, string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record VersionConflict(string? Id = null) : DesktopCalendarResult<T>(Id);
    public sealed record MalformedResponse(string? Id = null) : DesktopCalendarResult<T>(Id);
}

public interface IDesktopCalendarApiClient
{
    global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken);
    global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(
        Guid eventId, CancellationToken cancellationToken);
    global::System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
    global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(
        DesktopCalendarEventCommand command, CancellationToken cancellationToken);
    global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(
        Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken);
}
