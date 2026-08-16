namespace Task.Domain.Recurrence;

/// <summary>
/// Immutable recurrence schedule of a series. Mirrors the recurrence fields of
/// the OpenAPI <c>RecurrenceSeriesCreate</c> schema. A rule has a valid
/// interval, bounded day selectors, and at most one termination mode: either
/// an until date or a maximum occurrence count, never both (BR-040, AC-040).
/// </summary>
public sealed class RecurrenceRule
{
    private RecurrenceRule(
        RecurrenceFrequency frequency,
        int interval,
        IReadOnlyList<int> weekdays,
        IReadOnlyList<int> monthDays,
        int? monthOfYear,
        DateOnly occurrenceStartDate,
        TimeOnly? localStartTime,
        DateOnly? untilDate,
        int? maxOccurrences)
    {
        Frequency = frequency;
        Interval = interval;
        Weekdays = weekdays;
        MonthDays = monthDays;
        MonthOfYear = monthOfYear;
        OccurrenceStartDate = occurrenceStartDate;
        LocalStartTime = localStartTime;
        UntilDate = untilDate;
        MaxOccurrences = maxOccurrences;
    }

    /// <summary>Cadence of the series; the interval unit is frequency dependent.</summary>
    public RecurrenceFrequency Frequency { get; }

    /// <summary>Every Nth period; bound to 1..999 (OpenAPI <c>RecurrenceSeries.interval</c>).</summary>
    public int Interval { get; }

    /// <summary>
    /// Selected weekdays (1=Monday..7=Sunday) when the rule repeats on chosen
    /// weekdays. Required and non-empty for weekly rules, forbidden for
    /// monthly and yearly rules (OpenAPI <c>RecurrenceSeries.weekdays</c>).
    /// </summary>
    public IReadOnlyList<int> Weekdays { get; }

    /// <summary>
    /// Selected days of the month: positive values count from the first day,
    /// negative values from the last day (-1 is the last day). Non-empty and
    /// required for monthly and yearly rules (OpenAPI
    /// <c>RecurrenceSeries.monthDays</c>).
    /// </summary>
    public IReadOnlyList<int> MonthDays { get; }

    /// <summary>Month of the year (1..12); required for yearly rules only.</summary>
    public int? MonthOfYear { get; }

    /// <summary>First calendar date of the series; every occurrence is on or after it.</summary>
    public DateOnly OccurrenceStartDate { get; }

    /// <summary>Local wall-clock time of each occurrence, interpreted with the series time zone.</summary>
    public TimeOnly? LocalStartTime { get; }

    /// <summary>
    /// Inclusive termination date; the series stops generating after it.
    /// Mutually exclusive with <see cref="MaxOccurrences"/> (BR-040).
    /// </summary>
    public DateOnly? UntilDate { get; }

    /// <summary>
    /// Maximum number of occurrences; the series stops after generating it.
    /// Mutually exclusive with <see cref="UntilDate"/> (BR-040).
    /// </summary>
    public int? MaxOccurrences { get; }

