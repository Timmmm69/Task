using System.Globalization;

namespace Task.Domain.Reminders;

/// <summary>
/// Deterministic key of a reminder occurrence built from the
/// (reminderId, dueAt UTC) pair. Regenerating the same pair always
/// reproduces the same key, enabling deterministic server-side
/// de-duplication of repeated firing (BR-046, AC-046).
/// </summary>
public sealed record ReminderOccurrenceKey
{
    /// <summary>Upper bound of the canonical key string.</summary>
    public const int MaxLength = 80;

    private const string TimestampFormat = "O";

    private ReminderOccurrenceKey(Guid reminderId, DateTimeOffset dueAtUtc, string value)
    {
        ReminderId = reminderId;
        DueAtUtc = dueAtUtc;
        Value = value;
    }

    public Guid ReminderId { get; }

    /// <summary>The UTC instant the occurrence is due on; equal to the instant encoded in <see cref="Value"/>.</summary>
    public DateTimeOffset DueAtUtc { get; }

    /// <summary>Stable string form of the key: <c>&lt;reminderId&gt;|&lt;dueAt round-trip&gt;</c>.</summary>
    public string Value { get; }

    /// <summary>
    /// Builds the deterministic key of an occurrence for the given reminder
    /// and UTC due instant.
    /// </summary>
    public static ReminderOccurrenceKey From(Guid reminderId, DateTimeOffset dueAtUtc)
    {
        if (reminderId == Guid.Empty)
        {
            throw new ArgumentException("Reminder identifier must not be empty.", nameof(reminderId));
        }

        if (dueAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(dueAtUtc));
        }

        return new ReminderOccurrenceKey(
            reminderId,
            dueAtUtc,
            $"{reminderId:D}|{dueAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Parses a key string. Only the exact canonical form produced by
    /// <see cref="From"/> is accepted.
    /// </summary>
    public static ReminderOccurrenceKey FromValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"An occurrence key must contain between 1 and {MaxLength} characters.",
                nameof(value));
        }

        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException("An occurrence key must have the <reminderId>|<dueAt> form.", nameof(value));
        }

        if (!Guid.TryParseExact(value.AsSpan(0, separator), "D", out var reminderId) || reminderId == Guid.Empty)
        {
            throw new ArgumentException(
                "An occurrence key must start with the canonical reminder identifier.",
                nameof(value));
        }

        if (!DateTimeOffset.TryParseExact(
                value.AsSpan(separator + 1),
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dueAtUtc) ||
            dueAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "An occurrence key must end with the UTC round-trip due instant.",
                nameof(value));
        }

        return From(reminderId, dueAtUtc);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}