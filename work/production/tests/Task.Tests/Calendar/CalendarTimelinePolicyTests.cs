using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarTimelinePolicyTests
{
    private const string TimeZoneId = "Europe/Berlin";

    private static readonly DateTimeOffset TenAm = new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ElevenAm = new(2026, 8, 17, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Noon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static CalendarTimelinePlacement TimedEvent(DateTimeOffset start, DateTimeOffset end) =>
        CalendarTimelinePlacement.FromTiming(
            CalendarEventTiming.CreateTimed(
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).DateTime),
                start,
                end,
                TimeZoneId));

    private static CalendarTimelinePlacement AllDayEvent(DateOnly date) =>
        CalendarTimelinePlacement.FromTiming(CalendarEventTiming.CreateAllDay(date, TimeZoneId));

    [Fact]
    public void FromTiming_AllDayEvent_YieldsDateOnlyPlacement()
    {
        var placement = AllDayEvent(new DateOnly(2026, 8, 17));

        Assert.Equal(CalendarTimelinePlacementKind.DateOnly, placement.Kind);
        Assert.Equal(new DateOnly(2026, 8, 17), placement.EventDate);
        Assert.Null(placement.StartUtc);
        Assert.Null(placement.EndUtc);
    }

    [Fact]
    public void FromTiming_TimedEvent_YieldsTimelinePlacement()
    {
        var placement = TimedEvent(TenAm, ElevenAm);

        Assert.Equal(CalendarTimelinePlacementKind.Timeline, placement.Kind);
        Assert.Equal(TenAm, placement.StartUtc);
        Assert.Equal(ElevenAm, placement.EndUtc);
    }

    [Fact]
    public void FromTaskSchedule_NoStartWithoutDeadline_YieldsNonePlacement()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: null, deadlineUtc: null);

        var placement = CalendarTimelinePlacement.FromTaskSchedule(schedule);

        Assert.Equal(CalendarTimelinePlacementKind.None, placement.Kind);
    }

    [Fact]
    public void FromTaskSchedule_NoStartWithDeadline_YieldsNonePlacement()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: null, deadlineUtc: Noon);

        var placement = CalendarTimelinePlacement.FromTaskSchedule(schedule);

        Assert.Equal(CalendarTimelinePlacementKind.None, placement.Kind);
        Assert.Null(placement.StartUtc);
        Assert.Null(placement.EndUtc);
    }

    [Fact]
    public void FromTaskSchedule_WithStart_YieldsTimelinePlacementDrivenByStartOnly()
    {
        var schedule = TaskSchedule.Create(startsAtUtc: TenAm, deadlineUtc: Noon);

        var placement = CalendarTimelinePlacement.FromTaskSchedule(schedule);

        Assert.Equal(CalendarTimelinePlacementKind.Timeline, placement.Kind);
        Assert.Equal(TenAm, placement.StartUtc);
        Assert.Null(placement.EndUtc);
    }

    [Fact]
    public void OverlapPolicy_DetectsRealOverlap()
    {
        var first = TimedEvent(TenAm, ElevenAm);
        var second = TimedEvent(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), Noon);

        var result = CalendarOverlapPolicy.Evaluate(first, second);

        Assert.True(result.HasOverlap);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), result.OverlapStartUtc);
        Assert.Equal(ElevenAm, result.OverlapEndUtc);
    }

    [Fact]
    public void OverlapPolicy_TouchingHalfOpenBoundaries_DoNotOverlap()
    {
        var first = TimedEvent(TenAm, ElevenAm);
        var second = TimedEvent(ElevenAm, Noon);

        var result = CalendarOverlapPolicy.Evaluate(first, second);

        Assert.False(result.HasOverlap);
    }

    [Fact]
    public void OverlapPolicy_NestedInterval_OverlapsAndReportsInnerBounds()
    {
        var outer = TimedEvent(TenAm, Noon);
        var inner = TimedEvent(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), ElevenAm);

        var result = CalendarOverlapPolicy.Evaluate(outer, inner);

        Assert.True(result.HasOverlap);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), result.OverlapStartUtc);
        Assert.Equal(ElevenAm, result.OverlapEndUtc);
    }

    [Fact]
    public void OverlapPolicy_DisjointIntervals_DoNotOverlap()
    {
        var first = TimedEvent(TenAm, ElevenAm);
        var second = TimedEvent(new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero));

        var result = CalendarOverlapPolicy.Evaluate(first, second);

        Assert.False(result.HasOverlap);
    }

    [Fact]
    public void OverlapPolicy_AllDayParticipant_DoesNotWarn()
    {
        var timed = TimedEvent(TenAm, ElevenAm);
        var allDay = AllDayEvent(new DateOnly(2026, 8, 17));

        var result = CalendarOverlapPolicy.Evaluate(timed, allDay);

        Assert.False(result.HasOverlap);
    }

    [Fact]
    public void OverlapPolicy_NoneParticipant_DoesNotWarn()
    {
        var timed = TimedEvent(TenAm, ElevenAm);
        var none = CalendarTimelinePlacement.FromTaskSchedule(TaskSchedule.Create(startsAtUtc: null, deadlineUtc: Noon));

        var result = CalendarOverlapPolicy.Evaluate(timed, none);

        Assert.False(result.HasOverlap);
    }

    [Fact]
    public void OverlapPolicy_DoesNotThrowForValidOverlaps()
    {
        var first = TimedEvent(TenAm, ElevenAm);
        var second = TimedEvent(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), Noon);

        var result = Record.Exception(() => CalendarOverlapPolicy.Evaluate(first, second));

        Assert.Null(result);
    }

    [Fact]
    public void OverlapPolicy_OpenEndedTaskInterval_OverlapsAnIntervalStartingAfterItsStart()
    {
        var task = CalendarTimelinePlacement.FromTaskSchedule(
            TaskSchedule.Create(startsAtUtc: TenAm, deadlineUtc: Noon));
        var eventPlacement = TimedEvent(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), ElevenAm);

        var result = CalendarOverlapPolicy.Evaluate(task, eventPlacement);

        Assert.True(result.HasOverlap);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), result.OverlapStartUtc);
        Assert.Equal(ElevenAm, result.OverlapEndUtc);
    }

    [Fact]
    public void FindOverlaps_IgnoresDateOnlyAndNonePlacements()
    {
        var placements = new[]
        {
            TimedEvent(TenAm, ElevenAm),
            TimedEvent(new DateTimeOffset(2026, 8, 17, 10, 30, 0, TimeSpan.Zero), Noon),
            AllDayEvent(new DateOnly(2026, 8, 18)),
            CalendarTimelinePlacement.None(),
        };

        var overlaps = CalendarOverlapPolicy.FindOverlaps(placements);

        var pair = Assert.Single(overlaps);
        Assert.Equal(TenAm, pair.First.StartUtc);
        Assert.Equal(Noon, pair.Second.EndUtc);
    }

    [Fact]
    public void FindOverlaps_EmptyCollection_YieldsEmptyResult()
    {
        Assert.Empty(CalendarOverlapPolicy.FindOverlaps(Array.Empty<CalendarTimelinePlacement>()));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void FindOverlaps_SetWithoutOverlap_YieldsNoPairs(int intervalCount, int expectedOverlaps)
    {
        var placements = Enumerable.Range(0, intervalCount)
            .Select(i => TimedEvent(TenAm.AddHours(i), ElevenAm.AddHours(i)))
            .ToArray();

        var overlaps = CalendarOverlapPolicy.FindOverlaps(placements);

        Assert.Equal(expectedOverlaps, overlaps.Count);
    }

    [Fact]
    public void FindOverlaps_ChainOfTouchingIntervals_HasNoOverlaps()
    {
        var placements = new[] { TimedEvent(TenAm, ElevenAm), TimedEvent(ElevenAm, Noon), TimedEvent(Noon, new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.Zero)) };

        Assert.Empty(CalendarOverlapPolicy.FindOverlaps(placements));
    }
}