using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Today;

public sealed partial class TodayViewModelTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public async System.Threading.Tasks.Task Summary_LoadsAllPagesAndExcludesTerminalAndUnrelatedTasks()
    {
        var tasks = new SummaryClient();
        var overdue = SummaryTask("Overdue", User, [User], Clock().AddMinutes(-1));
        var waiting = SummaryTask("Waiting", User, [Other], null);
        var review = SummaryTask("Review", Other, [User], null) with { Status = DesktopTaskStatus.Review };
        tasks.Pages.Enqueue(Page([overdue, SummaryTask("Unrelated", Other, [Other], Clock().AddDays(-1))], "next"));
        tasks.Pages.Enqueue(Page([waiting, review, overdue with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.Completed },
            overdue with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.Cancelled }], null));
        using var vm = SummaryVm(tasks);
        await vm.ActivateAsync();
        Assert.Equal(new string?[] { null, "next" }, tasks.Cursors);
        Assert.Equal(overdue.Id, Assert.Single(vm.OverdueTasks).Id);
        Assert.Equal(waiting.Id, Assert.Single(vm.WaitingTasks).Id);
        Assert.Equal(review.Id, Assert.Single(vm.ReviewTasks).Id);
        Assert.Equal(TodayScreenState.Loaded, vm.State);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_TaskReadWorksWithoutCalendarRead()
    {
        var tasks = new SummaryClient();
        tasks.Pages.Enqueue(Page([SummaryTask("Late", User, [User], Clock().AddDays(-1))], null));
        var calendar = new FakeCalendarClient();
        using var vm = new TodayViewModel(calendar, ["Task.Read"], TimeZoneInfo.Utc, Clock, tasks, User);
        await vm.ActivateAsync();
        Assert.Single(vm.OverdueTasks);
        Assert.Equal(0, calendar.ScheduleCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_RepeatedCursorDoesNotPublishPartialData()
    {
        var tasks = new SummaryClient();
        tasks.Pages.Enqueue(Page([SummaryTask("Late", User, [User], Clock().AddDays(-1))], "same"));
        tasks.Pages.Enqueue(Page([], "same"));
        using var vm = SummaryVm(tasks);
        await vm.ActivateAsync();
        Assert.Empty(vm.OverdueTasks);
        Assert.Contains("повторил", vm.TasksMessage);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_PermissionRevocationRejectsLateResponse()
    {
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        var tasks = new SummaryClient { Pending = pending.Task };
        using var vm = SummaryVm(tasks);
        var load = vm.ActivateAsync();
        vm.UpdateCapabilities([]);
        pending.SetResult(Page([SummaryTask("Private", User, [User], Clock().AddDays(-1))], null));
        await load;
        Assert.Empty(vm.OverdueTasks);
        Assert.Equal(TodayScreenState.Forbidden, vm.State);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_SessionLossClearsAndRejectsLateResponse()
    {
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        var tasks = new SummaryClient { Pending = pending.Task };
        using var vm = SummaryVm(tasks);
        var load = vm.ActivateAsync();
        vm.UpdateSessionState(false);
        pending.SetResult(Page([SummaryTask("Private", User, [User], Clock().AddDays(-1))], null));
        await load;
        Assert.False(vm.HasItems);
        Assert.Equal(TodayScreenState.SessionEnded, vm.State);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_RefreshRetainsConfirmedDataOnFailure()
    {
        var tasks = new SummaryClient();
        tasks.Pages.Enqueue(Page([SummaryTask("Late", User, [User], Clock().AddDays(-1))], null));
        tasks.Pages.Enqueue(new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        using var vm = SummaryVm(tasks);
        await vm.ActivateAsync();
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.OverdueTasks);
        Assert.NotNull(vm.TasksMessage);
        Assert.Equal(TodayScreenState.Error, vm.State);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_OpenCommandNavigatesToExistingTaskCard()
    {
        var task = SummaryTask("Открыть из Сегодня", User, [User], Clock().AddDays(-1));
        var summary = new SummaryClient();
        summary.Pages.Enqueue(Page([task], null));
        using var today = SummaryVm(summary);
        var client = new Task.Desktop.Tests.TaskScreen.TasksViewModelTests.FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task)
        };
        using var tasks = new TasksViewModel(client, ["Task.Read"]);
        using var shell = new MainWindowViewModel(null, null, tasks, today: today);
        await today.OpenItemCommand.ExecuteAsync(Assert.Single(today.OverdueTasks));
        Assert.True(shell.IsTasksSectionSelected);
        Assert.Equal(task.Id, tasks.SelectedDetails?.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task Summary_CompletedTaskDisappearsAfterRefresh()
    {
        var task = SummaryTask("Complete", User, [User], Clock().AddDays(-1));
        var tasks = new SummaryClient();
        tasks.Pages.Enqueue(Page([task], null));
        tasks.Pages.Enqueue(Page([task with { Status = DesktopTaskStatus.Completed }], null));
        using var vm = SummaryVm(tasks);
        await vm.ActivateAsync();
        Assert.Single(vm.OverdueTasks);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Empty(vm.OverdueTasks);
    }

    private static TodayViewModel SummaryVm(SummaryClient tasks) =>
        new(new FakeCalendarClient(), ["Task.Read"], TimeZoneInfo.Utc, Clock, tasks, User);
    private static DesktopTaskDto SummaryTask(string title, Guid author, Guid[] assignees, DateTimeOffset? deadline) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, title, author, DesktopTaskStatus.New,
            DesktopTaskPriority.Normal, null, deadline, assignees, [], null);
    private static DesktopTasksApiResult<DesktopTaskPage> Page(DesktopTaskDto[] tasks, string? cursor) =>
        new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(tasks, cursor, null));

    private sealed class SummaryClient : IDesktopTasksApiClient
    {
        public Queue<DesktopTasksApiResult<DesktopTaskPage>> Pages { get; } = new();
        public List<string?> Cursors { get; } = [];
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>>? Pending { get; init; }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default)
        {
            Cursors.Add(cursor);
            return Pending ?? System.Threading.Tasks.Task.FromResult(Pages.Dequeue());
        }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
