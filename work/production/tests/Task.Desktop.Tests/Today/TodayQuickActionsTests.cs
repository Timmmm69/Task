using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Today;

public sealed partial class TodayViewModelTests
{
    public static IEnumerable<object[]> QuickTransitions()
    {
        var allowed = new[] { (DesktopTaskStatus.New, DesktopTaskStatus.InProgress),
            (DesktopTaskStatus.Review, DesktopTaskStatus.InProgress),
            (DesktopTaskStatus.InProgress, DesktopTaskStatus.Review),
            (DesktopTaskStatus.New, DesktopTaskStatus.Completed), (DesktopTaskStatus.New, DesktopTaskStatus.Cancelled),
            (DesktopTaskStatus.InProgress, DesktopTaskStatus.Completed), (DesktopTaskStatus.InProgress, DesktopTaskStatus.Cancelled),
            (DesktopTaskStatus.Review, DesktopTaskStatus.Completed), (DesktopTaskStatus.Review, DesktopTaskStatus.Cancelled) };
        foreach (var from in Enum.GetValues<DesktopTaskStatus>())
        foreach (var to in Enum.GetValues<DesktopTaskStatus>())
            yield return [from, to, allowed.Contains((from, to))];
    }

    [Theory]
    [MemberData(nameof(QuickTransitions))]
    public async AsyncTask QuickActions_ObeyAllCanonicalTransitions(DesktopTaskStatus from, DesktopTaskStatus to, bool allowed)
    {
        var task = QuickTask() with { Status = from };
        var client = new QuickClient(task);
        using var vm = QuickVm(client);
        await vm.ActivateAsync();
        Assert.Equal(allowed, vm.TransitionCommand.CanExecute(to));
        Assert.Equal(allowed, await vm.TransitionCommand.ExecuteAsync(to));
        if (!allowed) { Assert.Empty(client.Commands); return; }
        if (to is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled)
        {
            Assert.Empty(client.Commands);
            Assert.True(vm.IsTransitionConfirmationVisible);
            await vm.ConfirmTransitionCommand.ExecuteAsync();
        }
        var sent = Assert.Single(client.Commands);
        Assert.Equal(task.Id, sent.Id);
        Assert.Equal(task.Version, sent.ExpectedVersion);
        Assert.False(string.IsNullOrEmpty(sent.IdempotencyKey));
        Assert.Equal(to, sent.TargetStatus);
        Assert.Equal(1, client.ListCalls);
        Assert.Equal(0, client.DetailCalls);
        Assert.Contains(TaskItemViewModel.LocalizeStatus(to), vm.ActionMessage);
        if (to is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled)
        {
            Assert.Empty(vm.QueueItems); Assert.Empty(vm.OverdueTasks); Assert.Empty(vm.WaitingTasks);
            Assert.Null(vm.SelectedItem); Assert.Equal(TodayScreenState.Empty, vm.State);
        }
        else
        {
            Assert.Equal(to == DesktopTaskStatus.Review, vm.ReviewTasks.Count == 1);
            Assert.Equal(task.Id, vm.SelectedItem?.Id);
            Assert.Equal(task.Version + 1, Assert.IsType<TaskItemViewModel>(vm.SelectedItem!.Source).Source.Version);
        }
    }

