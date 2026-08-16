using System.Globalization;

namespace Task.Domain.Recurrence;

/// <summary>
/// Deterministic key of a recurrence occurrence. The key value is the
/// ISO-8601 calendar date of the occurrence, so regenerating the same set
/// always reproduces the same keys and a key is unique per series and date
/// (BR-041, AC-041; architecture §13.5 stores a unique occurrence key).
/// </summary>
public sealed record OccurrenceKey
{
    /// <summary>OpenAPI <c>RecurrenceOccurrence.occurrenceKey</c> upper bound.</summary>
    public const int MaxLength = 64;

    private const string DateFormat = "yyyy-MM-dd";

    private OccurrenceKey(DateOnly localDate, string value)
    {
        LocalDate = localDate;
        Value = value;
    }

    /// <summary>
    /// The calendar date the occurrence is scheduled on. Always equal to the
    /// date encoded in <see cref="Value"/>.
    /// </summary>
    public DateOnly LocalDate { get; }

    /// <summary>Stable string form of the key, an ISO-8601 calendar date.</summary>
    public string Value { get; }

    /// <summary>
    /// Builds the deterministic key of an occurrence for the given date.
    /// </summary>
    public static OccurrenceKey FromLocalDate(DateOnly localDate) =>
        new(localDate, localDate.ToString(DateFormat, CultureInfo.InvariantCulture));

    /// <summary>
    /// Parses a key string. Only the exact ISO-8601 calendar date form produced
    /// by <see cref="FromLocalDate"/> is accepted.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, too long, or not an ISO-8601 calendar date.</exception>
    public static OccurrenceKey FromValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"An occurrence key must contain between 1 and {MaxLength} characters.",
                nameof(value));
        }

        if (!DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
        {
            throw new ArgumentException(
                "An occurrence key must be an ISO-8601 calendar date in the yyyy-MM-dd form.",
                nameof(value));
        }

        return FromLocalDate(localDate);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}