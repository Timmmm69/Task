using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class TasksViewModelTests
{
    [Theory]
    [InlineData(DesktopTaskStatus.New, false, true)]
    [InlineData(DesktopTaskStatus.InProgress, false, false)]
    [InlineData(DesktopTaskStatus.Review, true, false)]
    [InlineData(DesktopTaskStatus.Completed, false, false)]
    [InlineData(DesktopTaskStatus.Cancelled, false, false)]
    public async System.Threading.Tasks.Task Return_EligibilityUsesStatusAndLoadedDetails(DesktopTaskStatus status, bool canReturn, bool canStart)
    {
        var source = CreateTask(status: status);
        var client = new ReturnClient(source);
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        Assert.Equal(canReturn, vm.CanReturnForRework);
        Assert.Equal(canStart, vm.CanStart);
        vm.SelectedItem = null;
        Assert.False(vm.CanReturnForRework);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("readonly")]
    [InlineData("offline")]
    [InlineData("editor")]
    [InlineData("comment")]
    [InlineData("notfound")]
    public async System.Threading.Tasks.Task Return_AccessGuardsBlockSending(string guard)
    {
        var client = new ReturnClient(CreateTask(status: DesktopTaskStatus.Review));
        if (guard == "notfound") client.Inner.DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.NotFound();
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        switch (guard)
        {
            case "permission": vm.UpdateCapabilities(["Task.Read", "Task.Update"]); break;
            case "readonly": vm.UpdateSessionState(false); break;
            case "offline": vm.UpdateConnectivity(false); break;
            case "editor": await vm.EditTaskCommand.ExecuteAsync(); break;
            case "comment": vm.Workspace!.Comment = "Несохранённый текст"; break;
        }
        Assert.False(vm.CanReturnForRework);
        Assert.False(await vm.TransitionCommand.ExecuteAsync("InProgress"));
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Equal(0, client.Inner.TransitionCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Return_WaitsForServerSingleFlightAndRefreshesCardListHistory()
    {
        var source = CreateTask(status: DesktopTaskStatus.Review) with { Version = 7 };
        var returned = source with { Status = DesktopTaskStatus.InProgress, Version = 8 };
        var client = new ReturnClient(source);
        var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        client.Inner.TransitionHandler = _ => release.Task;
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        var reads = client.WorkspaceReads;
        await vm.TransitionCommand.ExecuteAsync("InProgress");
        Assert.Equal("Вернуть задачу на доработку?", vm.PendingTransitionText);
        Assert.Equal("Вернуть", vm.TransitionConfirmationLabel);
        Assert.False(vm.ShowTransitionReason);
        var request = vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.True(vm.IsMutationBusy);
        Assert.Equal("Сохранение…", vm.TransitionConfirmationLabel);
        Assert.False(await vm.ConfirmTransitionCommand.ExecuteAsync());
        Assert.False(vm.CanReturnForRework);
        Assert.Equal(DesktopTaskStatus.Review, vm.SelectedDetails!.Source.Status);
        Assert.Equal(7, client.Inner.LastTransition!.ExpectedVersion);
        Assert.Equal(DesktopTaskStatus.InProgress, client.Inner.LastTransition.TargetStatus);
        Assert.Null(client.Inner.LastTransition.Reason);
        release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(returned, 8, false));
        await request;
        Assert.Equal(1, client.Inner.TransitionCallCount);
        Assert.Equal(returned, Assert.Single(vm.Items).Source);
        Assert.Equal(returned, vm.SelectedDetails!.Source);
        Assert.True(client.WorkspaceReads > reads);
        Assert.Single(vm.Workspace!.History);
        Assert.Contains("В работе", vm.Announcement);
        Assert.Null(vm.PendingTransition);
        Assert.False(vm.CanStart);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("session")]
    [InlineData("notfound")]
    [InlineData("invalid")]
    [InlineData("validation")]
    [InlineData("server")]
    public async System.Threading.Tasks.Task Return_FailureNeverAppliesUnconfirmedStatus(string error)
    {
        var source = CreateTask(status: DesktopTaskStatus.Review);
        var client = new ReturnClient(source);
        client.Inner.TransitionResult = error switch
        {
            "forbidden" => new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
            "session" => new DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure(),
            "notfound" => new DesktopTaskWriteResult<DesktopTaskDto>.NotFound(),
            "invalid" => new DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition(),
            "validation" => new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Проверьте данные", new Dictionary<string, IReadOnlyList<string>>()),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
        };
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("InProgress");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Equal(source, Assert.Single(vm.Items).Source);
        Assert.False(string.IsNullOrWhiteSpace(vm.ScreenMessage));
        Assert.Equal(vm.ScreenMessage, vm.Announcement);
        if (error is "forbidden" or "session" or "notfound" or "invalid") Assert.False(vm.CanReturnForRework);
    }

    [Fact]
    public async System.Threading.Tasks.Task Return_ConflictOffersExistingReloadWithoutOverwriting()
    {
        var source = CreateTask(status: DesktopTaskStatus.Review);
        var client = new ReturnClient(source);
        client.Inner.TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict();
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("InProgress");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.True(vm.Editor!.HasConflict);
        Assert.True(vm.ReloadConflictCommand.CanExecute(null));
        Assert.Equal(source, vm.SelectedItem!.Source);
        var latest = source with { Version = 3, Status = DesktopTaskStatus.Completed };
        client.Inner.DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(latest);
        await vm.ReloadConflictCommand.ExecuteAsync();
        Assert.Equal(latest, vm.SelectedDetails!.Source);
        Assert.False(vm.CanReturnForRework);
    }

    [Fact]
    public async System.Threading.Tasks.Task Return_UncertainResultRetriesOriginalIntentAfterReconnect()
    {
        var source = CreateTask(status: DesktopTaskStatus.Review);
        var client = new ReturnClient(source);
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("InProgress");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        var command = client.Inner.LastTransition;
        vm.UpdateConnectivity(false);
        Assert.False(vm.ConfirmTransitionCommand.CanExecute(null));
        vm.UpdateConnectivity(true);
        Assert.Equal(1, client.Inner.TransitionCallCount);
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Same(command, client.Inner.LastTransition);
        Assert.Equal(2, client.Inner.TransitionCallCount);
        Assert.Equal(source, vm.SelectedDetails!.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Return_ClosingOrChangingSelectionIgnoresLateResponse(bool deactivate)
    {
        var source = CreateTask(status: DesktopTaskStatus.Review);
        var client = new ReturnClient(source);
        var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        client.Inner.TransitionHandler = _ => release.Task; // Simulate a response after cancellation.
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("InProgress");
        var request = vm.ConfirmTransitionCommand.ExecuteAsync();
        if (deactivate) vm.Deactivate(); else vm.SelectedItem = null;
        release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Version = 2, Status = DesktopTaskStatus.InProgress }, 2, false));
        await request;
        Assert.Equal(source, Assert.Single(vm.Items).Source);
        Assert.False(vm.IsMutationBusy);
    }

    internal sealed class ReturnClient : IDesktopTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public FakeTasksApiClient Inner { get; } = new();
        public int WorkspaceReads { get; private set; }
        public ReturnClient(DesktopTaskDto source)
        {
            Inner.EnqueuePage(SucceededPage([source]));
            Inner.DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(source);
        }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) => Inner.GetTasksAsync(cursor, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) => Inner.GetTaskByIdAsync(id, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => Inner.CreateTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => Inner.PatchTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => Inner.TransitionTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) => System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), 0, null));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token)
        {
            WorkspaceReads++;
            var body = new JsonObject { ["historyVisible"] = true };
            if (Inner.TransitionCallCount > 0) body["history"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O"), ["action"] = "TaskStatusChanged" });
            return System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(body, 1, null));
        }
        public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token) => throw new InvalidOperationException("Return must use the transition API.");
    }
}
