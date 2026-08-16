namespace Task.Domain.Recurrence;

/// <summary>
/// Lifecycle status of a single occurrence of a recurrence series.
/// Mirrors the OpenAPI <c>RecurrenceOccurrence.status</c> enum
/// (planned, generated, skipped, cancelled).
/// </summary>
public enum RecurrenceOccurrenceStatus
{
    Planned = 0,
    Generated = 1,
    Skipped = 2,
    Cancelled = 3,
}