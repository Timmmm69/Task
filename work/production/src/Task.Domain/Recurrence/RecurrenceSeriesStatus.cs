namespace Task.Domain.Recurrence;

/// <summary>
/// Lifecycle status of a recurrence series. Mirrors the OpenAPI
/// <c>RecurrenceSeries.status</c> enum (active, paused, completed, cancelled).
/// Cancellation is a domain status; it never moves the series into the
/// universal trash (BR-043, AC-043).
/// </summary>
public enum RecurrenceSeriesStatus
{
    Active = 0,
    Paused = 1,
    Completed = 2,
    Cancelled = 3,
}