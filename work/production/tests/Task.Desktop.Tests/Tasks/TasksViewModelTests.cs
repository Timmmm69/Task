using System.Collections.Concurrent;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class TasksViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task InitialLoad_ShowsLocalizedItemsAndDetails()
    {
        var task = CreateTask(title: "Подготовить отчёт", status: DesktopTaskStatus.InProgress,
            priority: DesktopTaskPriority.Critical);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([task], "next-page"));
        client.DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task);
        using var viewModel = new TasksViewModel(client);

        await viewModel.ActivateAsync();
        await WaitForAsync(() => viewModel.DetailsState == TaskDetailsState.Loaded);

        Assert.Equal(TasksScreenState.Loaded, viewModel.State);
        Assert.False(viewModel.ShowBlockingState);
        Assert.False(viewModel.ShowInlineMessage);
        Assert.True(viewModel.HasSuccessfulRefresh);
        Assert.StartsWith("Последнее обновление:", viewModel.LastSuccessfulRefreshText, StringComparison.Ordinal);
        Assert.Equal("Показано задач: 1", viewModel.DisplayedCountText);
        Assert.Single(viewModel.Items);
        Assert.Equal("В работе", viewModel.Items[0].StatusText);
        Assert.Equal("Критический", viewModel.Items[0].PriorityText);
        Assert.True(viewModel.HasNextPage);
        Assert.Equal(task.Id, viewModel.SelectedDetails?.Id);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task InitialLoad_EmptyPage_ShowsEmptyState()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client);

        await viewModel.ActivateAsync();

        Assert.Equal(TasksScreenState.Empty, viewModel.State);
        Assert.Empty(viewModel.Items);
        Assert.Contains("нет", viewModel.ScreenMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.IsEmptyState);
        Assert.True(viewModel.ShowBlockingState);
        Assert.Equal("Активных задач нет", viewModel.StateTitle);
        Assert.Equal("Task.Icon.Tasks", viewModel.StateIconKey);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_ReplacesPageOnlyAfterResponseArrives()
    {
        var original = CreateTask(title: "Первая");
        var replacement = CreateTask(title: "Вторая");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([original]));
        client.EnqueuePage(async (_, cancellationToken) =>
        {
            entered.SetResult();
            return await release.Task.WaitAsync(cancellationToken);
        });
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        var refresh = viewModel.RefreshAsync();
        await entered.Task;
        Assert.Equal("Первая", Assert.Single(viewModel.Items).Title);

        release.SetResult(SucceededPage([replacement]));
        await refresh;

        Assert.Equal("Вторая", Assert.Single(viewModel.Items).Title);
        Assert.Equal(TasksScreenState.Loaded, viewModel.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoadMore_AppendsNextPage()
    {
        var first = CreateTask(title: "Первая");
        var second = CreateTask(title: "Вторая");
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([first], "cursor-1"));
        client.EnqueuePage((cursor, _) => global::System.Threading.Tasks.Task.FromResult(
            cursor == "cursor-1"
                ? SucceededPage([second])
                : new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor()));
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        var started = await viewModel.LoadNextPageAsync();

        Assert.True(started);
        Assert.Equal(["Первая", "Вторая"], viewModel.Items.Select(item => item.Title));
        Assert.False(viewModel.HasNextPage);
        Assert.Equal(TasksScreenState.Loaded, viewModel.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NextPageFailure_PreservesLoadedItems()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([CreateTask()], "cursor-1"));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        await viewModel.LoadNextPageAsync();

        Assert.Single(viewModel.Items);
        Assert.True(viewModel.HasNextPage);
        Assert.Equal(TasksScreenState.Error, viewModel.State);
        Assert.Contains("ранее загруженный", viewModel.ScreenMessage!);
        Assert.False(viewModel.ShowBlockingState);
        Assert.True(viewModel.ShowInlineMessage);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task InvalidCursor_PreservesItemsAndResetsContinuation()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([CreateTask()], "cursor-1"));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor());
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        await viewModel.LoadNextPageAsync();

        Assert.Single(viewModel.Items);
        Assert.False(viewModel.HasNextPage);
        Assert.Equal(TasksScreenState.InvalidCursor, viewModel.State);
        Assert.Contains("Обновить", viewModel.ScreenMessage!);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Forbidden_ClearsPreviouslyVisibleData()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([CreateTask()]));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Forbidden());
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Items);
        Assert.Equal(TasksScreenState.Forbidden, viewModel.State);
        Assert.True(viewModel.IsFailureState);
        Assert.True(viewModel.ShowBlockingState);
        Assert.Equal("Нет доступа к задачам", viewModel.StateTitle);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task AuthenticationFailure_ShowsSessionEndedAndClearsData()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([CreateTask()]));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure());
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Items);
        Assert.Equal(TasksScreenState.SessionEnded, viewModel.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_BlocksSecondSubmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        client.EnqueuePage(async (_, cancellationToken) =>
        {
            entered.SetResult();
            return await release.Task.WaitAsync(cancellationToken);
        });
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();

        var first = viewModel.RefreshAsync();
        await entered.Task;
        var second = await viewModel.RefreshAsync();
        release.SetResult(SucceededPage([]));

        Assert.False(second);
        Assert.True(await first);
        Assert.Equal(2, client.PageCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CancelledRefresh_ShowsCancelledState()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        client.EnqueuePage(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return SucceededPage([]);
        });
        using var viewModel = new TasksViewModel(client);
        await viewModel.ActivateAsync();
        using var cancellation = new CancellationTokenSource();

        var refresh = viewModel.RefreshAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await refresh;

        Assert.Equal(TasksScreenState.Cancelled, viewModel.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SelectingTask_NotFound_ShowsObjectDisappearedState()
    {
        var first = CreateTask(title: "Первая");
        var second = CreateTask(title: "Вторая");
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.NotFound(),
        };
        client.EnqueuePage(SucceededPage([first, second]));
        using var viewModel = new TasksViewModel(client);

        await viewModel.ActivateAsync();
        await WaitForAsync(() => viewModel.DetailsState == TaskDetailsState.NotFound);

        Assert.Null(viewModel.SelectedDetails);
        Assert.Contains("недоступна", viewModel.DetailMessage);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Navigation_ActivatesAndDeactivatesTaskScreen()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(async (_, cancellationToken) =>
        {
            entered.SetResult();
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return SucceededPage([]);
        });
        using var tasks = new TasksViewModel(client);
        using var shell = new MainWindowViewModel(null, null, tasks);

        shell.SelectedSection = shell.Sections.Single(section => section.Route == "tasks");
        await entered.Task;
        Assert.True(tasks.IsActive);

        shell.SelectedSection = shell.Sections[0];
        await WaitForAsync(() => tasks.State == TasksScreenState.Inactive);

        Assert.False(tasks.IsActive);
    }

    private static DesktopTasksApiResult<DesktopTaskPage> SucceededPage(
        IReadOnlyList<DesktopTaskDto> items,
        string? nextCursor = null) =>
        new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(
            new DesktopTaskPage(items, nextCursor, null));

    private static DesktopTaskDto CreateTask(
        string title = "Задача",
        DesktopTaskStatus status = DesktopTaskStatus.New,
        DesktopTaskPriority priority = DesktopTaskPriority.Normal) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            title,
            Guid.NewGuid(),
            status,
            priority,
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            [],
            [],
            null);

    private static async global::System.Threading.Tasks.Task WaitForAsync(
        Func<bool> condition,
        int timeoutMilliseconds = 3000)
    {
        using var cancellation = new CancellationTokenSource(timeoutMilliseconds);
        while (!condition())
        {
            await global::System.Threading.Tasks.Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class FakeTasksApiClient : IDesktopTasksApiClient
    {
        private readonly ConcurrentQueue<Func<string?, CancellationToken,
            global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>>>> _pages = new();

        public int PageCallCount { get; private set; }

        public DesktopTasksApiResult<DesktopTaskDto> DetailResult { get; set; } =
            new DesktopTasksApiResult<DesktopTaskDto>.NotFound();

        public void EnqueuePage(DesktopTasksApiResult<DesktopTaskPage> result) =>
            EnqueuePage((_, _) => global::System.Threading.Tasks.Task.FromResult(result));

        public void EnqueuePage(Func<string?, CancellationToken,
            global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>>> result) =>
            _pages.Enqueue(result);

        public global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(
            string? cursor = null,
            CancellationToken cancellationToken = default)
        {
            PageCallCount++;
            return _pages.TryDequeue(out var response)
                ? response(cursor, cancellationToken)
                : global::System.Threading.Tasks.Task.FromResult(SucceededPage([]));
        }

        public global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(DetailResult);
    }
}
