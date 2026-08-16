namespace Task.Domain.Reminders;

/// <summary>
/// Value object of the single trigger configuration of a reminder. A
/// reminder has exactly one trigger mode (BR-044, AC-044): the trigger
/// type decides which configuration field is allowed and which must be
/// absent, so contradictory configurations are rejected at construction.
/// </summary>
public sealed record ReminderTrigger
{
    /// <summary>OpenAPI <c>Reminder.offsetMinutes</c> upper bound.</summary>
    public const int MaxOffsetMinutes = 525600;

    private ReminderTrigger(ReminderTriggerType type, int? offsetMinutes, DateTimeOffset? absoluteTriggerAt)
    {
        Type = type;
        OffsetMinutes = offsetMinutes;
        AbsoluteTriggerAt = absoluteTriggerAt;
    }

    public ReminderTriggerType Type { get; }

    /// <summary>
    /// Offset from the trigger moment; present only for
    /// <see cref="ReminderTriggerType.BeforeStart"/> and
    /// <see cref="ReminderTriggerType.BeforeDeadline"/> and bounded by
    /// <see cref="MaxOffsetMinutes"/>.
    /// </summary>
    public int? OffsetMinutes { get; }

    /// <summary>
    /// Absolute firing instant; present only for
    /// <see cref="ReminderTriggerType.Absolute"/>.
    /// </summary>
    public DateTimeOffset? AbsoluteTriggerAt { get; }

    /// <summary>
    /// Builds a trigger configuration. Exactly one configuration must match
    /// the given type: an absolute trigger requires <paramref name="absoluteTriggerAt"/>
    /// with no offset; a before-start/before-deadline trigger requires an offset in
    /// the 0..<see cref="MaxOffsetMinutes"/> range with no absolute instant; an
    /// at-start/at-deadline trigger uses neither field.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The trigger type is undefined or the offset is out of range.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The configuration contradicts the trigger type, an identifier instant
    /// is not UTC, or a timestamp is empty.
    /// </exception>
    public static ReminderTrigger Create(
        ReminderTriggerType type,
        int? offsetMinutes,
        DateTimeOffset? absoluteTriggerAt)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Unknown reminder trigger type.");
        }

        switch (type)
        {
            case ReminderTriggerType.Absolute:
                EnsurePresent(absoluteTriggerAt, nameof(absoluteTriggerAt), "An absolute trigger requires absoluteTriggerAt.");
                EnsureAbsent(offsetMinutes, nameof(offsetMinutes), "An absolute trigger must not carry an offset.");
                EnsureUtc(absoluteTriggerAt!.Value, nameof(absoluteTriggerAt));
                return new ReminderTrigger(type, offsetMinutes: null, absoluteTriggerAt);

            case ReminderTriggerType.BeforeStart:
            case ReminderTriggerType.BeforeDeadline:
                EnsurePresent(offsetMinutes, nameof(offsetMinutes), "An offset trigger requires offsetMinutes.");
                if (offsetMinutes!.Value is < 0 or > MaxOffsetMinutes)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(offsetMinutes),
                        $"offsetMinutes must be between 0 and {MaxOffsetMinutes}.");
                }

                EnsureAbsent(absoluteTriggerAt, nameof(absoluteTriggerAt), "An offset trigger must not carry absoluteTriggerAt.");
                return new ReminderTrigger(type, offsetMinutes, absoluteTriggerAt: null);

            case ReminderTriggerType.AtStart:
            case ReminderTriggerType.AtDeadline:
                EnsureAbsent(offsetMinutes, nameof(offsetMinutes), "An at-start/at-deadline trigger must not carry an offset.");
                EnsureAbsent(absoluteTriggerAt, nameof(absoluteTriggerAt), "An at-start/at-deadline trigger must not carry absoluteTriggerAt.");
                return new ReminderTrigger(type, offsetMinutes: null, absoluteTriggerAt: null);

            default:
                throw new ArgumentOutOfRangeException(nameof(type), "Unsupported reminder trigger type.");
        }
    }

    private static void EnsurePresent(int? value, string parameterName, string message)
    {
        if (value is null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsurePresent(DateTimeOffset? value, string parameterName, string message)
    {
        if (value is null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureAbsent(int? value, string parameterName, string message)
    {
        if (value is not null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureAbsent(DateTimeOffset? value, string parameterName, string message)
    {
        if (value is not null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}