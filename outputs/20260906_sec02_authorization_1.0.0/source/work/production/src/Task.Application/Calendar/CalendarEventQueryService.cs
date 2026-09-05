using Task.Domain.Calendar;

namespace Task.Application.Calendar;

/// <summary>
/// Read-only application service for querying calendar events. Never mutates
/// the aggregate: it only loads the event through
/// <see cref="ICalendarEventStore.Get"/> and projects it into
/// <see cref="CalendarEventDetails"/> without any lifecycle transitions.
/// </summary>
public sealed class CalendarEventQueryService
{
    private readonly ICalendarEventStore _store;

    public CalendarEventQueryService(ICalendarEventStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public CalendarEventDetails? GetById(Guid organizationId, Guid eventId, Guid? actorId = null)
    {
        var calendarEvent = actorId is { } actor ? _store.GetForUser(eventId, organizationId, actor) : _store.Get(eventId, organizationId);
        if (calendarEvent is null)
        {
            return null;
        }

        return new CalendarEventDetails(
            calendarEvent.Metadata.Id,
            calendarEvent.Metadata.OrganizationId,
            calendarEvent.ProjectId,
            calendarEvent.Title,
            calendarEvent.Description,
            calendarEvent.Timing.EventDate,
            calendarEvent.Timing.IsAllDay,
            calendarEvent.Timing.StartAtUtc,
            calendarEvent.Timing.EndAtUtc,
            calendarEvent.Timing.TimeZoneId,
            calendarEvent.Status,
            calendarEvent.Metadata.LifecycleState,
            calendarEvent.Metadata.Version,
            calendarEvent.Metadata.CreatedAtUtc,
            calendarEvent.Metadata.UpdatedAtUtc,
            calendarEvent.UserAttendees,
            calendarEvent.ContactAttendees);
    }
}
