using System.Text.Json.Nodes;
using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Today;

public sealed partial class TodayViewModelTests
{
    [Fact]
    public async AsyncTask Planning_DateOnlyPatchPreservesDeadlineAndCard_AndReopensFromConfirmedData()
    {
        var task = PlanningTask(); var client = new PlanningClient(task);
        using var vm = PlanningVm(client); await vm.ActivateAsync();
        vm.Planning!.Date = Today.ToDateTime(TimeOnly.MinValue);
        Assert.Empty(client.Patches); await vm.SavePlanningCommand.ExecuteAsync();
        var patch = Assert.Single(client.Patches);
        Assert.Equal("{\"scheduledDate\":\"2026-08-31\"}", patch.CardPatch);
        Assert.False(patch.StartAtUtc.IsSpecified); Assert.False(patch.DeadlineAtUtc.IsSpecified);
        Assert.False(patch.Title.IsSpecified); Assert.False(patch.Priority.IsSpecified);
        Assert.Equal(task.Version, patch.ExpectedVersion);
        var saved = client.Tasks[task.Id];
        Assert.Equal(task.DeadlineAtUtc, saved.DeadlineAtUtc);
        Assert.Equal(task.Card!.Description, saved.Card!.Description);
        Assert.Equal(task.Card.DeadlineDate, saved.Card.DeadlineDate);
        Assert.Equal(task.Card.DeadlineReminderOffsetMinutes, saved.Card.DeadlineReminderOffsetMinutes);
        Assert.Equal(task.Card.ProjectId, saved.Card.ProjectId);
        Assert.Empty(vm.QueueItems); Assert.Single(vm.AllDayItems); Assert.Equal(task.Id, vm.SelectedItem?.Id);
        Assert.False(vm.SavePlanningCommand.CanExecute(null));
        vm.Deactivate(); await vm.ActivateAsync();
        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), vm.Planning!.Date);
        Assert.Single(vm.AllDayItems); Assert.Empty(vm.QueueItems);
    }

    [Fact]
    public async AsyncTask Planning_TimeAndDurationMoveTimelineWithoutDeadlineOrDuplicate_ThenStatusUsesLatestVersion()
    {
        var task = PlanningTask() with { Card = PlanningTask().Card! with { ScheduledDate = Today } };
        var client = new PlanningClient(task); using var vm = PlanningVm(client); await vm.ActivateAsync();
        vm.Planning!.Time = "10:30"; await vm.SavePlanningCommand.ExecuteAsync();
        var startPatch = client.Patches[0]; Assert.True(startPatch.StartAtUtc.IsSpecified);
        Assert.Equal(Today.ToDateTime(new(10, 30)), startPatch.StartAtUtc.Value!.Value.UtcDateTime);
        Assert.DoesNotContain("plannedDurationMinutes", startPatch.CardPatch!);
        Assert.False(startPatch.DeadlineAtUtc.IsSpecified);
        Assert.Single(vm.TimelineItems); Assert.Empty(vm.AllDayItems); Assert.Empty(vm.QueueItems);
        Assert.Equal("10:30–11:30", vm.SelectedItem!.TimeText);
        vm.Planning!.Duration = "120"; await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Equal("{\"plannedDurationMinutes\":120}", client.Patches[1].CardPatch);
        Assert.False(client.Patches[1].StartAtUtc.IsSpecified);
        Assert.Equal("10:30–12:30", Assert.Single(vm.TimelineItems).TimeText);
        Assert.Equal(132, vm.TimelineItems[0].TimelineHeight);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal(task.Version + 2, Assert.Single(client.Transitions).ExpectedVersion);
        Assert.Equal(120, vm.Planning is null ? null : client.Tasks[task.Id].Card!.PlannedDurationMinutes);
        vm.Planning!.Time = "11:00"; await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Equal(task.Version + 3, client.Patches[2].ExpectedVersion);
        Assert.Equal(DesktopTaskStatus.InProgress, client.Tasks[task.Id].Status);
    }

    [Fact]
    public async AsyncTask Planning_ClearTimeMovesToAllDay_ClearDateMovesToQueue_FutureDateLeavesSelection()
    {
        var client = new PlanningClient(PlanningTask()); using var vm = PlanningVm(client); await vm.ActivateAsync();
        vm.Planning!.Date = Today.ToDateTime(TimeOnly.MinValue); vm.Planning.Time = "09:00";
        await vm.SavePlanningCommand.ExecuteAsync(); Assert.Single(vm.TimelineItems);
        vm.Planning!.Time = ""; await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Empty(vm.TimelineItems); Assert.Single(vm.AllDayItems); Assert.Empty(vm.QueueItems);
        Assert.True(client.Patches[1].StartAtUtc.IsSpecified); Assert.Null(client.Patches[1].StartAtUtc.Value);
        vm.Planning!.Date = null; await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Single(vm.QueueItems); Assert.Empty(vm.AllDayItems);
        vm.Planning!.Date = Today.AddDays(1).ToDateTime(TimeOnly.MinValue); await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Empty(vm.QueueItems); Assert.Null(vm.SelectedItem); Assert.Equal(TodayScreenState.Empty, vm.State);
    }

    [Fact]
    public async AsyncTask Planning_CancelRestoresConfirmedValuesWithoutApiCall_AndSwitchDiscardsDraft()
    {
        var first = PlanningTask(); var second = PlanningTask() with { Title = "Second" };
        var client = new PlanningClient(first, second); using var vm = PlanningVm(client); await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == first.Id));
        vm.Planning!.Date = Today.ToDateTime(TimeOnly.MinValue); vm.Planning.Time = "bad"; vm.Planning.Duration = "-1";
        Assert.False(vm.SavePlanningCommand.CanExecute(null)); await vm.CancelPlanningCommand.ExecuteAsync();
        Assert.Null(vm.Planning!.Date); Assert.Equal("", vm.Planning.Time); Assert.Equal("60", vm.Planning.Duration);
        vm.Planning.Date = Today.ToDateTime(TimeOnly.MinValue);
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == second.Id));
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == first.Id));
        Assert.Null(vm.Planning!.Date); Assert.Empty(client.Patches);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("offline")]
    [InlineData("session")]
    [InlineData("inactive")]
    public async AsyncTask Planning_WriteRequiresLiveSessionPermissionAndConnection(string condition)
    {
        var client = new PlanningClient(PlanningTask()); using var vm = PlanningVm(client); await vm.ActivateAsync();
        vm.Planning!.Duration = "30";
        if (condition == "permission") vm.UpdateCapabilities(["Task.Read"]);
        if (condition == "offline") vm.UpdateConnectivity(false);
        if (condition == "session") vm.UpdateSessionState(false);
        if (condition == "inactive") vm.Deactivate();
        Assert.False(vm.CanEditPlanning); Assert.False(await vm.SavePlanningCommand.ExecuteAsync()); Assert.Empty(client.Patches);
        if (condition == "offline")
        {
            vm.UpdateConnectivity(true); Assert.Empty(client.Patches);
            Assert.True(await vm.SavePlanningCommand.ExecuteAsync());
        }
    }

    [Fact]
    public async AsyncTask Planning_ConflictKeepsDraftUntilExplicitReload_ThenManualRetryUsesNewVersion()
    {
        var task = PlanningTask(); var client = new PlanningClient(task);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict());
        using var vm = PlanningVm(client); await vm.ActivateAsync();
        vm.Planning!.Duration = "90"; await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Equal("90", vm.Planning.Duration); Assert.Equal(0, client.DetailCalls);
        Assert.Contains("вручную", vm.PlanningMessage); Assert.False(vm.SavePlanningCommand.CanExecute(null));
        client.Tasks[task.Id] = task with { Version = 19, Card = task.Card! with { Description = "Someone else's change", PlannedDurationMinutes = 45 } };
        await vm.ReloadSelectedTaskCommand.ExecuteAsync();
        Assert.Equal("45", vm.Planning!.Duration); vm.Planning.Duration = "90";
        await vm.SavePlanningCommand.ExecuteAsync(); Assert.Equal(19, client.Patches[1].ExpectedVersion);
        Assert.NotEqual(client.Patches[0].IdempotencyKey, client.Patches[1].IdempotencyKey);
        Assert.Equal("Someone else's change", client.Tasks[task.Id].Card!.Description);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("offline")]
    [InlineData("cancel")]
    [InlineData("malformed")]
    public async AsyncTask Planning_AmbiguousOutcomeRetriesExactCommandAndBlocksStatus(string failure)
    {
        var task = PlanningTask(); var client = new PlanningClient(task) { CancelWrite = failure == "cancel" };
        if (failure != "cancel") client.Results.Enqueue(failure switch
        {
            "busy" => new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress(),
            "offline" => new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task with { Id = Guid.NewGuid(), Version = 8 }, 8, false)
        });
        using var vm = PlanningVm(client); await vm.ActivateAsync(); vm.Planning!.Duration = "90";
        await vm.SavePlanningCommand.ExecuteAsync(); Assert.Equal("90", vm.Planning.Duration);
        Assert.False(vm.CanEditPlanning); Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.InProgress));
        client.CancelWrite = false; vm.UpdateConnectivity(true);
        Assert.Single(client.Patches); await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Same(client.Patches[0], client.Patches[1]); Assert.True(vm.CanEditPlanning);
        Assert.Equal("90", vm.Planning!.Duration);
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("forbidden")]
    [InlineData("notfound")]
    [InlineData("auth")]
    [InlineData("idempotency")]
    [InlineData("precondition")]
    public async AsyncTask Planning_HandlesDefinitiveServerFailures(string failure)
    {
        var task = PlanningTask(); var client = new PlanningClient(task);
        client.Results.Enqueue(failure switch
        {
            "validation" => new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Server validation", new Dictionary<string, IReadOnlyList<string>>()),
            "forbidden" => new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
            "notfound" => new DesktopTaskWriteResult<DesktopTaskDto>.NotFound(),
            "auth" => new DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure(),
            "idempotency" => new DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict(),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired()
        });
        using var vm = PlanningVm(client); await vm.ActivateAsync(); vm.Planning!.Duration = "90";
        await vm.SavePlanningCommand.ExecuteAsync(); Assert.Single(client.Patches);
        if (failure is "auth" or "notfound") Assert.Null(vm.SelectedItem);
        else
        {
            Assert.Equal("90", vm.Planning!.Duration);
            Assert.Equal(failure == "validation", vm.CanEditPlanning);
            if (failure == "validation") Assert.Equal("Server validation", vm.PlanningMessage);
        }
    }

    [Fact]
    public async AsyncTask Planning_InFlightDoubleSaveAndStatusBlocked_SwitchKeepsNewDraft()
    {
        var first = PlanningTask(); var second = PlanningTask() with { Title = "Second" };
        var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new PlanningClient(first, second) { Pending = pending.Task };
        using var vm = PlanningVm(client); await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == first.Id));
        vm.Planning!.Duration = "90"; var write = vm.SavePlanningCommand.ExecuteAsync();
        Assert.False(await vm.SavePlanningCommand.ExecuteAsync()); Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.InProgress));
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == second.Id));
        vm.Planning!.Duration = "120";
        pending.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(first with { Version = 8, Card = first.Card! with { PlannedDurationMinutes = 90 } }, 8, false));
        await write; Assert.Single(client.Patches); Assert.Equal(second.Id, vm.SelectedItem?.Id);
        Assert.Equal("120", vm.Planning!.Duration); Assert.Null(vm.PlanningMessage);
    }

    [Theory]
    [InlineData("section")]
    [InlineData("session")]
    [InlineData("read")]
    [InlineData("update")]
    public async AsyncTask Planning_LateResponseAfterLifecycleChangeDoesNotApply(string change)
    {
        var task = PlanningTask(); var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new PlanningClient(task) { Pending = pending.Task };
        using var vm = PlanningVm(client); await vm.ActivateAsync(); vm.Planning!.Duration = "90";
        var write = vm.SavePlanningCommand.ExecuteAsync();
        if (change == "section") vm.Deactivate();
        if (change == "session") vm.UpdateSessionState(false);
        if (change == "read") vm.UpdateCapabilities([]);
        if (change == "update") vm.UpdateCapabilities(["Task.Read"]);
        pending.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task with { Version = 8, Card = task.Card! with { PlannedDurationMinutes = 90 } }, 8, false));
        await write; Assert.Null(vm.PlanningMessage);
        Assert.DoesNotContain(vm.QueueItems, i => ((TaskItemViewModel)i.Source).Source.Version == 8);
    }

    [Fact]
    public async AsyncTask Planning_CalendarEventDoesNotEditTaskWithSameId()
    {
        var task = PlanningTask(); var client = new PlanningClient(task); var calendar = new FakeCalendarClient();
        calendar.ScheduleResults.Enqueue(Schedule([AllDayEvent(Today, "Event") with { ObjectId = task.Id }]));
        using var vm = PlanningVm(client, calendar); await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(Assert.Single(vm.AllDayItems));
        Assert.False(vm.ShowPlanning); Assert.Null(vm.Planning); Assert.False(await vm.SavePlanningCommand.ExecuteAsync());
        object? opened = null; vm.OpenItemRequested += item => opened = item;
        await vm.OpenSelectedItemCommand.ExecuteAsync(); Assert.IsType<CalendarItemViewModel>(opened);
        Assert.Empty(client.Patches);
    }

    [Theory]
    [InlineData(2026, 3, 8, "02:30", false)]
    [InlineData(2026, 11, 1, "01:30", false)]
    [InlineData(2026, 3, 8, "03:30", true)]
    public void Planning_DstUsesSavedZoneAndRejectsInvalidAndAmbiguousTime(int year, int month, int day, string time, bool valid)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var task = PlanningTask() with { DeadlineAtUtc = null, Card = new TaskCardContent { ScheduleTimeZone = zone.Id } };
        var draft = new TodayPlanningEditor(task, TimeZoneInfo.Utc) { Date = new(year, month, day), Time = time };
        Assert.Equal(valid, draft.Validate());
        if (!valid) { Assert.Throws<ArgumentException>(() => draft.BuildPatch()); return; }
        var command = draft.BuildPatch()!;
        Assert.Equal(new DateTimeOffset(year, month, day, 7, 30, 0, TimeSpan.Zero), command.StartAtUtc.Value);
        Assert.Equal(zone.Id, task.Card!.Apply(command.CardPatch).ScheduleTimeZone);
    }

    [Theory]
    [InlineData("24:00", "60")]
    [InlineData("09:00", "60")]
    [InlineData("", "0")]
    [InlineData("", "10081")]
    [InlineData("", "x")]
    public void Planning_InvalidDraftCannotBuildPatch(string time, string duration)
    {
        var draft = new TodayPlanningEditor(PlanningTask(), TimeZoneInfo.Utc) { Time = time, Duration = duration };
        Assert.False(draft.Validate()); Assert.Throws<ArgumentException>(() => draft.BuildPatch());
    }

    [Fact]
    public void Planning_PreservesExactLegacyUtcOnDurationOnly_RejectsStartAfterDeadline()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero).AddTicks(1234);
        var task = PlanningTask() with { StartAtUtc = instant, DeadlineAtUtc = instant.AddHours(2), Card = new TaskCardContent() };
        var draft = new TodayPlanningEditor(task, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")) { Duration = "30" };
        Assert.False(draft.BuildPatch()!.StartAtUtc.IsSpecified);
        draft.Date = new DateTime(2026, 11, 2); Assert.False(draft.Validate());
    }

    [Fact]
    public async AsyncTask Planning_DateOnlyDeadlineRemainsSeparate_OverdueStaysInQueueAfterFutureReschedule()
    {
        var task = PlanningTask() with { DeadlineAtUtc = null, Card = new TaskCardContent
            { DeadlineDate = Today.AddDays(-1), DeadlineTimeZone = "UTC", ScheduledDate = Today } };
        var client = new PlanningClient(task); using var vm = PlanningVm(client); await vm.ActivateAsync();
        Assert.Single(vm.AllDayItems); Assert.Single(vm.OverdueTasks);
        vm.Planning!.Date = Today.AddDays(1).ToDateTime(TimeOnly.MinValue); await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Single(vm.QueueItems); Assert.Single(vm.OverdueTasks); Assert.Empty(vm.AllDayItems);
        Assert.Equal(task.Id, vm.SelectedItem?.Id);
        Assert.Equal(task.Card.DeadlineDate, client.Tasks[task.Id].Card!.DeadlineDate);
        Assert.Equal("{\"scheduledDate\":\"2026-09-01\"}", client.Patches[0].CardPatch);
    }

    [Fact]
    public async AsyncTask Planning_OvernightTaskClipsToCurrentDay_AndNeverUsesDeadlineAsDuration()
    {
        var start = new DateTimeOffset(Today.AddDays(-1).ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero);
        var task = PlanningTask() with { StartAtUtc = start, Card = new TaskCardContent
            { ScheduledDate = Today.AddDays(-1), StartTimeLocal = new(23, 30), ScheduleTimeZone = "UTC", PlannedDurationMinutes = 120 } };
        var client = new PlanningClient(task); using var vm = PlanningVm(client); await vm.ActivateAsync();
        Assert.Equal(0, Assert.Single(vm.TimelineItems).TimelineTop);
        Assert.Equal(97.5, vm.TimelineItems[0].TimelineHeight);
        Assert.Equal("23:30–01:30", vm.TimelineItems[0].TimeText);
        vm.Planning!.Date = Today.ToDateTime(TimeOnly.MinValue); await vm.SavePlanningCommand.ExecuteAsync();
        Assert.Equal(1621.5, Assert.Single(vm.TimelineItems).TimelineTop);
        Assert.Equal(34.5, vm.TimelineItems[0].TimelineHeight);
        Assert.Equal(task.DeadlineAtUtc, client.Tasks[task.Id].DeadlineAtUtc);
    }

    [Fact]
    public void Planning_ZoneConversionCanChangeUtcDayWithoutChangingPlanningDay()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var draft = new TodayPlanningEditor(PlanningTask() with { DeadlineAtUtc = null }, zone)
            { Date = Today.ToDateTime(TimeOnly.MinValue), Time = "00:30" };
        var command = draft.BuildPatch()!;
        Assert.Equal(new DateTimeOffset(Today.AddDays(-1).ToDateTime(new TimeOnly(15, 30)), TimeSpan.Zero), command.StartAtUtc.Value);
        Assert.Equal(Today.ToString("yyyy-MM-dd"), JsonNode.Parse(command.CardPatch!)!["scheduledDate"]!.ToString());
    }

    [Fact]
    public async AsyncTask Planning_DurationAcrossDstConvertsEndInstantUsingZoneRules()
    {
        var task = PlanningTask() with { StartAtUtc = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero),
            Card = new TaskCardContent { ScheduledDate = new(2026, 3, 8), StartTimeLocal = new(1, 30),
                ScheduleTimeZone = "America/New_York", PlannedDurationMinutes = 120 } };
        using var vm = new TodayViewModel(new FakeCalendarClient(), ["Task.Read", "Task.Update"],
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
            () => new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero), new PlanningClient(task), User);
        await vm.ActivateAsync();
        Assert.Equal("01:30–04:30", Assert.Single(vm.TimelineItems).TimeText);
        Assert.Equal("120", vm.Planning!.Duration);
    }

    private static DesktopTaskDto PlanningTask() => QuickTask() with { DeadlineAtUtc = Clock().AddDays(10),
        Card = new TaskCardContent { Description = "Keep me", ProjectId = Guid.NewGuid(), PlannedDurationMinutes = 60,
            DeadlineReminderOffsetMinutes = 15 } };
    private static TodayViewModel PlanningVm(PlanningClient client, FakeCalendarClient? calendar = null) =>
        new(calendar ?? new FakeCalendarClient(), calendar is null ? ["Task.Read", "Task.Update", "Task.ChangeStatus"]
            : ["Task.Read", "Task.Update", "Task.ChangeStatus", "Calendar.Read"], TimeZoneInfo.Utc, Clock, client, User);

    private sealed class PlanningClient(params DesktopTaskDto[] tasks) : IDesktopTasksApiClient
    {
        public Dictionary<Guid, DesktopTaskDto> Tasks { get; } = tasks.ToDictionary(t => t.Id);
        public List<DesktopPatchTaskCommand> Patches { get; } = [];
        public List<DesktopTransitionTaskCommand> Transitions { get; } = [];
        public Queue<DesktopTaskWriteResult<DesktopTaskDto>> Results { get; } = [];
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>>? Pending { get; init; }
        public bool CancelWrite { get; set; }
        public int DetailCalls { get; private set; }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(Page(Tasks.Values.ToArray(), null));
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
        { DetailCalls++; return System.Threading.Tasks.Task.FromResult<DesktopTasksApiResult<DesktopTaskDto>>(new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(Tasks[id])); }
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default)
        {
            Patches.Add(command);
            if (CancelWrite) throw new OperationCanceledException(cancellationToken);
            if (Pending is not null) return Pending;
            if (Results.Count > 0) return System.Threading.Tasks.Task.FromResult(Results.Dequeue());
            var source = Tasks[command.Id]; Assert.Equal(source.Version, command.ExpectedVersion);
            var saved = source with { Version = source.Version + 1,
                StartAtUtc = command.StartAtUtc.IsSpecified ? command.StartAtUtc.Value : source.StartAtUtc,
                Card = (source.Card ?? new()).Apply(command.CardPatch) };
            saved.Card.Validate(saved.StartAtUtc); Tasks[saved.Id] = saved;
            return System.Threading.Tasks.Task.FromResult<DesktopTaskWriteResult<DesktopTaskDto>>(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(saved, saved.Version, false));
        }
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default)
        {
            Transitions.Add(command); var task = Tasks[command.Id]; Assert.Equal(task.Version, command.ExpectedVersion);
            var saved = task with { Status = command.TargetStatus, Version = task.Version + 1 }; Tasks[saved.Id] = saved;
            return System.Threading.Tasks.Task.FromResult<DesktopTaskWriteResult<DesktopTaskDto>>(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(saved, saved.Version, false));
        }
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
