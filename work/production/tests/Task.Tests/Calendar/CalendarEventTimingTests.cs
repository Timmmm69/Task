using Task.Domain.Calendar;

namespace Task.Tests.Calendar;

public sealed class CalendarEventTimingTests
{
    private const string TimeZoneId = "Europe/Berlin";

    private static readonly DateTimeOffset UtcNoon = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UtcOnePm = new(2026, 8, 17, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAllDay_AcceptsNullInstants()
    {
        var timing = CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), TimeZoneId);

        Assert.True(timing.IsAllDay);
        Assert.Null(timing.StartAtUtc);
        Assert.Null(timing.EndAtUtc);
        Assert.Equal(new DateOnly(2026, 8, 17), timing.EventDate);
        Assert.Equal(TimeZoneId, timing.TimeZoneId);
    }

    [Fact]
    public void CreateAllDay_TrimsTimeZone()
    {
        var timing = CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), $"  {TimeZoneId}  ");

        Assert.Equal(TimeZoneId, timing.TimeZoneId);
    }

    [Fact]
    public void CreateAllDay_RejectsEmptyTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), "   "));
    }

    [Fact]
    public void CreateAllDay_RejectsUnknownTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), "Mars/Olympus"));
    }

    [Fact]
    public void CreateAllDay_RejectsTooLongTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), new string('x', 65)));
    }

    [Fact]
    public void CreateAllDay_RejectsNullTimeZone()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 17), null!));
    }

    [Fact]
    public void CreateTimed_AcceptsBothUtcInstantsAndLocalEventDate()
    {
        var timing = CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, TimeZoneId);

        Assert.False(timing.IsAllDay);
        Assert.Equal(UtcNoon, timing.StartAtUtc);
        Assert.Equal(UtcOnePm, timing.EndAtUtc);
        Assert.Equal(new DateOnly(2026, 8, 17), timing.EventDate);
    }

    [Fact]
    public void CreateTimed_TrimsTimeZone()
    {
        var timing = CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, $"  {TimeZoneId}  ");

        Assert.Equal(TimeZoneId, timing.TimeZoneId);
    }

    [Fact]
    public void CreateTimed_ConvertsLocalDateAcrossUtcDateBoundary()
    {
        // 2026-08-16T23:30:00Z is 2026-08-17T01:30 local in Europe/Berlin (UTC+2).
        var start = new DateTimeOffset(2026, 8, 16, 23, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 8, 17, 1, 30, 0, TimeSpan.Zero);

        var timing = CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), start, end, TimeZoneId);

        Assert.Equal(new DateOnly(2026, 8, 17), timing.EventDate);
    }

    [Fact]
    public void CreateTimed_EventDateMustMatchLocalStartDate()
    {
        // 2026-08-16T23:30:00Z is 2026-08-17 local, so 2026-08-16 is wrong.
        var start = new DateTimeOffset(2026, 8, 16, 23, 30, 0, TimeSpan.Zero);

        var exception = Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 16), start, start.AddHours(2), TimeZoneId));

        Assert.Equal("eventDate", exception.ParamName);
    }

    [Fact]
    public void CreateTimed_RejectsEndBeforeStart()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcOnePm, UtcNoon, TimeZoneId));
    }

    [Fact]
    public void CreateTimed_RejectsEndEqualToStart()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcNoon, TimeZoneId));
    }

    [Fact]
    public void CreateTimed_RejectsNonUtcStart()
    {
        var nonUtcStart = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), nonUtcStart, nonUtcStart.AddHours(1), TimeZoneId));
    }

    [Fact]
    public void CreateTimed_RejectsNonUtcEnd()
    {
        var nonUtcEnd = new DateTimeOffset(2026, 8, 17, 13, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, nonUtcEnd, TimeZoneId));
    }

    [Fact]
    public void CreateTimed_RejectsEmptyTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, " "));
    }

    [Fact]
    public void CreateTimed_RejectsUnknownTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, "Unknown/Zone"));
    }

    [Fact]
    public void CreateTimed_RejectsTooLongTimeZone()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, new string('x', 65)));
    }

    [Fact]
    public void CreateTimed_RejectsNullTimeZone()
    {
        Assert.Throws<ArgumentNullException>(
            () => CalendarEventTiming.CreateTimed(new DateOnly(2026, 8, 17), UtcNoon, UtcOnePm, null!));
    }

    [Fact]
    public void Create_AllDayWithStartInstant_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.Create(new DateOnly(2026, 8, 17), isAllDay: true, UtcNoon, endAtUtc: null, TimeZoneId));
    }

    [Fact]
    public void Create_AllDayWithEndInstant_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.Create(new DateOnly(2026, 8, 17), isAllDay: true, startAtUtc: null, UtcOnePm, TimeZoneId));
    }

    [Fact]
    public void Create_AllDayWithBothInstants_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.Create(new DateOnly(2026, 8, 17), isAllDay: true, UtcNoon, UtcOnePm, TimeZoneId));
    }

    [Fact]
    public void Create_TimedWithoutStart_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.Create(new DateOnly(2026, 8, 17), isAllDay: false, startAtUtc: null, UtcOnePm, TimeZoneId));
    }

    [Fact]
    public void Create_TimedWithoutEnd_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => CalendarEventTiming.Create(new DateOnly(2026, 8, 17), isAllDay: false, UtcNoon, endAtUtc: null, TimeZoneId));
    }
}