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
