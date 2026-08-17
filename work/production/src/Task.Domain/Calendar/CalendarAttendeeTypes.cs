namespace Task.Domain.Calendar;

/// <summary>
/// Role of a calendar attendee (OpenAPI <c>role</c> enum:
/// <c>required</c>, <c>optional</c>, <c>observer</c>). Unknown values are
/// rejected by the attendee value objects.
/// </summary>
public enum CalendarAttendeeRole
{
    Required = 0,
    Optional = 1,
    Observer = 2,
}

/// <summary>
/// Response status of a calendar attendee (OpenAPI <c>responseStatus</c>
/// enum: <c>pending</c>, <c>accepted</c>, <c>declined</c>, <c>tentative</c>).
/// Declared independently of <c>respondedAt</c>: the Stage 2.2 contract keeps
/// the timestamp nullable regardless of the status. Unknown values are
/// rejected by the attendee value objects.
/// </summary>
public enum CalendarAttendeeResponseStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Tentative = 3,
}
