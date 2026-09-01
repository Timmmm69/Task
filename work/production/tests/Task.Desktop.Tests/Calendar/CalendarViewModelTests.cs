using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Calendar;

public sealed class CalendarViewModelTests
{
    private static readonly DateOnly Monday = new(2026, 8, 31);

    [Fact]
    public void WeekRange_UsesLocalMidnightsAcrossDst()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test/DST", TimeSpan.FromHours(2), "Test", "Test",
            "Test DST", [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday))]);

        var range = CalendarViewModel.GetUtcRange(new DateOnly(2026, 3, 23), zone);

        Assert.Equal(DateTimeOffset.Parse("2026-03-22T22:00:00Z"), range.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-03-29T21:00:00Z"), range.ToUtc);
        Assert.Equal(TimeSpan.FromHours(167), range.ToUtc - range.FromUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Activate_MapsSevenDaysMixedItemsAndConflicts()
    {
        var client = new FakeCalendarClient();
        var eventId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        client.ScheduleResults.Enqueue(Schedule([
            Item(eventId, DesktopScheduleItemType.CalendarEvent, Monday, true),
            Item(taskId, DesktopScheduleItemType.Task, Monday.AddDays(1), false),
        ]));
        client.ConflictResults.Enqueue(new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([
            new(eventId, taskId, DateTimeOffset.Parse("2026-09-01T08:00:00Z"), DateTimeOffset.Parse("2026-09-01T09:00:00Z"), DesktopConflictSeverity.Blocking),
        ]));
        using var vm = new CalendarViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));

        await vm.ActivateAsync();

        Assert.Equal(CalendarScreenState.Loaded, vm.State);
        Assert.Equal(7, vm.Days.Count);
        Assert.Equal(2, vm.Days.Sum(day => day.Items.Count));
        Assert.All(vm.Days.SelectMany(day => day.Items), item => Assert.True(item.HasConflict));
        Assert.Equal("31 авг. — 6 сент. 2026", vm.WeekRangeText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task MissingCapability_FailsClosedWithoutRequest()
    {
        var client = new FakeCalendarClient();
        using var vm = new CalendarViewModel(client, [], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));

        await vm.ActivateAsync();

        Assert.Equal(CalendarScreenState.Forbidden, vm.State);
        Assert.Empty(vm.Days);
        Assert.Equal(0, client.ScheduleCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Revocation_CancelsAndClearsProtectedData()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([Item(Guid.NewGuid(), DesktopScheduleItemType.CalendarEvent, Monday, true)]));
        client.ConflictResults.Enqueue(Conflicts());
        using var vm = new CalendarViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();

        vm.UpdateCapabilities([]);

        Assert.Equal(CalendarScreenState.Forbidden, vm.State);
        Assert.Empty(vm.Days);
        Assert.Null(vm.SelectedEvent);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshFailure_PreservesConfirmedDataWithHonestBanner()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([Item(Guid.NewGuid(), DesktopScheduleItemType.Task, Monday, false)]));
        client.ConflictResults.Enqueue(Conflicts());
        client.ScheduleResults.Enqueue(new DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable());
        client.ConflictResults.Enqueue(new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.ServerUnavailable());
        using var vm = new CalendarViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();

        await vm.RefreshCommand.ExecuteAsync();

        Assert.Equal(CalendarScreenState.Error, vm.State);
        Assert.Single(vm.Days.SelectMany(day => day.Items));
        Assert.Contains("последние подтверждённые", vm.Message);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Navigation_LoadsAdjacentWeek()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([])); client.ConflictResults.Enqueue(Conflicts());
        client.ScheduleResults.Enqueue(Schedule([], Monday.AddDays(7))); client.ConflictResults.Enqueue(Conflicts());
        using var vm = new CalendarViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();

        await vm.NextWeekCommand.ExecuteAsync();

        Assert.Equal(Monday.AddDays(7), vm.WeekStart);
        Assert.Equal(DateTimeOffset.Parse("2026-09-07T00:00:00Z"), client.ScheduleRanges[1].From);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EventSelection_LoadsDetails_TaskSelectionDoesNot()
    {
        var client = new FakeCalendarClient(); var eventId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        client.ScheduleResults.Enqueue(Schedule([Item(eventId, DesktopScheduleItemType.CalendarEvent, Monday, true), Item(taskId, DesktopScheduleItemType.Task, Monday, false)]));
        client.ConflictResults.Enqueue(Conflicts()); client.EventResults.Enqueue(Event(eventId));
        using var vm = new CalendarViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();

        vm.SelectedItem = vm.Days[0].Items.Single(item => item.IsCalendarEvent);
        await SpinUntilAsync(() => vm.SelectedEvent is not null);
        vm.SelectedItem = vm.Days[0].Items.Single(item => !item.IsCalendarEvent);

        Assert.Equal(1, client.EventCalls);
        Assert.Null(vm.SelectedEvent);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CreateAndUpdate_UseEditorAndReloadSchedule()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([])); client.ConflictResults.Enqueue(Conflicts());
        client.CreateResults.Enqueue(Event(Guid.NewGuid()));
        client.ScheduleResults.Enqueue(Schedule([])); client.ConflictResults.Enqueue(Conflicts());
        using var vm = new CalendarViewModel(client, ["Calendar.Read", "CalendarEvent.Create"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync();
        vm.Editor!.Title = "Планёрка"; vm.Editor.Date = Monday.ToDateTime(TimeOnly.MinValue); vm.Editor.IsAllDay = true;

        await vm.SaveEventCommand.ExecuteAsync();

        Assert.Equal(1, client.CreateCalls);
        Assert.Null(vm.Editor);
        Assert.Contains("сохранено", vm.Announcement);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SessionEnd_ClearsDataAndDisablesCommands()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([Item(Guid.NewGuid(), DesktopScheduleItemType.Task, Monday, false)])); client.ConflictResults.Enqueue(Conflicts());
        using var vm = new CalendarViewModel(client, ["Calendar.Read", "CalendarEvent.Create"], TimeZoneInfo.Utc, Monday.ToDateTime(TimeOnly.MinValue));
        await vm.ActivateAsync();

        vm.UpdateSessionState(false);

        Assert.Equal(CalendarScreenState.SessionEnded, vm.State);
        Assert.Empty(vm.Days);
        Assert.False(vm.NewEventCommand.CanExecute(null));
    }

    private static DesktopCalendarResult<DesktopSchedulePage> Schedule(IReadOnlyList<DesktopScheduleItem> items, DateOnly? start = null)
    {
        var from = new DateTimeOffset((start ?? Monday).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new(items, from, from.AddDays(7)));
    }
    private static DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>> Conflicts() => new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([]);
    private static DesktopScheduleItem Item(Guid id, DesktopScheduleItemType type, DateOnly date, bool allDay) => new(
        id, type, type == DesktopScheduleItemType.Task ? "Задача" : "Событие", date,
        allDay ? null : new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
        allDay ? null : new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
        allDay, null, type == DesktopScheduleItemType.Task ? "new" : "scheduled",
        type == DesktopScheduleItemType.Task ? DesktopCalendarPriority.Normal : null);
    private static DesktopCalendarResult<DesktopCalendarEvent> Event(Guid id) => new DesktopCalendarResult<DesktopCalendarEvent>.Succeeded(new(
        id, Guid.NewGuid(), 3, DateTimeOffset.Parse("2026-08-01T00:00:00Z"), DateTimeOffset.Parse("2026-08-02T00:00:00Z"), null,
        "Событие", "Описание", Monday, true, null, null, "UTC", "scheduled", [], "\"v3\""));
    private static async global::System.Threading.Tasks.Task SpinUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await global::System.Threading.Tasks.Task.Delay(5);
        Assert.True(condition());
    }

    private sealed class FakeCalendarClient : IDesktopCalendarApiClient
    {
        public Queue<DesktopCalendarResult<DesktopSchedulePage>> ScheduleResults { get; } = new();
        public Queue<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> ConflictResults { get; } = new();
        public Queue<DesktopCalendarResult<DesktopCalendarEvent>> EventResults { get; } = new();
        public Queue<DesktopCalendarResult<DesktopCalendarEvent>> CreateResults { get; } = new();
        public int ScheduleCalls { get; private set; }
        public int EventCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public List<(DateTimeOffset From, DateTimeOffset To)> ScheduleRanges { get; } = [];
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); ScheduleCalls++; ScheduleRanges.Add((fromUtc, toUtc)); return global::System.Threading.Tasks.Task.FromResult(ScheduleResults.Dequeue()); }
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return global::System.Threading.Tasks.Task.FromResult(ConflictResults.Dequeue()); }
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid eventId, CancellationToken cancellationToken)
        { EventCalls++; return global::System.Threading.Tasks.Task.FromResult(EventResults.Dequeue()); }
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand command, CancellationToken cancellationToken)
        { CreateCalls++; return global::System.Threading.Tasks.Task.FromResult(CreateResults.Dequeue()); }
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
