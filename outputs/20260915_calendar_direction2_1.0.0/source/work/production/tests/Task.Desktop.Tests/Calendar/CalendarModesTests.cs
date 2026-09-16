using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Calendar;

public sealed class CalendarModesTests
{
    [Fact]
    public void Month_UsesMondayAlignedWholeWeeksIncludingAdjacentDates()
    {
        var range = CalendarViewModel.GetVisibleDateRange(new DateOnly(2024, 2, 15), CalendarViewMode.Month);

        Assert.Equal(new DateOnly(2024, 1, 29), range.FirstDate);
        Assert.Equal(new DateOnly(2024, 3, 4), range.LastDateExclusive);
        Assert.Equal(35, range.LastDateExclusive.DayNumber - range.FirstDate.DayNumber);
    }

    [Fact]
    public void UtcRange_UsesLocalMidnightsForLeapDay()
    {
        var range = CalendarViewModel.GetUtcRange(new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 1), TimeZoneInfo.Utc);

        Assert.Equal(DateTimeOffset.Parse("2024-02-29T00:00:00Z"), range.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2024-03-01T00:00:00Z"), range.ToUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task MonthNavigation_PreservesAnchorDayWherePossible()
    {
        using var vm = new CalendarViewModel(new EmptyCalendarClient(), ["Calendar.Read"], TimeZoneInfo.Utc, new DateTime(2024, 1, 31));
        await vm.ActivateAsync();
        await vm.MonthModeCommand.ExecuteAsync();
        await vm.NextWeekCommand.ExecuteAsync();

        Assert.Equal(new DateTime(2024, 2, 29), vm.SelectedDate);
        Assert.Equal(CalendarViewMode.Month, vm.ViewMode);
    }

    [Fact]
    public void TimedEntry_IsShownOnEveryOverlappedLocalDay()
    {
        var item = new CalendarItemViewModel(new DesktopScheduleItem(Guid.NewGuid(), DesktopScheduleItemType.CalendarEvent,
            "Ночная смена", new DateOnly(2024, 4, 1), DateTimeOffset.Parse("2024-04-01T22:00:00Z"),
            DateTimeOffset.Parse("2024-04-02T02:00:00Z"), false, null, "scheduled", null), null);

        Assert.True(item.AppearsOn(new DateOnly(2024, 4, 1), TimeZoneInfo.Utc));
        Assert.True(item.AppearsOn(new DateOnly(2024, 4, 2), TimeZoneInfo.Utc));
    }

    [Fact]
    public void WeekTimeline_AssignsReadableLanesAndCurrentMoment()
    {
        var date = new DateOnly(2026, 9, 15);
        CalendarItemViewModel Item(string title, string from, string to) => new(new DesktopScheduleItem(
            Guid.NewGuid(), DesktopScheduleItemType.CalendarEvent, title, date,
            DateTimeOffset.Parse(from), DateTimeOffset.Parse(to), false, null, "scheduled", null), null, TimeZoneInfo.Utc);
        var first = Item("Первая встреча", "2026-09-15T10:00:00Z", "2026-09-15T11:00:00Z");
        var second = Item("Пересекающаяся встреча", "2026-09-15T10:30:00Z", "2026-09-15T11:30:00Z");
        var third = Item("После встреч", "2026-09-15T12:00:00Z", "2026-09-15T12:30:00Z");

        var day = new CalendarDayViewModel(date, [first, second, third], true, TimeZoneInfo.Utc,
            DateTimeOffset.Parse("2026-09-15T10:45:00Z"));

        Assert.Equal(2, day.TimelineItems[0].TimelineLaneCount);
        Assert.Equal(2, day.TimelineItems[1].TimelineLaneCount);
        Assert.NotEqual(day.TimelineItems[0].TimelineLane, day.TimelineItems[1].TimelineLane);
        Assert.Equal(1, day.TimelineItems[2].TimelineLaneCount);
        Assert.True(day.ShowCurrentTime);
        Assert.True(day.IsToday);
        Assert.Equal(10.75d * 69d, day.CurrentTimeTop, 3);
    }

    [Fact]
    public void ShortSequentialEntries_UseSeparateLanesWhenMinimumCardHeightsWouldTouch()
    {
        var date = new DateOnly(2026, 9, 15);
        CalendarItemViewModel Item(string title, string from, string to) => new(new DesktopScheduleItem(
            Guid.NewGuid(), DesktopScheduleItemType.CalendarEvent, title, date,
            DateTimeOffset.Parse(from), DateTimeOffset.Parse(to), false, null, "scheduled", null), null, TimeZoneInfo.Utc);
        var first = Item("Пятнадцать минут", "2026-09-15T10:00:00Z", "2026-09-15T10:15:00Z");
        var second = Item("Следующая встреча", "2026-09-15T10:20:00Z", "2026-09-15T10:35:00Z");

        var day = new CalendarDayViewModel(date, [first, second], true, TimeZoneInfo.Utc,
            DateTimeOffset.Parse("2026-09-15T10:00:00Z"));

        Assert.Equal(2, day.TimelineItems[0].TimelineLaneCount);
        Assert.NotEqual(day.TimelineItems[0].TimelineLane, day.TimelineItems[1].TimelineLane);
        Assert.True(day.TimelineItems[0].TimelineTop + day.TimelineItems[0].TimelineHeight > day.TimelineItems[1].TimelineTop);
    }

    [Fact]
    public void CurrentMoment_AdvancesAndLeavesYesterdayAfterMidnight()
    {
        var date = new DateOnly(2026, 9, 15);
        var day = new CalendarDayViewModel(date, [], true, TimeZoneInfo.Utc,
            DateTimeOffset.Parse("2026-09-15T23:59:00Z"));

        Assert.True(day.IsToday);
        Assert.Equal("23:59", day.CurrentTimeText);
        day.UpdateCurrentTime(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));

        Assert.False(day.IsToday);
        Assert.False(day.ShowCurrentTime);
        Assert.Equal("00:00", day.CurrentTimeText);
        Assert.Equal(0, day.CurrentTimeTop);
    }

    private sealed class EmptyCalendarClient : IDesktopCalendarApiClient
    {
        private static readonly DesktopCalendarResult<DesktopSchedulePage> Schedule = new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(
            new([], DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        private static readonly DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>> Conflicts = new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([]);
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken) => global::System.Threading.Tasks.Task.FromResult(Schedule);
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => global::System.Threading.Tasks.Task.FromResult(Conflicts);
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid eventId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
