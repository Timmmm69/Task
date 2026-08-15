namespace Task.Domain;

/// <summary>
/// Immutable planning window of a task. Both bounds are optional, and every
/// non-null timestamp must use the UTC offset. A deadline earlier than the
/// start is rejected; equal start and deadline are allowed.
/// </summary>
public sealed record TaskSchedule
{
    private TaskSchedule(DateTimeOffset? startsAtUtc, DateTimeOffset? deadlineUtc)
    {
        StartsAtUtc = startsAtUtc;
        DeadlineUtc = deadlineUtc;
    }

    public DateTimeOffset? StartsAtUtc { get; }

    public DateTimeOffset? DeadlineUtc { get; }

    public static TaskSchedule Create(DateTimeOffset? startsAtUtc, DateTimeOffset? deadlineUtc)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(deadlineUtc, nameof(deadlineUtc));

        if (startsAtUtc is not null && deadlineUtc is not null && deadlineUtc.Value < startsAtUtc.Value)
        {
            throw new ArgumentException(
                "Deadline must not be earlier than the scheduled start.",
                nameof(deadlineUtc));
        }

        return new TaskSchedule(startsAtUtc, deadlineUtc);
    }

    private static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}