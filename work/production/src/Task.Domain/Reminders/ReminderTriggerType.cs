namespace Task.Domain.Reminders;

/// <summary>
/// Moment a reminder fires. Mirrors the OpenAPI <c>Reminder.triggerType</c>
/// enum exactly (absolute, before_start, before_deadline, at_start,
/// at_deadline); no other trigger modes exist (BR-044, AC-044).
/// </summary>
public enum ReminderTriggerType
{
    Absolute = 0,
    BeforeStart = 1,
    BeforeDeadline = 2,
    AtStart = 3,
    AtDeadline = 4,
}