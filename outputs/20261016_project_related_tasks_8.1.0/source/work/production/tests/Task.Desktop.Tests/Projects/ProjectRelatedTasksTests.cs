using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Tests.TaskScreen;
using AsyncTask = System.Threading.Tasks.Task;
using PageResult = Task.Desktop.TaskApi.DesktopTasksApiResult<Task.Desktop.TaskApi.DesktopTaskPage>;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static ProjectsViewModel RelatedModel(TasksViewModelTests.FakeTasksApiClient tasks, string[]? capabilities = null)
    {
        var vm = new ProjectsViewModel(new FakeProjectsClient { Projects = SuccessPage(Project()),
            ProjectHandler = (id, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(
                new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Id = id })) }, User, capabilities ?? Capabilities, tasks);
        vm.Activate();
        return vm;
    }
    private static PageResult RelatedPage(string? cursor = null, params DesktopTaskDto[] items) => new PageResult.Succeeded(new(items, cursor, null));

    [Theory]
    [InlineData(20, 5, false)]
    [InlineData(500, 10, false)]
    [InlineData(5000, 10, false)]
    [InlineData(5000, 10, true)]
    [InlineData(500, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(20100, 10, true)] // The old 200-cursor ceiling must not silently truncate.
    public async AsyncTask RelatedTasks_ExplicitPagesPublishProgressAndOnlyEndProvesCompleteness(int total, int matches, bool late)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        await vm.CurrentDetailsOperation;
        Assert.Equal(0, tasks.PageCallCount);
        Assert.False(vm.IsDetailLoading);
        Assert.Equal("Описание", vm.SelectedItem?.Description);
        var pages = Math.Max(1, (total + 99) / 100);
        var viewed = 0;
        for (var p = 0; p < pages; p++)
        {
            var index = p;
            var release = new System.Threading.Tasks.TaskCompletionSource();
            var entered = new System.Threading.Tasks.TaskCompletionSource();
            tasks.EnqueuePage(async (cursor, ct) =>
            {
                Assert.Equal(index == 0 ? null : $"p{index}", cursor);
                entered.SetResult(); await release.Task.WaitAsync(ct);
                var items = Enumerable.Range(index * 100, Math.Min(100, total - index * 100))
                    .Select(i => Task(Guid.NewGuid(), (late ? i >= total - matches : i < matches) ? ProjectId : Guid.Empty, "Задача")).ToArray();
                viewed += items.Length;
                return RelatedPage(index + 1 < pages ? $"p{index + 1}" : null, items);
            });
            var operation = vm.LoadRelatedTasksCommand.ExecuteAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProjectTasksState.Loading, vm.RelatedTasksState);
            Assert.False(vm.IsDetailLoading);
            Assert.False(vm.LoadRelatedTasksCommand.CanExecute(null));
            Assert.False(await vm.LoadRelatedTasksCommand.ExecuteAsync());
            release.SetResult(); await operation;
            Assert.Equal(p + 1, tasks.PageCallCount);
            if (p < pages - 1)
            {
                Assert.Equal(ProjectTasksState.Partial, vm.RelatedTasksState);
                Assert.Contains("пока", vm.RelatedTasksStatusText);
                Assert.Equal(late ? 0 : matches, vm.RelatedTasks.Count);
            }
        }
        Assert.Equal(total, viewed);
        Assert.Equal(matches, vm.RelatedTasks.Count);
        Assert.Equal(matches == 0 ? ProjectTasksState.Empty : ProjectTasksState.Complete, vm.RelatedTasksState);
        Assert.False(vm.LoadRelatedTasksCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("collapse")]
    [InlineData("switch")]
    [InlineData("deactivate")]
    [InlineData("dispose")]
    [InlineData("session")]
    [InlineData("permission")]
    [InlineData("project-permission")]
    [InlineData("offline")]
    public async AsyncTask RelatedTasks_LifetimeCancelsAndIgnoresLateSuccess(string change)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        var gate = new System.Threading.Tasks.TaskCompletionSource<PageResult>();
        CancellationToken request = default;
        tasks.EnqueuePage((_, ct) => { request = ct; return gate.Task; });
        var operation = vm.LoadRelatedTasksCommand.ExecuteAsync();
        switch (change)
        {
            case "collapse": vm.CancelRelatedTasks(); break;
            case "switch": vm.SelectedItem = new ProjectItemViewModel(Project() with { Id = Guid.NewGuid() }); break;
            case "deactivate": vm.Deactivate(); break;
            case "dispose": vm.Dispose(); break;
            case "session": vm.UpdateSessionState(false); break;
            case "permission": vm.UpdateCapabilities(["Project.Read"]); break;
            case "project-permission": vm.UpdateCapabilities(["Task.Read"]); break;
            case "offline": vm.UpdateConnectivity(false); break;
        }
        Assert.True(request.IsCancellationRequested);
        gate.SetResult(RelatedPage(null, Task(Guid.NewGuid(), ProjectId, "Старый результат")));
        await operation;
        Assert.Empty(vm.RelatedTasks);
        Assert.False(vm.IsRelatedTasksLoading);
        Assert.False(vm.LoadRelatedTasksCommand.IsExecuting);
        if (change == "session") Assert.Equal(ProjectTasksState.SessionEnded, vm.RelatedTasksState);
    }

    [Fact]
    public async AsyncTask RelatedTasks_CancelResumesSameCursorAndDeduplicates()
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        var item = Task(Guid.NewGuid(), ProjectId, "Одна");
        tasks.EnqueuePage(RelatedPage("next", item));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        var gate = new System.Threading.Tasks.TaskCompletionSource();
        tasks.EnqueuePage(async (_, ct) => { await gate.Task.WaitAsync(ct); return RelatedPage(); });
        var cancelled = vm.LoadRelatedTasksCommand.ExecuteAsync();
        vm.CancelRelatedTasks(); await cancelled;
        Assert.Equal(ProjectTasksState.Cancelled, vm.RelatedTasksState);
        Assert.Single(vm.RelatedTasks);
        tasks.EnqueuePage((cursor, _) => { Assert.Equal("next", cursor); return AsyncTask.FromResult(RelatedPage(null, item, item)); });
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Equal(ProjectTasksState.Complete, vm.RelatedTasksState);
        Assert.Single(vm.RelatedTasks);
    }

    [Fact]
    public async AsyncTask RelatedTasks_IntermediateFailurePreservesProgressAndRetriesOnlyFailedPage()
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        tasks.EnqueuePage(RelatedPage("next", Task(Guid.NewGuid(), ProjectId, "Первая")));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        tasks.EnqueuePage(new PageResult.ServerUnavailable());
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Single(vm.RelatedTasks);
        Assert.Equal(ProjectTasksState.ServerUnavailable, vm.RelatedTasksState);
        Assert.Contains("Повторить", vm.RelatedTasksActionText);
        tasks.EnqueuePage((cursor, _) => { Assert.Equal("next", cursor); return AsyncTask.FromResult(RelatedPage(null, Task(Guid.NewGuid(), ProjectId, "Вторая"))); });
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Equal(2, vm.RelatedTasks.Count);
        Assert.Equal(3, tasks.PageCallCount);
        Assert.Equal(ProjectTasksState.Complete, vm.RelatedTasksState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async AsyncTask RelatedTasks_RepeatedOrCyclicCursorIsErrorAndExplicitRestartWorks(bool cycle)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        tasks.EnqueuePage(RelatedPage("one"));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        if (cycle) { tasks.EnqueuePage(RelatedPage("two")); await vm.LoadRelatedTasksCommand.ExecuteAsync(); }
        tasks.EnqueuePage(RelatedPage("one"));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Equal(ProjectTasksState.Failed, vm.RelatedTasksState);
        Assert.Contains("заново", vm.RelatedTasksActionText);
        tasks.EnqueuePage((cursor, _) => { Assert.Null(cursor); return AsyncTask.FromResult(RelatedPage()); });
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Equal(ProjectTasksState.Empty, vm.RelatedTasksState);
    }

    [Fact]
    public async AsyncTask RelatedTasks_MissingReadAndOfflineNeverMakeRequests_ReadOnlyCanRead()
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks, ["Project.Read"]);
        Assert.Equal(ProjectTasksState.Forbidden, vm.RelatedTasksState);
        Assert.False(await vm.LoadRelatedTasksCommand.ExecuteAsync());
        vm.UpdateCapabilities(["Project.Read", "Task.Read"]);
        vm.UpdateConnectivity(false);
        Assert.Equal(ProjectTasksState.Offline, vm.RelatedTasksState);
        Assert.False(await vm.LoadRelatedTasksCommand.ExecuteAsync());
        Assert.Equal(0, tasks.PageCallCount);
        vm.UpdateConnectivity(true);
        Assert.False(vm.CanUpdate);
        Assert.True(await vm.LoadRelatedTasksCommand.ExecuteAsync());
        Assert.Equal(ProjectTasksState.Empty, vm.RelatedTasksState);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("auth")]
    [InlineData("notfound")]
    [InlineData("invalidcursor")]
    [InlineData("malformed")]
    [InlineData("exception")]
    public async AsyncTask RelatedTasks_ErrorsNeverReportCompleteAndAccessLossClearsResults(string error)
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        tasks.EnqueuePage(RelatedPage("next", Task(Guid.NewGuid(), ProjectId, "Первая")));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        if (error == "exception") tasks.EnqueuePage((_, _) => throw new InvalidOperationException("private transport details"));
        else tasks.EnqueuePage(error switch
        {
            "forbidden" => new PageResult.Forbidden(), "auth" => new PageResult.AuthenticationFailure(),
            "notfound" => new PageResult.NotFound(), "invalidcursor" => new PageResult.InvalidCursor(),
            _ => new PageResult.MalformedResponse()
        });
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.DoesNotContain(vm.RelatedTasksState, new[] { ProjectTasksState.Complete, ProjectTasksState.Empty, ProjectTasksState.Loading });
        if (error is "forbidden" or "auth" or "notfound") Assert.Empty(vm.RelatedTasks);
        else Assert.Single(vm.RelatedTasks);
        if (error == "auth") { Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem); Assert.Equal(ProjectsScreenState.SessionEnded, vm.State); }
    }

    [Fact]
    public async AsyncTask RelatedTasks_SessionAndCapabilityLossClearCompletedData()
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        tasks.EnqueuePage(RelatedPage(null, Task(Guid.NewGuid(), ProjectId, "Первая")));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        vm.UpdateCapabilities(["Project.Read"]);
        Assert.Empty(vm.RelatedTasks);
        vm.UpdateCapabilities(Capabilities);
        tasks.EnqueuePage(RelatedPage(null, Task(Guid.NewGuid(), ProjectId, "Первая")));
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        vm.UpdateSessionState(false);
        Assert.Empty(vm.RelatedTasks);
        Assert.Empty(vm.Members);
        Assert.Empty(vm.Items);
        Assert.Equal(ProjectTasksState.SessionEnded, vm.RelatedTasksState);
    }

    [Fact]
    public async AsyncTask RelatedTasks_DelayedProjectCannotOverwriteNewSelectionOrEndedSession()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<DesktopProjectResult<DesktopProjectDto>>();
        CancellationToken token = default;
        var second = Project() with { Id = Guid.NewGuid(), Name = "Второй проект" };
        var client = new FakeProjectsClient { Projects = SuccessPage(Project(), second),
            ProjectHandler = (id, ct) => { if (id == second.Id) return AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.Succeeded(second)); token = ct; return gate.Task; } };
        using var vm = new ProjectsViewModel(client, User, Capabilities, new TasksViewModelTests.FakeTasksApiClient());
        vm.Activate();
        var old = vm.CurrentDetailsOperation;
        Assert.True(vm.IsDetailLoading);
        vm.SelectedItem = vm.Items[1];
        await vm.CurrentDetailsOperation;
        Assert.True(token.IsCancellationRequested);
        Assert.False(vm.IsDetailLoading);
        vm.UpdateSessionState(false);
        gate.SetResult(new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project()));
        await old;
        Assert.Empty(vm.Items);
        Assert.Empty(vm.Members);
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async AsyncTask RelatedTasks_SwitchToBThenReturnToAStartsFreshAndDoesNotUseLateCursor()
    {
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = RelatedModel(tasks);
        var a = vm.SelectedItem;
        var gate = new System.Threading.Tasks.TaskCompletionSource<PageResult>();
        tasks.EnqueuePage((_, _) => gate.Task);
        var old = vm.LoadRelatedTasksCommand.ExecuteAsync();
        vm.SelectedItem = new ProjectItemViewModel(Project() with { Id = Guid.NewGuid() });
        Assert.NotEqual(ProjectId, vm.SelectedItem.Id);
        vm.SelectedItem = a;
        gate.SetResult(RelatedPage("stale", Task(Guid.NewGuid(), ProjectId, "Старая")));
        await old;
        tasks.EnqueuePage((cursor, _) => { Assert.Null(cursor); return AsyncTask.FromResult(RelatedPage(null, Task(Guid.NewGuid(), ProjectId, "Новая"))); });
        await vm.LoadRelatedTasksCommand.ExecuteAsync();
        Assert.Equal("Новая", Assert.Single(vm.RelatedTasks).Title);
    }

    [Fact]
    public void RelatedTasks_ConnectivityCannotOverrideEndedSession()
    {
        using var vm = RelatedModel(new TasksViewModelTests.FakeTasksApiClient());
        vm.UpdateSessionState(false);
        vm.UpdateConnectivity(false);
        Assert.Equal(ProjectTasksState.SessionEnded, vm.RelatedTasksState);
        Assert.False(vm.LoadRelatedTasksCommand.CanExecute(null));
    }
}
