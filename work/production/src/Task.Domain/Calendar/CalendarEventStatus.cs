namespace Task.Domain.Calendar;

/// <summary>
/// Status of a calendar event (OpenAPI <c>CalendarEvent.status</c> enum:
/// <c>scheduled</c>, <c>cancelled</c>). Unknown values are rejected by the
/// aggregate.
/// </summary>
public enum CalendarEventStatus
{
    Scheduled = 0,
    Cancelled = 1,
}
