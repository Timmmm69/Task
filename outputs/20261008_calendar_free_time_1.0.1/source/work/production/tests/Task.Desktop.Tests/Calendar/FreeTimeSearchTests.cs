using System.Diagnostics;
using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Calendar;

public sealed class FreeTimeSearchTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DesktopUserSettings Settings = new(1, "ru", "24h", 1, "09:00", "18:00", [6, 7], 30, 10, false, true, true, "ask");
    private static DateTimeOffset At(int hour, int minute = 0) => new(Monday.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);
    private static DesktopScheduleItem Busy(int fromHour, int toHour, int fromMinute = 0, int toMinute = 0,
        string status = "scheduled", DesktopScheduleItemType type = DesktopScheduleItemType.CalendarEvent) =>
        new(Guid.NewGuid(), type, "Занято", Monday, At(fromHour, fromMinute), At(toHour, toMinute), false, null, status, null);
    private static IReadOnlyList<FreeTimeSlot> Find(IReadOnlyList<DesktopScheduleItem> items, DateTimeOffset? now = null, int minutes = 30) =>
        FreeTimeCalculator.Find(items, Settings, TimeZoneInfo.Utc, Monday, Monday.AddDays(1), now ?? At(8), minutes);

    [Theory]
    [InlineData(15)] [InlineData(30)] [InlineData(60)]
    public void EmptyDay_ReturnsFirstThreeNonOverlappingSlots(int minutes)
    {
        var slots = Find([], minutes: minutes);
        Assert.Equal(3, slots.Count); Assert.Equal(At(9), slots[0].StartUtc);
        Assert.All(slots, s => Assert.Equal(minutes, s.DurationMinutes));
        Assert.Equal(slots[0].EndUtc, slots[1].StartUtc);
    }

    [Fact] public void FullyBusyDay_HasNoSlots() => Assert.Empty(Find([Busy(9, 18)]));
    [Theory] [InlineData(30, 1)] [InlineData(29, 0)]
    public void ExactGap_RequiresFullDuration(int gap, int expected)
    {
        var slots = Find([Busy(9, 12), Busy(12, 18, fromMinute: gap)]);
        Assert.Equal(expected, slots.Count);
        if (expected == 1) Assert.Equal(At(12), slots[0].StartUtc);
    }

    [Fact]
    public void UnsortedOverlappingAdjacentAndNestedItems_FormOneBusyUnion()
    {
        var slots = Find([Busy(11, 12), Busy(9, 10), Busy(10, 11), Busy(9, 11, 30, 30), Busy(10, 10, 5, 20)]);
        Assert.Equal(At(12), slots[0].StartUtc);
    }
    [Fact]
    public void ItemsOutsideAndAcrossWorkBoundaries_AreClipped()
    {
        var slots = Find([Busy(7, 8), Busy(8, 10), Busy(17, 20), Busy(20, 21)]);
        Assert.Equal(At(10), slots[0].StartUtc);
        Assert.Empty(Find([Busy(8, 20)]));
    }
    [Fact]
    public void PastTimeToday_IsRoundedForwardToMinutePrecision()
    {
        var slots = Find([], At(14, 12).AddSeconds(3));
        Assert.Equal(At(14, 13), slots[0].StartUtc);
        Assert.Empty(Find([], At(17, 31)));
    }
    [Theory] [InlineData(4, 18)] [InlineData(5, 10)] [InlineData(6, 10)]
    public void NextWorkingDay_SkipsConfiguredWeekends(int days, int hour)
    {
        var slots = FreeTimeCalculator.Find([], Settings, TimeZoneInfo.Utc, Monday.AddDays(days), Monday.AddDays(11), At(hour).AddDays(days), 30);
        Assert.Equal(Monday.AddDays(7), slots[0].Date); Assert.Equal(At(9).AddDays(7), slots[0].StartUtc);
    }
    [Fact]
    public void DateOnlyCancelledAndPointItems_DoNotBlockTime()
    {
        var dateOnly = Busy(9, 18, type: DesktopScheduleItemType.Task) with { StartAtUtc = null, EndAtUtc = null, IsAllDay = true };
        var point = Busy(9, 18, type: DesktopScheduleItemType.Task) with { EndAtUtc = null };
        Assert.Equal(At(9), Find([dateOnly, point, Busy(9, 18, status: "cancelled")])[0].StartUtc);
    }
    [Fact]
    public void CompletedStatus_PreservesModeSpecificCalendarSemantics()
    {
        var done = Busy(9, 18, status: "completed", type: DesktopScheduleItemType.Task);
        Assert.Empty(Find([done]));
        Assert.Equal(3, FreeTimeCalculator.Find([done], Settings, TimeZoneInfo.Utc, Monday, Monday.AddDays(1), At(8), 30, personal: true).Count);
    }

    [Theory]
    [InlineData(2026, 3, 8, 2)] [InlineData(2026, 11, 1, 3)]
    public void DstTransitions_UseElapsedUtcDurationAndOffsets(int year, int month, int day, int count)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var date = new DateOnly(year, month, day);
        var settings = Settings with { WorkdayStart = "01:00", WorkdayEnd = month == 3 ? "04:00" : "03:00", WeekendDays = [6] };
        var now = FreeTimeCalculator.Boundary(date, TimeOnly.MinValue, zone);
        var slots = FreeTimeCalculator.Find([], settings, zone, date, date.AddDays(1), now, 60);
        Assert.Equal(count, slots.Count); Assert.All(slots, s => Assert.Equal(TimeSpan.FromHours(1), s.EndUtc - s.StartUtc));
        Assert.Equal(month == 3 ? 3 : 1, TimeZoneInfo.ConvertTime(slots[0].EndUtc, zone).Hour);
        if (month == 11) Assert.NotEqual(TimeZoneInfo.ConvertTime(slots[0].StartUtc, zone).Offset, TimeZoneInfo.ConvertTime(slots[1].StartUtc, zone).Offset);
    }
    [Fact]
    public void DstInvalidWorkBoundary_MovesToFirstExistingMinute()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); var date = new DateOnly(2026, 3, 8);
        var result = FreeTimeCalculator.Boundary(date, new(2, 30), zone);
        Assert.Equal(3, TimeZoneInfo.ConvertTime(result, zone).Hour);
        Assert.Equal(0, TimeZoneInfo.ConvertTime(result, zone).Minute);
    }
    [Fact]
    public void DstAmbiguousWorkEnd_IncludesBothOccurrences()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); var date = new DateOnly(2026, 11, 1);
        Assert.Equal(TimeSpan.FromHours(1), FreeTimeCalculator.Boundary(date, new(1, 30), zone, true) - FreeTimeCalculator.Boundary(date, new(1, 30), zone));
    }
    [Fact]
    public void DifferentMachineZone_UsesExplicitCalendarZone()
    {
        var offset = TimeZoneInfo.Local.BaseUtcOffset == TimeSpan.FromHours(11) ? TimeSpan.FromHours(-7) : TimeSpan.FromHours(11);
        var zone = TimeZoneInfo.CreateCustomTimeZone("CalendarTest", offset, "CalendarTest", "CalendarTest");
        var start = new DateTimeOffset(Monday.ToDateTime(new(9, 0)), offset).ToUniversalTime();
        var slots = FreeTimeCalculator.Find([], Settings, zone, Monday, Monday.AddDays(1), start.AddHours(-1), 30);
        Assert.Equal(start, slots[0].StartUtc); Assert.Equal(Monday, slots[0].Date);
    }

    [Fact]
    public async System.Threading.Tasks.Task OfflineCompleteCache_AllowsStaleResultsAndNoNetworkCalls()
    {
        var client = new Client(); var search = new FreeTimeSearch(client, TimeZoneInfo.Utc);
        search.Remember(new([], At(0), At(0).AddDays(7)));
        var result = await search.SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, false, default);
        Assert.True(result.Stale); Assert.Null(result.Error); Assert.Equal(3, result.Slots.Count); Assert.Empty(client.Ranges);
    }
    [Fact]
    public async System.Threading.Tasks.Task OfflineIncompleteCache_NeverInventsSlots()
    {
        var client = new Client(); var search = new FreeTimeSearch(client, TimeZoneInfo.Utc);
        search.Remember(new([], At(0), At(0).AddDays(6)));
        var result = await search.SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, false, default);
        Assert.True(result.Stale); Assert.NotNull(result.Error); Assert.Empty(result.Slots);
    }
    [Fact]
    public async System.Threading.Tasks.Task ServerUnavailable_FallsBackOnlyToCompleteCoverage()
    {
        var client = new Client { Read = (_, _) => new DesktopCalendarResult<DesktopSchedulePage>.ServerUnavailable() };
        var search = new FreeTimeSearch(client, TimeZoneInfo.Utc);
        search.Remember(new([], At(0), At(0).AddDays(3))); search.Remember(new([], At(0).AddDays(3), At(0).AddDays(7)));
        var result = await search.SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, true, default);
        Assert.True(result.Stale); Assert.Null(result.Error); Assert.Equal(3, result.Slots.Count);
    }
    [Fact]
    public async System.Threading.Tasks.Task RangeError_IsSplitIntoBoundedRequests()
    {
        var client = new Client { Read = (from, to) => to - from > TimeSpan.FromDays(1)
            ? new DesktopCalendarResult<DesktopSchedulePage>.RangeTooLarge() : new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], from, to)) };
        var result = await new FreeTimeSearch(client, TimeZoneInfo.Utc).SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, true, default);
        Assert.Null(result.Error); Assert.Equal(3, result.Slots.Count); Assert.InRange(client.Ranges.Count, 2, 64);
        Assert.All(client.Ranges, r => Assert.True(r.To - r.From <= TimeSpan.FromDays(7)));
    }
    [Fact]
    public async System.Threading.Tasks.Task UnsplittableRangeAndInvalidCoverage_ReturnNoSlots()
    {
        var client = new Client { Read = (_, _) => new DesktopCalendarResult<DesktopSchedulePage>.RangeTooLarge() };
        var result = await new FreeTimeSearch(client, TimeZoneInfo.Utc).SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, true, default);
        Assert.NotNull(result.Error); Assert.Empty(result.Slots);
        client.Read = (from, to) => new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], from, to.AddMinutes(-1)));
        result = await new FreeTimeSearch(client, TimeZoneInfo.Utc).SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, true, default);
        Assert.IsType<DesktopCalendarResult<DesktopSchedulePage>.MalformedResponse>(result.Failure); Assert.Empty(result.Slots);
    }
    [Fact]
    public async System.Threading.Tasks.Task Forbidden_DoesNotFallBackToCachedPermissions()
    {
        var client = new Client { Read = (_, _) => new DesktopCalendarResult<DesktopSchedulePage>.Forbidden() };
        var search = new FreeTimeSearch(client, TimeZoneInfo.Utc); search.Remember(new([], At(0), At(0).AddDays(7)));
        var result = await search.SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, true, default);
        Assert.Empty(result.Slots); Assert.IsType<DesktopCalendarResult<DesktopSchedulePage>.Forbidden>(result.Failure);
    }
    [Fact]
    public async System.Threading.Tasks.Task FreshOverlappingPage_DiscardsOldCoverageRatherThanMixingVersions()
    {
        var search = new FreeTimeSearch(new Client(), TimeZoneInfo.Utc);
        search.Remember(new([], At(0), At(0).AddDays(7)));
        search.Remember(new([Busy(9, 18)], At(0), At(0).AddDays(1)));
        var result = await search.SearchAsync(Settings, Monday, Monday.AddDays(7), At(8), 30, false, default);
        Assert.Empty(result.Slots); Assert.NotNull(result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task CalendarSelection_NavigatesHighlightsAndOnlyOffersDraft()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync();
        Assert.Equal(30, vm.FreeTimeMinutes); Assert.Equal(3, vm.FreeTimeSlots.Count);
        FreeTimeSlot? chosen = null; vm.FreeTimeChosen += slot => chosen = slot;
        var drafts = 0; vm.CreateTaskAtFreeTime = (_, _) => { drafts++; return System.Threading.Tasks.Task.CompletedTask; };
        await vm.ChooseFreeTimeCommand.ExecuteAsync(vm.FreeTimeSlots[0]);
        Assert.NotNull(chosen); Assert.Equal(CalendarViewMode.Day, vm.ViewMode); Assert.Equal(Monday.ToDateTime(TimeOnly.MinValue), vm.SelectedDate);
        Assert.True(vm.ShowFreeTimeHighlight); Assert.False(vm.ShowFreeTime); Assert.Equal(0, drafts); Assert.Equal(0, client.Writes);
        await vm.CreateTaskAtFreeTimeCommand.ExecuteAsync(); Assert.Equal(1, drafts); Assert.Equal(0, client.Writes);
    }
    [Fact]
    public async System.Threading.Tasks.Task CalendarOfflineCompleteCache_DisablesAllWrites()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync(); vm.UpdateConnectivity(false);
        await vm.FindTimeCommand.ExecuteAsync(); Assert.Equal(3, vm.FreeTimeSlots.Count); Assert.Contains("неактуальны", vm.FreeTimeMessage);
        await vm.ChooseFreeTimeCommand.ExecuteAsync(vm.FreeTimeSlots[0]);
        Assert.False(vm.NewEventCommand.CanExecute(null)); Assert.False(vm.CreateTaskAtFreeTimeCommand.CanExecute(null)); Assert.False(vm.SaveEventCommand.CanExecute(null));
    }
    [Fact]
    public async System.Threading.Tasks.Task CalendarOfflineIncompleteCache_ExplainsMissingData()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync();
        // A fresh shorter page deliberately invalidates the old overlapping cached range.
        client.Read = (from, to) => new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], from, to));
        await vm.DayModeCommand.ExecuteAsync(); vm.UpdateConnectivity(false);
        await vm.FindTimeCommand.ExecuteAsync(); Assert.Empty(vm.FreeTimeSlots); Assert.Contains("Недостаточно", vm.FreeTimeMessage);
    }
    [Fact]
    public async System.Threading.Tasks.Task CalendarWithoutRealSettings_DoesNotUseInventedDefaults()
    {
        using var vm = new CalendarViewModel(new Client(), ["Calendar.Read"], TimeZoneInfo.Utc, clock: () => At(8));
        await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync();
        Assert.Empty(vm.FreeTimeSlots); Assert.Contains("настроек", vm.FreeTimeMessage);
    }
    [Fact]
    public async System.Threading.Tasks.Task ShowFurther_RequestsOnlyTheNextWeek()
    {
        var client = new Client { Read = (from, to) => new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new(
            [Busy(0, 23) with { StartAtUtc = from, EndAtUtc = to }], from, to)) };
        using var vm = Vm(client); await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync();
        Assert.Empty(vm.FreeTimeSlots); Assert.True(vm.MoreFreeTimeCommand.CanExecute(null));
        await vm.MoreFreeTimeCommand.ExecuteAsync(); var last = client.Ranges.Last();
        Assert.Equal(At(0).AddDays(7), last.From); Assert.Equal(At(0).AddDays(14), last.To);
    }
    [Fact]
    public async System.Threading.Tasks.Task SessionLoss_CancelsLateSearchAndClearsResults()
    {
        var client = new Client(); using var vm = Vm(client); await vm.ActivateAsync();
        var pending = new TaskCompletionSource<DesktopCalendarResult<DesktopSchedulePage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Pending = pending.Task;
        var task = vm.FindTimeCommand.ExecuteAsync(); vm.UpdateSessionState(false);
        pending.SetResult(new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], At(0), At(0).AddDays(7)))); await task;
        Assert.Empty(vm.FreeTimeSlots); Assert.False(vm.FindTimeCommand.CanExecute(null)); Assert.False(vm.ShowFreeTime);
    }
    [Theory] [InlineData(0)] [InlineData(1)]
    public void OrdinaryCreateDraft_KeepsExactDstInstantAndDuration(int repeatedHour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var start = DateTimeOffset.Parse("2026-11-01T05:00:00Z").AddHours(repeatedHour);
        var editor = new TaskEditorViewModel(TaskEditorMode.Create) { Title = "Встреча" };
        editor.SeedCalendarSlot(new(start, start.AddMinutes(30), zone));
        var command = editor.BuildCreateCommand();
        Assert.NotNull(command); Assert.Equal(start, command!.StartAtUtc); Assert.Equal(30, command.Card!.PlannedDurationMinutes);
        command.Card.Validate(command.StartAtUtc);
    }
    [Fact]
    public void LargeCalendar_IsSortPlusScan()
    {
        var items = Enumerable.Range(0, 100_000).Select(_ => Busy(9, 18)).ToArray();
        var timer = Stopwatch.StartNew(); Assert.Empty(Find(items)); timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Calculation took {timer.Elapsed}");
    }

    private static CalendarViewModel Vm(Client client) => new(client, ["Calendar.Read", "CalendarEvent.Create"], TimeZoneInfo.Utc,
        Monday.ToDateTime(TimeOnly.MinValue), clock: () => At(8), userSettings: _ => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopUserSettings>>(new DesktopWorkResult<DesktopUserSettings>.Succeeded(Settings)));
    [Theory]
    [InlineData("next")] [InlineData("previous")] [InlineData("today")] [InlineData("week")] [InlineData("month")] [InlineData("date")]
    public async System.Threading.Tasks.Task ManualNavigation_ClearsChosenSlotAndCreateOffer(string navigation)
    {
        using var vm = Vm(new Client()); await vm.ActivateAsync(); await vm.FindTimeCommand.ExecuteAsync();
        await vm.ChooseFreeTimeCommand.ExecuteAsync(vm.FreeTimeSlots[0]);
        Assert.NotNull(vm.ChosenFreeTime);
        switch (navigation)
        {
            case "next": await vm.NextWeekCommand.ExecuteAsync(); break;
            case "previous": await vm.PreviousWeekCommand.ExecuteAsync(); break;
            case "today": await vm.TodayCommand.ExecuteAsync(); break;
            case "week": await vm.WeekModeCommand.ExecuteAsync(); break;
            case "month": await vm.MonthModeCommand.ExecuteAsync(); break;
            case "date": vm.SelectedDate = Monday.AddDays(1).ToDateTime(TimeOnly.MinValue); break;
        }
        Assert.Null(vm.ChosenFreeTime); Assert.False(vm.ShowFreeTimeHighlight); Assert.False(vm.CreateTaskAtFreeTimeCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task CorporateShell_UsesOrdinaryCreateFlowWithoutSaving()
    {
        var tasksClient = new TaskScreen.TasksViewModelTests.FakeTasksApiClient();
        using var tasks = new TasksViewModel(tasksClient, ["Task.Read", "Task.Create"]);
        using var calendar = Vm(new Client());
        using var shell = new MainWindowViewModel(new Uri("https://task.test/"), null, tasks, calendar);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "calendar");
        for (var i = 0; calendar.IsBusy && i < 100; i++) await System.Threading.Tasks.Task.Delay(5);
        await calendar.FindTimeCommand.ExecuteAsync();
        var slot = calendar.FreeTimeSlots[0]; await calendar.ChooseFreeTimeCommand.ExecuteAsync(slot);
        Assert.Null(tasks.Editor);
        await calendar.CreateTaskAtFreeTimeCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection?.Route); Assert.NotNull(tasks.Editor);
        tasks.Editor!.Title = "Черновик";
        var command = tasks.Editor.BuildCreateCommand();
        Assert.Equal(slot.StartUtc, command!.StartAtUtc); Assert.Equal(30, command.Card!.PlannedDurationMinutes);
        Assert.Equal(0, tasksClient.CreateCallCount);
    }

    private sealed class Client : IDesktopCalendarApiClient
    {
        public List<(DateTimeOffset From, DateTimeOffset To)> Ranges { get; } = [];
        public Func<DateTimeOffset, DateTimeOffset, DesktopCalendarResult<DesktopSchedulePage>> Read { get; set; } = (from, to) => new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], from, to));
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>>? Pending { get; set; }
        public int Writes { get; private set; }
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset from, DateTimeOffset to, string zone, CancellationToken token)
        { Ranges.Add((from, to)); return Pending ?? System.Threading.Tasks.Task.FromResult(Read(from, to)); }
        public System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token) => System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>>(new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([]));
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand command, CancellationToken token) { Writes++; throw new NotSupportedException(); }
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid id, long version, DesktopCalendarEventCommand command, CancellationToken token) { Writes++; throw new NotSupportedException(); }
    }
}