    [Fact]
    public async AsyncTask QuickActions_CalendarEventNeverUsesTaskWithSameId()
    {
        var task = QuickTask(); var client = new QuickClient(task); var calendar = new FakeCalendarClient();
        calendar.ScheduleResults.Enqueue(Schedule([AllDayEvent(Today, "Event") with { ObjectId = task.Id }]));
        using var vm = QuickVm(client, calendar);
        await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(Assert.Single(vm.AllDayItems));
        Assert.False(vm.ShowTaskActions);
        Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.Completed));
        Assert.Single(vm.QueueItems); Assert.Equal(0, client.DetailCalls);
        Assert.False(vm.QueueItems[0].IsSelected); Assert.True(vm.AllDayItems[0].IsSelected);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async AsyncTask QuickActions_RequirePermissionAndConnection(bool permission, bool connected)
    {
        var client = new QuickClient(QuickTask());
        using var vm = QuickVm(client, grants: permission ? ["Task.Read", "Task.ChangeStatus"] : ["Task.Read"]);
        await vm.ActivateAsync(); vm.UpdateConnectivity(connected);
        Assert.False(await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress));
        Assert.Empty(client.Commands); Assert.Single(vm.QueueItems);
    }

    [Fact]
    public async AsyncTask QuickActions_RevokePermissionWhileConfirmingPreventsWrite()
    {
        var client = new QuickClient(QuickTask()); using var vm = QuickVm(client);
        await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed);
        vm.UpdateCapabilities(["Task.Read"]);
        Assert.False(vm.IsTransitionConfirmationVisible);
        Assert.False(await vm.ConfirmTransitionCommand.ExecuteAsync()); Assert.Empty(client.Commands);
    }

    [Fact]
    public async AsyncTask QuickActions_ConfirmationCanBeDismissedWithoutWriting()
    {
        var client = new QuickClient(QuickTask()); using var vm = QuickVm(client);
        await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Cancelled);
        await vm.CancelTransitionCommand.ExecuteAsync();
        Assert.Empty(client.Commands); Assert.False(vm.IsTransitionConfirmationVisible); Assert.True(vm.CanStart);
    }

    [Fact]
    public async AsyncTask QuickActions_SwitchWhileWritingAppliesResponseToOriginalAndKeepsNewSelection()
    {
        var task = QuickTask(); var other = QuickTask() with { Title = "Other" };
        var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new QuickClient(task, other) { Pending = pending.Task }; using var vm = QuickVm(client);
        await vm.ActivateAsync(); await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == task.Id));
        var write = vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.True(vm.IsActionBusy);
        Assert.False(await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == other.Id));
        Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.Completed));
        Assert.Equal("Запланирована", vm.QueueItems.Single(i => i.Id == task.Id).StatusText);
        pending.SetResult(QuickSuccess(task, DesktopTaskStatus.InProgress)); await write;
        Assert.Single(client.Commands); Assert.Equal(other.Id, vm.SelectedItem?.Id);
        Assert.Equal("В работе", vm.QueueItems.Single(i => i.Id == task.Id).StatusText);
    }

    [Fact]
    public async AsyncTask QuickActions_SwitchBeforeConfirmationDoesNotTargetAnotherTask()
    {
        var task = QuickTask(); var other = QuickTask(); var client = new QuickClient(task, other);
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed);
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id != vm.SelectedItem!.Id));
        Assert.False(await vm.ConfirmTransitionCommand.ExecuteAsync()); Assert.Empty(client.Commands);
    }

    [Fact]
    public async AsyncTask QuickActions_TimelineUsesTaskVersionAndUpdatesAllProjectionsWithoutReload()
    {
        var task = QuickTask() with { Status = DesktopTaskStatus.InProgress, AssigneeIds = [Other], StartAtUtc = Clock().AddHours(-1) };
        var calendar = new FakeCalendarClient(); calendar.ScheduleResults.Enqueue(Schedule([TimedTask(Today, "Projection") with { ObjectId = task.Id }]));
        var client = new QuickClient(task); using var vm = QuickVm(client, calendar); await vm.ActivateAsync();
        Assert.IsType<CalendarItemViewModel>(vm.SelectedItem!.Source); Assert.Single(vm.WaitingTasks); Assert.Empty(vm.QueueItems);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Review);
        Assert.Equal(task.Version, Assert.Single(client.Commands).ExpectedVersion);
        Assert.Single(vm.ReviewTasks); Assert.Empty(vm.WaitingTasks); Assert.Single(vm.OverdueTasks);
        Assert.Equal("На проверке", vm.SelectedItem!.StatusText); Assert.Empty(vm.QueueItems);
        Assert.Equal("review", Assert.Single(vm.TimedItems).Source.Status);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed); await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Empty(vm.TimelineItems); Assert.Empty(vm.TimedItems); Assert.Empty(vm.ReviewTasks); Assert.Empty(vm.OverdueTasks);
        Assert.Equal(1, calendar.ScheduleCalls); Assert.Equal(1, client.ListCalls);
    }

    [Fact]
    public async AsyncTask QuickActions_MissingCalendarTaskFetchesCanonicalDto()
    {
        var task = QuickTask() with { Version = 37 };
        var calendar = new FakeCalendarClient(); calendar.ScheduleResults.Enqueue(Schedule([TimedTask(Today, "Projection") with { ObjectId = task.Id }]));
        var client = new QuickClient { Detail = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task) };
        using var vm = QuickVm(client, calendar); await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal(37, Assert.Single(client.Commands).ExpectedVersion); Assert.Equal(1, client.DetailCalls);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("invalid")]
    public async AsyncTask QuickActions_ConflictReloadsOneTaskAndRequiresNewUserAction(string outcome)
    {
        var task = QuickTask(); var latest = task with { Version = 50, Status = DesktopTaskStatus.InProgress, Title = "Updated" };
        var client = new QuickClient(task) { Detail = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(latest) };
        client.Results.Enqueue(outcome == "version" ? new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict()
            : new DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition());
        using var vm = QuickVm(client); await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal("Updated", vm.SelectedItem!.Title); Assert.Contains("другим пользователем", vm.ActionMessage);
        Assert.Equal(1, client.DetailCalls); Assert.Equal(1, client.ListCalls); Assert.Single(client.Commands);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Review);
        Assert.Equal(50, client.Commands[1].ExpectedVersion);
        Assert.NotEqual(client.Commands[0].IdempotencyKey, client.Commands[1].IdempotencyKey);
    }

    [Fact]
    public async AsyncTask QuickActions_FailedConflictReloadBlocksWritesUntilExplicitReload()
    {
        var task = QuickTask(); var client = new QuickClient(task);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict());
        using var vm = QuickVm(client); await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.True(vm.NeedsTaskReload); Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.InProgress));
        client.Detail = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task with { Version = 40 });
        await vm.ReloadSelectedTaskCommand.ExecuteAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal(40, client.Commands[1].ExpectedVersion);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("progress")]
    [InlineData("malformed")]
    [InlineData("cancel")]
    public async AsyncTask QuickActions_AmbiguousOutcomeRetainsExactCommandAcrossSelectionAndReconnect(string outcome)
    {
        var task = QuickTask(); var other = QuickTask(); var client = new QuickClient(task, other) { CancelWrite = outcome == "cancel" };
        if (outcome != "cancel") client.Results.Enqueue(outcome switch
        {
            "progress" => new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress(),
            "malformed" => new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
        });
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == task.Id));
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal("Запланирована", vm.SelectedItem!.StatusText); Assert.False(string.IsNullOrEmpty(vm.ActionMessage));
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == other.Id));
        vm.UpdateConnectivity(true); Assert.Single(client.Commands); client.CancelWrite = false;
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == task.Id));
        Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.Completed));
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Same(client.Commands[0], client.Commands[1]); Assert.Equal("В работе", vm.SelectedItem!.StatusText);
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("forbidden")]
    [InlineData("missing")]
    [InlineData("validation")]
    [InlineData("key")]
    [InlineData("precondition")]
    public async AsyncTask QuickActions_ClassifiesDefinitiveFailuresWithoutFalseSuccess(string outcome)
    {
        var client = new QuickClient(QuickTask());
        client.Results.Enqueue(outcome switch
        {
            "auth" => new DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure(),
            "forbidden" => new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
            "missing" => new DesktopTaskWriteResult<DesktopTaskDto>.NotFound(),
            "validation" => new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Ошибка проверки", new Dictionary<string, IReadOnlyList<string>>()),
            "key" => new DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict(),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired(),
        });
        using var vm = QuickVm(client); await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Single(client.Commands);
        if (outcome == "auth") { Assert.Equal(TodayScreenState.SessionEnded, vm.State); Assert.Empty(vm.QueueItems); }
        else if (outcome == "missing") { Assert.Null(vm.SelectedItem); Assert.Empty(vm.QueueItems); }
        else { Assert.Equal("Запланирована", vm.SelectedItem!.StatusText); Assert.NotNull(vm.ActionMessage); }
        if (outcome is "key" or "precondition" or "forbidden") Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.InProgress));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("deactivate")]
    [InlineData("permission")]
    public async AsyncTask QuickActions_LifecycleRejectsLateWrite(string change)
    {
        var task = QuickTask(); var pending = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var client = new QuickClient(task) { Pending = pending.Task }; using var vm = QuickVm(client);
        await vm.ActivateAsync(); var write = vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        if (change == "session") vm.UpdateSessionState(false);
        else if (change == "deactivate") vm.Deactivate();
        else vm.UpdateCapabilities([]);
        pending.SetResult(QuickSuccess(task, DesktopTaskStatus.InProgress)); await write;
        Assert.Null(vm.ActionMessage); Assert.DoesNotContain(vm.QueueItems, i => i.StatusText == "В работе");
    }

    [Fact]
    public async AsyncTask QuickActions_AuthoritativeReloadResolvesAmbiguousCommandWithoutBlockingNextTransition()
    {
        var task = QuickTask(); var client = new QuickClient(task);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress());
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        client.Detail = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task with { Status = DesktopTaskStatus.InProgress, Version = 8 });
        await vm.ReloadSelectedTaskCommand.ExecuteAsync();
        Assert.True(vm.TransitionCommand.CanExecute(DesktopTaskStatus.Review));
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Review);
        Assert.NotEqual(client.Commands[0].IdempotencyKey, client.Commands[1].IdempotencyKey);
        Assert.Equal(8, client.Commands[1].ExpectedVersion);
    }

    [Fact]
    public async AsyncTask QuickActions_IdempotencyConflictRequiresReloadAndNewCommand()
    {
        var task = QuickTask(); var client = new QuickClient(task);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict());
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.False(vm.TransitionCommand.CanExecute(DesktopTaskStatus.InProgress));
        client.Detail = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task with { Version = 11 });
        await vm.ReloadSelectedTaskCommand.ExecuteAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal(11, client.Commands[1].ExpectedVersion);
        Assert.NotEqual(client.Commands[0].IdempotencyKey, client.Commands[1].IdempotencyKey);
    }

    [Fact]
    public async AsyncTask QuickActions_TerminalRetryKeepsConfirmationAndExactCommand()
    {
        var task = QuickTask(); var client = new QuickClient(task);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress());
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.Completed); await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.True(vm.IsTransitionConfirmationVisible); Assert.Single(vm.QueueItems);
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Same(client.Commands[0], client.Commands[1]); Assert.Empty(vm.QueueItems);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("status")]
    public async AsyncTask QuickActions_RejectsInconsistentSuccessDto(string field)
    {
        var task = QuickTask(); var client = new QuickClient(task);
        var response = task with { Id = field == "id" ? Guid.NewGuid() : task.Id,
            Version = field == "version" ? task.Version : task.Version + 1,
            Status = field == "status" ? DesktopTaskStatus.Cancelled : DesktopTaskStatus.InProgress };
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(response, response.Version, true));
        using var vm = QuickVm(client); await vm.ActivateAsync(); await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal("Запланирована", vm.SelectedItem!.StatusText); Assert.Contains("не подтверждена", vm.ActionMessage);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Same(client.Commands[0], client.Commands[1]);
    }

    [Fact]
    public async AsyncTask QuickActions_ObjectScopeForbiddenDoesNotDisableOtherTasks()
    {
        var denied = QuickTask(); var allowed = QuickTask() with { Title = "Allowed" };
        var client = new QuickClient(denied, allowed);
        client.Results.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden());
        using var vm = QuickVm(client); await vm.ActivateAsync();
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == denied.Id));
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.False(vm.CanStart);
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == allowed.Id));
        Assert.True(vm.CanStart);
        await vm.TransitionCommand.ExecuteAsync(DesktopTaskStatus.InProgress);
        Assert.Equal(2, client.Commands.Count); Assert.Equal(allowed.Id, client.Commands[1].Id);
        await vm.SelectItemCommand.ExecuteAsync(vm.QueueItems.Single(i => i.Id == denied.Id));
        Assert.False(vm.CanStart);
    }

    private static DesktopTaskDto QuickTask() => SummaryTask("Quick", User, [User], Clock().AddDays(-1)) with { Version = 7 };
    private static DesktopTaskWriteResult<DesktopTaskDto> QuickSuccess(DesktopTaskDto task, DesktopTaskStatus target) =>
        new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task with { Version = task.Version + 1, Status = target }, task.Version + 1, false);
    private static TodayViewModel QuickVm(QuickClient client, FakeCalendarClient? calendar = null, string[]? grants = null) =>
        new(calendar ?? new FakeCalendarClient(), grants ?? (calendar is null
            ? ["Task.Read", "Task.ChangeStatus"] : ["Task.Read", "Task.ChangeStatus", "Calendar.Read"]), TimeZoneInfo.Utc, Clock, client, User);

    private sealed class QuickClient(params DesktopTaskDto[] tasks) : IDesktopTasksApiClient
    {
        public List<DesktopTransitionTaskCommand> Commands { get; } = [];
        public Queue<DesktopTaskWriteResult<DesktopTaskDto>> Results { get; } = [];
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>>? Pending { get; init; }
        public DesktopTasksApiResult<DesktopTaskDto> Detail { get; set; } = new DesktopTasksApiResult<DesktopTaskDto>.ServerUnavailable();
        public bool CancelWrite { get; set; }
        public int ListCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default)
        { ListCalls++; return System.Threading.Tasks.Task.FromResult(Page(tasks, null)); }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
        { DetailCalls++; return System.Threading.Tasks.Task.FromResult(Detail); }
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (CancelWrite) throw new OperationCanceledException(cancellationToken);
            var source = tasks.SingleOrDefault(t => t.Id == command.Id)
                ?? (Detail as DesktopTasksApiResult<DesktopTaskDto>.Succeeded)!.Value;
            return Pending ?? System.Threading.Tasks.Task.FromResult(Results.Count > 0 ? Results.Dequeue()
                : QuickSuccess(source with { Version = command.ExpectedVersion }, command.TargetStatus));
        }
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
