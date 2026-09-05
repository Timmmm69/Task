using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Application.Calendar;

/// <summary>
/// Application service for the calendar event lifecycle, scalar changes and
/// attendee replacement. Every mutating operation loads the aggregate
/// through <see cref="ICalendarEventStore.Get"/>, verifies the
/// caller-provided expected version against the stored one, delegates the
/// transition to the aggregate and persists the result with the original
/// expected version so the store can atomically confirm the concurrency
/// guard. Domain rules are not duplicated here and are never bypassed.
/// </summary>
public sealed class CalendarEventLifecycleService
{
    private readonly ICalendarEventStore _store;

    public CalendarEventLifecycleService(ICalendarEventStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public CalendarEvent Create(
        Guid eventId,
        Guid organizationId,
        Guid creatorId,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        DateTimeOffset createdAtUtc)
    {
        var calendarEvent = CalendarEvent.Create(
            eventId,
            organizationId,
            creatorId,
            projectId,
            title,
            description,
            timing,
            createdAtUtc);
        _store.AddForUser(calendarEvent);

        return calendarEvent;
    }

    public CalendarEvent Create(
        Guid eventId,
        Guid organizationId,
        Guid creatorId,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        DateTimeOffset createdAtUtc,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees)
    {
        var calendarEvent = CalendarEvent.Create(
            eventId,
            organizationId,
            creatorId,
            projectId,
            title,
            description,
            timing,
            createdAtUtc,
            userAttendees,
            contactAttendees);
        _store.AddForUser(calendarEvent);

        return calendarEvent;
    }

    public CalendarEvent UpdateDetails(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing) =>
        Execute(
            organizationId,
            eventId,
            expectedVersion,
            (calendarEvent) => calendarEvent.UpdateDetails(
                actorId,
                occurredAtUtc,
                projectId,
                title,
                description,
                timing));

    public CalendarEvent ApplyPatch(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        Guid? projectId,
        string title,
        string? description,
        CalendarEventTiming timing,
        CalendarEventStatus status,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees) =>
        Execute(
            organizationId,
            eventId,
            expectedVersion,
            calendarEvent => calendarEvent.ApplyPatch(
                actorId,
                occurredAtUtc,
                projectId,
                title,
                description,
                timing,
                status,
                userAttendees,
                contactAttendees));

    public CalendarEvent Cancel(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.Cancel(actorId, occurredAtUtc));

    public CalendarEvent Reschedule(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.Reschedule(actorId, occurredAtUtc));

    public CalendarEvent ReplaceAttendees(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc,
        IEnumerable<EventAttendee> userAttendees,
        IEnumerable<ContactAttendee> contactAttendees) =>
        Execute(
            organizationId,
            eventId,
            expectedVersion,
            (calendarEvent) => calendarEvent.ReplaceAttendees(
                actorId,
                occurredAtUtc,
                userAttendees,
                contactAttendees));

    public CalendarEvent Archive(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.Archive(actorId, occurredAtUtc));

    public CalendarEvent RestoreFromArchive(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.RestoreFromArchive(actorId, occurredAtUtc));

    public CalendarEvent MoveToTrash(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.MoveToTrash(actorId, occurredAtUtc));

    public CalendarEvent RestoreFromTrash(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAtUtc) =>
        Execute(organizationId, eventId, expectedVersion, (calendarEvent) => calendarEvent.RestoreFromTrash(actorId, occurredAtUtc));

    private CalendarEvent Execute(
        Guid organizationId,
        Guid eventId,
        int expectedVersion,
        Func<CalendarEvent, CalendarEvent> transition)
    {
        var calendarEvent = _store.Get(eventId, organizationId)
            ?? throw new KeyNotFoundException(
                $"Calendar event '{eventId}' was not found in organization '{organizationId}'.");

        if (calendarEvent.Metadata.Version != expectedVersion)
        {
            throw new CalendarEventConcurrencyException(eventId, expectedVersion, calendarEvent.Metadata.Version);
        }

        var updatedEvent = transition(calendarEvent);
        _store.SaveForUser(updatedEvent, expectedVersion);

        return updatedEvent;
    }
}
