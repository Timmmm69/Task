namespace Task.Domain.Recurrence;

/// <summary>
/// Immutable reminder rule of a recurrence task template. Mirrors the
/// OpenAPI <c>RecurrenceTemplateReminderRule</c> schema.
/// </summary>
public sealed record RecurrenceTemplateReminderRule
{
    private RecurrenceTemplateReminderRule(
        Guid id,
        Guid? recipientUserId,
        RecurrenceReminderTriggerType triggerType,
        int? offsetMinutes)
    {
        Id = id;
        RecipientUserId = recipientUserId;
        TriggerType = triggerType;
        OffsetMinutes = offsetMinutes;
    }

    public Guid Id { get; }

    public Guid? RecipientUserId { get; }

    public RecurrenceReminderTriggerType TriggerType { get; }

    /// <summary>
    /// Offset from the trigger moment; when present it must be non-negative.
    /// </summary>
    public int? OffsetMinutes { get; }

    /// <summary>
    /// Creates a reminder rule; the trigger type must be defined and the
    /// offset, when present, must not be negative.
    /// </summary>
    public static RecurrenceTemplateReminderRule Create(
        Guid id,
        Guid? recipientUserId,
        RecurrenceReminderTriggerType triggerType,
        int? offsetMinutes)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reminder rule identifier must not be empty.", nameof(id));
        }

        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("Reminder recipient must not be empty.", nameof(recipientUserId));
        }

        if (!Enum.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerType), "Unknown reminder trigger type.");
        }

        if (offsetMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetMinutes), "Reminder offset must not be negative.");
        }

        return new RecurrenceTemplateReminderRule(id, recipientUserId, triggerType, offsetMinutes);
    }
}