using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Application.Calendar;

/// <summary>
/// Read-only projection of a single calendar event for query use cases
/// (OpenAPI <c>CalendarEvent</c>): scalar fields, timing fields and both
/// attendee collections in their stored order.
/// </summary>
public sealed record CalendarEventDetails(
    Guid Id,
    Guid OrganizationId,
    Guid? ProjectId,
    string Title,
    string? Description,
    DateOnly EventDate,
    bool IsAllDay,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? EndAtUtc,
    string TimeZoneId,
    CalendarEventStatus Status,
    EntityLifecycleState LifecycleState,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<EventAttendee> UserAttendees,
    IReadOnlyList<ContactAttendee> ContactAttendees);
