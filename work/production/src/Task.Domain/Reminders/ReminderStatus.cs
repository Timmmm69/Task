namespace Task.Domain.Reminders;

/// <summary>
/// Lifecycle status of a reminder. Mirrors the OpenAPI
/// <c>Reminder.status</c> enum exactly; cancelled and expired are terminal
/// and can never be the initial status.
/// </summary>
public enum ReminderStatus
{
    Scheduled = 0,
    Due = 1,
    Delivered = 2,
    Snoozed = 3,
    Cancelled = 4,
    Expired = 5,
}