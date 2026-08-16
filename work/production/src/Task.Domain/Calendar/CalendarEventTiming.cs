namespace Task.Domain.Calendar;

/// <summary>
/// Temporal fields of a calendar event (OpenAPI <c>CalendarEvent</c>:
/// <c>eventDate</c>, <c>isAllDay</c>, <c>startAtUtc</c>, <c>endAtUtc</c>,
/// <c>timeZone</c>). Every local interpretation is bound to an explicit time
/// zone (BR-050, AC-050): all non-null instants have the UTC offset, an
/// all-day event carries no instants, and the <c>eventDate</c> of a timed
/// event is exactly the local start date in its time zone.
/// A timed event interval is half-open: <c>[startAtUtc, endAtUtc)</c>.
/// </summary>
public sealed record CalendarEventTiming
{
    private CalendarEventTiming(
        DateOnly eventDate,
        bool isAllDay,
        DateTimeOffset? startAtUtc,
        DateTimeOffset? endAtUtc,
        string timeZoneId)
    {
        EventDate = eventDate;
        IsAllDay = isAllDay;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        TimeZoneId = timeZoneId;
    }

    /// <summary>Calendar date without a time zone (OpenAPI <c>eventDate</c>).</summary>
    public DateOnly EventDate { get; }

    public bool IsAllDay { get; }

    /// <summary>RFC 3339 instant with the UTC offset; null for all-day events.</summary>
    public DateTimeOffset? StartAtUtc { get; }

    /// <summary>RFC 3339 instant with the UTC offset; null for all-day events.</summary>
    public DateTimeOffset? EndAtUtc { get; }

    /// <summary>System time-zone identifier (max 64 characters) for local interpretation.</summary>
    public string TimeZoneId { get; }

    /// <summary>
    /// Creates an all-day event: no timeline instants, date-based only.
    /// </summary>
    public static CalendarEventTiming CreateAllDay(DateOnly eventDate, string timeZoneId)
    {
        var normalizedTimeZone = NormalizeTimeZone(timeZoneId);
        return new CalendarEventTiming(
            eventDate,
            isAllDay: true,
            startAtUtc: null,
            endAtUtc: null,
            normalizedTimeZone);
    }

    /// <summary>
    /// Creates a timed event. Both instants are required and must carry the
    /// UTC offset, <paramref name="endAtUtc"/> must be strictly later than
    /// <paramref name="startAtUtc"/>, and <paramref name="eventDate"/> must be
    /// the local start date of <paramref name="startAtUtc"/> in the event time
    /// zone. Otherwise the local time would be interpreted without a time
    /// zone, which BR-050/AC-050 forbid.
    /// </summary>
    public static CalendarEventTiming CreateTimed(
        DateOnly eventDate,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        string timeZoneId)
    {
        var normalizedTimeZone = NormalizeTimeZone(timeZoneId);
        EnsureUtc(startAtUtc, nameof(startAtUtc));
        EnsureUtc(endAtUtc, nameof(endAtUtc));

        if (endAtUtc <= startAtUtc)
        {
            throw new ArgumentException(
                "The end instant must be strictly later than the start instant.",
                nameof(endAtUtc));
        }

        var localStartDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(startAtUtc, TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZone)).DateTime);

        if (localStartDate != eventDate)
        {
            throw new ArgumentException(
                "The event date must be the local start date in the event time zone.",
                nameof(eventDate));
        }

        return new CalendarEventTiming(
            eventDate,
            isAllDay: false,
            startAtUtc,
            endAtUtc,
            normalizedTimeZone);
    }

    private static string NormalizeTimeZone(string timeZoneId)
    {
        if (timeZoneId is null)
        {
            throw new ArgumentNullException(nameof(timeZoneId));
        }

        var normalized = timeZoneId.Trim();
        if (normalized.Length == 0 || normalized.Length > 64)
        {
            throw new ArgumentException(
                "The time zone identifier must be between 1 and 64 characters.",
                nameof(timeZoneId));
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out _))
        {
            throw new ArgumentException(
                "The time zone identifier must resolve to a known time zone.",
                nameof(timeZoneId));
        }

        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}