using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Today;

public sealed class TodayViewModelTests
{
    private static readonly DateOnly Today = new(2026, 8, 31);

    [Fact]
    public async global::System.Threading.Tasks.Task
        Activate_LoadsTimedAndUntimedItemsForCurrentDay()
    {
        var client = new FakeCalendarClient();

        client.ScheduleResults.Enqueue(Schedule([
            TimedTask(Today, "Планёрка"),
            UntimedTask(Today, "Подготовить отчёт"),
            AllDayEvent(Today, "Выставка"),
            TimedTask(Today.AddDays(1), "Завтра"),
        ]));

        using var vm = Create(client);

        await vm.ActivateAsync();

        Assert.Equal(TodayScreenState.Loaded, vm.State);
        Assert.Equal("понедельник, 31 августа 2026", vm.DateText);

        Assert.Single(vm.TimedItems);
        Assert.Equal("Планёрка", vm.TimedItems[0].Title);

        Assert.Equal(2, vm.UntimedItems.Count);
        Assert.DoesNotContain(
            vm.TimedItems.Concat(vm.UntimedItems),
            item => item.Title == "Завтра");

        Assert.Single(client.ScheduleRanges);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-31T00:00:00Z"),
            client.ScheduleRanges[0].From);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            client.ScheduleRanges[0].To);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task
        MissingCapability_FailsClosedWithoutRequest()
    {
        var client = new FakeCalendarClient();

        using var vm = new TodayViewModel(
            client,
            [],
            TimeZoneInfo.Utc,
            Clock);

        await vm.ActivateAsync();

        Assert.Equal(TodayScreenState.Forbidden, vm.State);
        Assert.False(vm.HasItems);
        Assert.Equal(0, client.ScheduleCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task
        CapabilityRevocation_CancelsAndClearsProtectedData()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(
            Schedule([TimedTask(Today, "Секретная задача")]));

        using var vm = Create(client);
        await vm.ActivateAsync();

        Assert.True(vm.HasItems);

        vm.UpdateCapabilities([]);

        Assert.Equal(TodayScreenState.Forbidden, vm.State);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task
        SessionEnd_ClearsProtectedData()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(
            Schedule([TimedTask(Today, "Рабочая задача")]));

        using var vm = Create(client);
        await vm.ActivateAsync();

        vm.UpdateSessionState(false);

        Assert.Equal(TodayScreenState.SessionEnded, vm.State);
        Assert.False(vm.HasItems);
        Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task
        RefreshFailure_PreservesLastConfirmedData()
    {
        var client = new FakeCalendarClient();

        client.ScheduleResults.Enqueue(
            Schedule([TimedTask(Today, "Подтверждённая задача")]));

        client.ScheduleResults.Enqueue(
            new DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable());

        using var vm = Create(client);
        await vm.ActivateAsync();

        await vm.RefreshCommand.ExecuteAsync();

        Assert.Equal(TodayScreenState.Error, vm.State);
        Assert.Single(vm.TimedItems);
        Assert.Equal(
            "Подтверждённая задача",
            vm.TimedItems[0].Title);
        Assert.Contains(
            "последние подтверждённые",
            vm.Message);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task
        DayRange_UsesLocalMidnightsAcrossDst()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test/DST",
            TimeSpan.FromHours(2),
            "Test",
            "Test",
            "Test DST",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(2026, 1, 1),
                    new DateTime(2026, 12, 31),
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                        new DateTime(1, 1, 1, 2, 0, 0),
                        3,
                        5,
                        DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                        new DateTime(1, 1, 1, 3, 0, 0),
                        10,
                        5,
                        DayOfWeek.Sunday))
            ]);

        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([]));

        using var vm = new TodayViewModel(
            client,
            ["Calendar.Read"],
            zone,
            () => DateTimeOffset.Parse("2026-03-29T12:00:00Z"));

        await vm.ActivateAsync();

        Assert.Single(client.ScheduleRanges);

        var range = client.ScheduleRanges[0];

        Assert.Equal(
            DateTimeOffset.Parse("2026-03-28T22:00:00Z"),
            range.From);
        Assert.Equal(
            DateTimeOffset.Parse("2026-03-29T21:00:00Z"),
            range.To);
        Assert.Equal(
            TimeSpan.FromHours(23),
            range.To - range.From);
    }

    [Theory]
    [InlineData("navigation")]
    [InlineData("capability")]
    [InlineData("session")]
    [InlineData("dispose")]
    public async global::System.Threading.Tasks.Task LateResponse_CannotRestoreStaleData(string action)
    {
        var pending = new TaskCompletionSource<DesktopCalendarResult<DesktopSchedulePage>>();
        var client = new FakeCalendarClient { PendingSchedule = pending.Task };
        using var vm = Create(client);
        var load = vm.ActivateAsync();
        var token = client.LastToken;

        switch (action)
        {
            case "navigation":
                vm.Deactivate();
                client.PendingSchedule = null;
                client.ScheduleResults.Enqueue(Schedule([TimedTask(Today, "Актуальная запись")]));
                await vm.ActivateAsync();
                break;
            case "capability": vm.UpdateCapabilities([]); break;
            case "session": vm.UpdateSessionState(false); break;
            case "dispose": vm.Dispose(); break;
        }

        Assert.True(token.IsCancellationRequested);
        pending.SetResult(Schedule([TimedTask(Today, "Устаревшая запись")]));
        await load;

        Assert.DoesNotContain(vm.TimedItems, item => item.Title == "Устаревшая запись");
        if (action == "navigation")
        {
            Assert.Equal("Актуальная запись", Assert.Single(vm.TimedItems).Title);
            Assert.Equal(TodayScreenState.Loaded, vm.State);
        }
        else
        {
            Assert.False(vm.HasItems);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Shell_ActivatesTodayAndRoutesFooterRefresh()
    {
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([]));
        client.ScheduleResults.Enqueue(Schedule([TimedTask(Today, "План")]));
        using var vm = Create(client);
        using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, today: vm);

        Assert.True(shell.IsTodaySectionSelected);
        Assert.True(vm.IsActive);
        Assert.Equal(TodayScreenState.Empty, vm.State);
        Assert.Same(vm.RefreshCommand, shell.FooterRefreshCommand);
        await shell.FooterRefreshCommand!.ExecuteAsync();
        Assert.Single(vm.TimedItems);
        shell.SelectedSection = shell.Sections.Single(section => section.Route == "calendar");
        Assert.False(vm.IsActive);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NextDayRefresh_DoesNotKeepYesterdayOnFailure()
    {
        var now = Clock();
        var client = new FakeCalendarClient();
        client.ScheduleResults.Enqueue(Schedule([TimedTask(Today, "Вчерашняя запись")]));
        client.ScheduleResults.Enqueue(new DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable());
        using var vm = new TodayViewModel(client, ["Calendar.Read"], TimeZoneInfo.Utc, () => now);
        await vm.ActivateAsync();
        now = now.AddDays(1);
        await vm.RefreshCommand.ExecuteAsync();

        Assert.Equal(Today.AddDays(1), vm.Today);
        Assert.False(vm.HasItems);
        Assert.Equal(TodayScreenState.Error, vm.State);
    }

    private static TodayViewModel Create(FakeCalendarClient client) =>
        new(
            client,
            ["Calendar.Read"],
            TimeZoneInfo.Utc,
            Clock);

    private static DateTimeOffset Clock() =>
        DateTimeOffset.Parse("2026-08-31T12:00:00Z");

    private static DesktopCalendarResult<DesktopSchedulePage> Schedule(
        IReadOnlyList<DesktopScheduleItem> items)
    {
        var from = DateTimeOffset.Parse("2026-08-31T00:00:00Z");

        return new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(
            new DesktopSchedulePage(
                items,
                from,
                from.AddDays(1)));
    }

    private static DesktopScheduleItem TimedTask(
        DateOnly date,
        string title)
    {
        return new DesktopScheduleItem(
            Guid.NewGuid(),
            DesktopScheduleItemType.Task,
            title,
            date,
            new DateTimeOffset(
                date.ToDateTime(new TimeOnly(9, 0)),
                TimeSpan.Zero),
            new DateTimeOffset(
                date.ToDateTime(new TimeOnly(10, 0)),
                TimeSpan.Zero),
            false,
            null,
            "in_progress",
            DesktopCalendarPriority.High);
    }

    private static DesktopScheduleItem UntimedTask(
        DateOnly date,
        string title)
    {
        return new DesktopScheduleItem(
            Guid.NewGuid(),
            DesktopScheduleItemType.Task,
            title,
            date,
            null,
            null,
            false,
            null,
            "new",
            DesktopCalendarPriority.Normal);
    }

    private static DesktopScheduleItem AllDayEvent(
        DateOnly date,
        string title)
    {
        return new DesktopScheduleItem(
            Guid.NewGuid(),
            DesktopScheduleItemType.CalendarEvent,
            title,
            date,
            null,
            null,
            true,
            null,
            "scheduled",
            null);
    }

    private sealed class FakeCalendarClient : IDesktopCalendarApiClient
    {
        public Queue<DesktopCalendarResult<DesktopSchedulePage>>
            ScheduleResults
        { get; } = new();

        public List<(DateTimeOffset From, DateTimeOffset To)>
            ScheduleRanges
        { get; } = [];

        public int ScheduleCalls { get; private set; }

        public global::System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>>? PendingSchedule { get; set; }

        public CancellationToken LastToken { get; private set; }

        public global::System.Threading.Tasks.Task<
            DesktopCalendarResult<DesktopSchedulePage>>
            GetScheduleAsync(
                DateTimeOffset fromUtc,
                DateTimeOffset toUtc,
                string timeZoneId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScheduleCalls++;
            ScheduleRanges.Add((fromUtc, toUtc));
            LastToken = cancellationToken;

            if (PendingSchedule is not null) return PendingSchedule;

            return global::System.Threading.Tasks.Task.FromResult(
                ScheduleResults.Dequeue());
        }

        public global::System.Threading.Tasks.Task<
            DesktopCalendarResult<DesktopCalendarEvent>>
            GetEventAsync(
                Guid eventId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<
            DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>>
            GetConflictsAsync(
                DateTimeOffset fromUtc,
                DateTimeOffset toUtc,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<
            DesktopCalendarResult<DesktopCalendarEvent>>
            CreateEventAsync(
                DesktopCalendarEventCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<
            DesktopCalendarResult<DesktopCalendarEvent>>
            UpdateEventAsync(
                Guid eventId,
                long expectedVersion,
                DesktopCalendarEventCommand command,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
