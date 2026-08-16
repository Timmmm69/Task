namespace Task.Domain.Reminders;

/// <summary>
/// Lifecycle status of a reminder occurrence. Mirrors the OpenAPI
/// <c>ReminderOccurrence.status</c> enum exactly.
/// </summary>
public enum ReminderOccurrenceStatus
{
    Created = 0,
    Claimed = 1,
    Delivered = 2,
    Failed = 3,
    DeadLetter = 4,
    Cancelled = 5,
}