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

    void Add(CalendarEvent calendarEvent);

    void Save(CalendarEvent calendarEvent, int expectedVersion);
}