    /// <summary>
    /// Creates a rule. Validation enforces the OpenAPI bounds
    /// (interval 1..999, weekdays 1..7 distinct, month days in -31..31
    /// excluding zero and distinct, month of year 1..12) and the supported
    /// combination grammar: weekly rules need weekdays, monthly and yearly
    /// rules need month days, yearly rules need a month of year, and a rule
    /// with a termination uses exactly one termination mode (BR-040).
    /// </summary>
    public static RecurrenceRule Create(
        RecurrenceFrequency frequency,
        int interval,
        IReadOnlyList<int>? weekdays,
        IReadOnlyList<int>? monthDays,
        int? monthOfYear,
        DateOnly occurrenceStartDate,
        TimeOnly? localStartTime,
        DateOnly? untilDate,
        int? maxOccurrences)
    {
        if (!Enum.IsDefined(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Unknown recurrence frequency.");
        }

        if (interval is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Recurrence interval must be between 1 and 999.");
        }

        var normalizedWeekdays = NormalizeDayNumbers(weekdays, nameof(weekdays), min: 1, max: 7, maxCount: 7);
        var normalizedMonthDays = NormalizeDayNumbers(monthDays, nameof(monthDays), min: -31, max: 31, maxCount: 62);

        if (monthOfYear is not null and (< 1 or > 12))
        {
            throw new ArgumentOutOfRangeException(nameof(monthOfYear), "Month of year must be between 1 and 12.");
        }

        if (untilDate is not null && maxOccurrences is not null)
        {
            throw new ArgumentException(
                "A rule must have at most one termination mode: either an until date or a maximum occurrence count, not both.",
                nameof(maxOccurrences));
        }

        if (untilDate is not null && untilDate.Value < occurrenceStartDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(untilDate),
                "The until date must not be earlier than the occurrence start date.");
        }

        if (maxOccurrences is not null and < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOccurrences), "The maximum occurrence count must be positive.");
        }

        switch (frequency)
        {
            case RecurrenceFrequency.Weekly when normalizedWeekdays.Count == 0:
                throw new ArgumentException("A weekly rule must select at least one weekday.", nameof(weekdays));
            case RecurrenceFrequency.Weekly when normalizedMonthDays.Count != 0:
                throw new ArgumentException("A weekly rule must not select month days.", nameof(monthDays));
            case RecurrenceFrequency.Monthly when normalizedWeekdays.Count != 0:
                throw new ArgumentException("A monthly rule must not select weekdays.", nameof(weekdays));
            case RecurrenceFrequency.Monthly when normalizedMonthDays.Count == 0:
                throw new ArgumentException("A monthly rule must select at least one month day.", nameof(monthDays));
            case RecurrenceFrequency.Yearly when normalizedMonthDays.Count == 0:
                throw new ArgumentException("A yearly rule must select at least one month day.", nameof(monthDays));
            case RecurrenceFrequency.Yearly when monthOfYear is null:
                throw new ArgumentException("A yearly rule must select a month of year.", nameof(monthOfYear));
            case RecurrenceFrequency.Daily when normalizedMonthDays.Count != 0:
                throw new ArgumentException("A daily rule must not select month days.", nameof(monthDays));
            case RecurrenceFrequency.Daily when monthOfYear is not null:
                throw new ArgumentException("A daily rule must not select a month of year.", nameof(monthOfYear));
            case RecurrenceFrequency.Weekly when monthOfYear is not null:
            case RecurrenceFrequency.Monthly when monthOfYear is not null:
                throw new ArgumentException(
                    "A daily, weekly, or monthly rule must not select a month of year.",
                    nameof(monthOfYear));
        }

        return new RecurrenceRule(
            frequency,
            interval,
            normalizedWeekdays,
            normalizedMonthDays,
            monthOfYear,
            occurrenceStartDate,
            localStartTime,
            untilDate,
            maxOccurrences);
    }

    /// <inheritdoc />
    public bool Equals(RecurrenceRule? other) =>
        other is not null &&
        Frequency == other.Frequency &&
        Interval == other.Interval &&
        MonthOfYear == other.MonthOfYear &&
        OccurrenceStartDate == other.OccurrenceStartDate &&
        LocalStartTime == other.LocalStartTime &&
        UntilDate == other.UntilDate &&
        MaxOccurrences == other.MaxOccurrences &&
        Weekdays.SequenceEqual(other.Weekdays) &&
        MonthDays.SequenceEqual(other.MonthDays);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RecurrenceRule);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Frequency);
        hash.Add(Interval);
        hash.Add(MonthOfYear);
        hash.Add(OccurrenceStartDate);
        hash.Add(LocalStartTime);
        hash.Add(UntilDate);
        hash.Add(MaxOccurrences);
        foreach (var weekday in Weekdays)
        {
            hash.Add(weekday);
        }

        foreach (var monthDay in MonthDays)
        {
            hash.Add(monthDay);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(RecurrenceRule? left, RecurrenceRule? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(RecurrenceRule? left, RecurrenceRule? right) => !(left == right);

    private static IReadOnlyList<int> NormalizeDayNumbers(
        IReadOnlyList<int>? values,
        string parameterName,
        int min,
        int max,
        int maxCount)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        if (values.Count > maxCount)
        {
            throw new ArgumentException($"{parameterName} must not contain more than {maxCount} entries.", parameterName);
        }

        var copy = new int[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value < min || value > max || (min <= 0 && value == 0))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"{parameterName} entries must be between {min} and {max} and must not be zero.");
            }

            copy[index] = value;
        }

        Array.Sort(copy);
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index] == copy[index - 1])
            {
                throw new ArgumentException($"{parameterName} must not contain duplicates.", parameterName);
            }
        }

        return copy;
    }
}