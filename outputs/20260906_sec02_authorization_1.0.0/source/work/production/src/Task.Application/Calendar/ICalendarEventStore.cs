using Task.Domain.Calendar;

namespace Task.Application.Calendar;

/// <summary>
/// Storage port for the CalendarEvent aggregate. Implementations own
/// persistence and must enforce the optimistic concurrency guarantee of
/// <see cref="Save"/>: a saved event is expected to currently have
/// <paramref name="expectedVersion"/>.
/// </summary>
public interface ICalendarEventStore
{
    CalendarEvent? Get(Guid eventId, Guid organizationId);

    CalendarEvent? GetForUser(Guid eventId, Guid organizationId, Guid actorId)
    {
        var item = Get(eventId, organizationId);
        return item?.Metadata.CreatedBy == actorId || item?.UserAttendees.Any(a => a.UserAccountId == actorId) == true ? item : null;
    }

    void Add(CalendarEvent calendarEvent);
    void AddForUser(CalendarEvent calendarEvent) => Add(calendarEvent);

    void Save(CalendarEvent calendarEvent, int expectedVersion);
    void SaveForUser(CalendarEvent calendarEvent, int expectedVersion) => Save(calendarEvent, expectedVersion);
}
