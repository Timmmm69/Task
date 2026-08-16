namespace Task.Domain.Recurrence;

/// <summary>
/// Moment a reminder rule of a recurrence task template fires. Mirrors the
/// OpenAPI <c>RecurrenceTemplateReminderRule.triggerType</c> enum
/// (before_start, before_deadline, at_start, at_deadline).
/// </summary>
public enum RecurrenceReminderTriggerType
{
    BeforeStart = 0,
    BeforeDeadline = 1,
    AtStart = 2,
    AtDeadline = 3,
}