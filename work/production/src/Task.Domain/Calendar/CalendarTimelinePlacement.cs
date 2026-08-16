namespace Task.Domain.Calendar;

/// <summary>
/// Pure domain projection of how a task or event is placed in the calendar.
/// A <see cref="CalendarTimelinePlacementKind.Timeline"/> placement carries a
/// half-open UTC interval <c>[StartUtc, EndUtc)</c> (an open-ended task
/// interval has a null <c>EndUtc</c>); a
/// <see cref="CalendarTimelinePlacementKind.DateOnly"/> placement lives on a
/// calendar date without a timeline interval; a
/// <see cref="CalendarTimelinePlacementKind.None"/> placement has no calendar
/// temporal placement at all. No UI, DTO or persistence is involved.
/// </summary>
public sealed record CalendarTimelinePlacement
{
    private CalendarTimelinePlacement(
        CalendarTimelinePlacementKind kind,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        DateOnly? eventDate)
    {
        Kind = kind;
        StartUtc = startUtc;
        EndUtc = endUtc;
        EventDate = eventDate;
    }

    public CalendarTimelinePlacementKind Kind { get; }

    /// <summary>
    /// Interval start; always present for a Timeline placement and always in
    /// the UTC offset.
    /// </summary>
    public DateTimeOffset? StartUtc { get; }

    /// <summary>
    /// Interval end (exclusive); present for a timed event, null for an
    /// open-ended task interval. A null end never comes from a deadline
    /// (BR-049, AC-049).
    /// </summary>
    public DateTimeOffset? EndUtc { get; }

    /// <summary>Calendar date of a DateOnly placement.</summary>
    public DateOnly? EventDate { get; }

    /// <summary>No calendar temporal placement.</summary>
    public static CalendarTimelinePlacement None() => new(CalendarTimelinePlacementKind.None, null, null, null);

    /// <summary>
    /// Date-based placement without a timeline interval, e.g. an all-day
    /// event (BR-048 date-only semantics).
    /// </summary>
    public static CalendarTimelinePlacement DateOnly(DateOnly eventDate) =>
        new(CalendarTimelinePlacementKind.DateOnly, null, null, eventDate);

    /// <summary>
    /// Timeline placement over the half-open UTC interval
    /// <c>[startUtc, endUtc)</c>. An open-ended task interval passes a null
    /// <paramref name="endUtc"/> and stays strictly start-driven.
    /// </summary>
    public static CalendarTimelinePlacement Timeline(DateTimeOffset startUtc, DateTimeOffset? endUtc)
    {
        EnsureUtc(startUtc, nameof(startUtc));
        EnsureOptionalUtc(endUtc, nameof(endUtc));
        if (endUtc is not null && endUtc.Value <= startUtc)
        {
            throw new ArgumentException(
                "The interval end must be strictly later than the interval start.",
                nameof(endUtc));
        }

        return new CalendarTimelinePlacement(CalendarTimelinePlacementKind.Timeline, startUtc, endUtc, null);
    }

    /// <summary>
    /// Projects event timing: a timed event becomes a Timeline placement and
    /// an all-day event becomes a DateOnly placement.
    /// </summary>
    public static CalendarTimelinePlacement FromTiming(CalendarEventTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        return timing.IsAllDay
            ? DateOnly(timing.EventDate)
            : Timeline(timing.StartAtUtc!.Value, timing.EndAtUtc!.Value);
    }

    /// <summary>
    /// Projects a task schedule (read-only). A task without
    /// <c>StartsAtUtc</c> is placed outside the timeline even when a deadline
    /// is present (BR-048, AC-048); a timeline placement is defined only by
    /// <c>StartsAtUtc</c>, and the deadline never becomes a timeline position
    /// (BR-049, AC-049).
    /// </summary>
    public static CalendarTimelinePlacement FromTaskSchedule(TaskSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return schedule.StartsAtUtc is null
            ? None()
            : Timeline(schedule.StartsAtUtc.Value, endUtc: null);
    }

    public bool IsTimeline => Kind == CalendarTimelinePlacementKind.Timeline;

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }

    private static void EnsureOptionalUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue)
        {
            EnsureUtc(value.Value, parameterName);
        }
    }
}