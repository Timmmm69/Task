using System.Collections.Concurrent;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class TasksViewModelTests
{
    private static readonly string[] WriteCapabilities =
        ["Task.Read", "Task.Create", "Task.Update", "Task.ChangeStatus"];

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

    [Fact]
    public async global::System.Threading.Tasks.Task Create_Success_ReconcilesListSelectionAndDetails()
    {
        var created = CreateTask(title: "Новая с сервера");
        var client = new FakeTasksApiClient
        {
            CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, created.Version, false),
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(created),
        };
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();

        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Новая с сервера";
        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Single(viewModel.Items);
        Assert.Equal(created.Id, viewModel.SelectedItem?.Id);
        Assert.Equal(created.Id, viewModel.SelectedDetails?.Id);
        Assert.Null(viewModel.Editor);
        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Edit_NoChanges_DoesNotSendRequest()
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task) };
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await WaitForAsync(() => viewModel.SelectedDetails is not null);

        await viewModel.EditTaskCommand.ExecuteAsync();
        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Equal(0, client.PatchCallCount);
        Assert.Contains("Нет изменений", viewModel.Editor!.StatusMessage);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Edit_SendsOnlyChangedFieldsAndUsesFreshVersion()
    {
        var task = CreateTask(title: "До") with { Version = 7 };
        var updated = task with { Title = "После", Version = 8 };
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(updated, 8, false),
        };
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.EditTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "После";

        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Equal(7, client.LastPatch!.ExpectedVersion);
        Assert.True(client.LastPatch.Title.IsSpecified);
        Assert.False(client.LastPatch.Priority.IsSpecified);
        Assert.False(client.LastPatch.StartAtUtc.IsSpecified);
        Assert.Equal("После", viewModel.SelectedItem!.Title);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task VersionConflict_PreservesDraftAndReloadsLatest()
    {
        var task = CreateTask(title: "Старая") with { Version = 2 };
        var latest = task with { Title = "Актуальная", Version = 3 };
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(latest),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict(),
        };
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.EditTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Моя правка";

        await viewModel.SaveEditorCommand.ExecuteAsync();
        Assert.True(viewModel.Editor.HasConflict);
        Assert.Equal("Моя правка", viewModel.Editor.Title);

        await viewModel.ReloadConflictCommand.ExecuteAsync();
        Assert.Equal("Актуальная", viewModel.Editor!.Title);
        Assert.False(viewModel.Editor.HasConflict);
        Assert.Equal(3, viewModel.SelectedItem!.Source.Version);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RequestInProgress_RetryReusesSameIdempotencyKey()
    {
        var created = CreateTask();
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        client.CreateResults.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress());
        client.CreateResults.Enqueue(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, true));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Повтор";

        await viewModel.SaveEditorCommand.ExecuteAsync();
        await WaitForAsync(() => viewModel.Editor!.RetryAvailable, 4000);
        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Equal(2, client.CreateCommands.Count);
        Assert.Equal(client.CreateCommands[0].IdempotencyKey, client.CreateCommands[1].IdempotencyKey);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Transition_Success_UsesSelectedVersionAndServerResponse()
    {
        var task = CreateTask() with { Version = 4 };
        var started = task with { Status = DesktopTaskStatus.InProgress, Version = 5 };
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task),
            TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(started, 5, false),
        };
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();

        await viewModel.TransitionCommand.ExecuteAsync("InProgress");
        await viewModel.ConfirmTransitionCommand.ExecuteAsync();

        Assert.Equal(4, client.LastTransition!.ExpectedVersion);
        Assert.Equal(DesktopTaskStatus.InProgress, viewModel.SelectedItem!.Source.Status);
        Assert.Equal(5, viewModel.SelectedDetails is null ? 0 : viewModel.SelectedItem.Source.Version);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task MissingWriteCapabilities_DisablesAllWriteCommands()
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, ["Task.Read"]);
        await viewModel.ActivateAsync();

        Assert.False(viewModel.NewTaskCommand.CanExecute(null));
        Assert.False(viewModel.EditTaskCommand.CanExecute(null));
        Assert.False(viewModel.TransitionCommand.CanExecute("InProgress"));
        Assert.True(viewModel.IsReadOnly);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Create_DoubleSubmit_IsSingleFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient
        {
            CreateHandler = async (_, token) =>
            {
                entered.TrySetResult();
                return await release.Task.WaitAsync(token);
            },
        };
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Одна команда";

        var first = viewModel.SaveEditorCommand.ExecuteAsync();
        await entered.Task;
        var second = await viewModel.SaveEditorCommand.ExecuteAsync();
        release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(CreateTask(), 1, false));
        await first;

        Assert.False(second);
        Assert.Equal(1, client.CreateCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Disposal_CancelsMutationAndSuppressesStaleResponse()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient
        {
            CreateHandler = async (_, token) =>
            {
                entered.TrySetResult();
                await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(CreateTask(), 1, false);
            },
        };
        client.EnqueuePage(SucceededPage([]));
        var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Не применять";

        var save = viewModel.SaveEditorCommand.ExecuteAsync();
        await entered.Task;
        viewModel.Dispose();
        await save;

        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.IsActive);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ForbiddenWrite_PreservesDraftAndDisablesFurtherWrites()
    {
        var client = new FakeTasksApiClient
        {
            CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
        };
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Сохранённый черновик";

        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Equal("Сохранённый черновик", viewModel.Editor.Title);
        Assert.True(viewModel.IsReadOnly);
        Assert.False(viewModel.NewTaskCommand.CanExecute(null));
        Assert.Contains("Права", viewModel.ScreenMessage);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EmptyPage_CreateCommandTracksActivationAndRaisesCanExecuteChanged()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, ["Task.Create"]);
        var changes = 0;
        viewModel.NewTaskCommand.CanExecuteChanged += (_, _) => changes++;

        Assert.False(viewModel.NewTaskCommand.CanExecute(null));
        await viewModel.ActivateAsync();
        Assert.True(viewModel.NewTaskCommand.CanExecute(null));

        viewModel.Deactivate();
        Assert.False(viewModel.NewTaskCommand.CanExecute(null));
        await viewModel.ActivateAsync();

        Assert.True(viewModel.NewTaskCommand.CanExecute(null));
        Assert.True(changes >= 3);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CapabilityGrant_UpdatesWriteStateWithoutRecreatingViewModel()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, ["Task.Read"]);
        await viewModel.ActivateAsync();
        var previousText = viewModel.WriteAccessText;

        viewModel.UpdateCapabilities(["Task.Read", "Task.Create"]);

        Assert.False(viewModel.IsReadOnly);
        Assert.True(viewModel.NewTaskCommand.CanExecute(null));
        Assert.NotEqual(previousText, viewModel.WriteAccessText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CapabilityRevoke_PreservesCreateDraftAndBlocksSubmission()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.NewTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Черновик после отзыва";

        viewModel.UpdateCapabilities(["Task.Read"]);
        var submitted = await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.Equal("Черновик после отзыва", viewModel.Editor.Title);
        Assert.False(viewModel.SaveEditorCommand.CanExecute(null));
        Assert.False(submitted);
        Assert.Equal(0, client.CreateCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EditSave_RequiresCurrentUpdateCapability()
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task),
        };
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.EditTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Изменённый черновик";

        viewModel.UpdateCapabilities(["Task.Read", "Task.Create"]);
        await viewModel.SaveEditorCommand.ExecuteAsync();

        Assert.False(viewModel.SaveEditorCommand.CanExecute(null));
        Assert.Equal(0, client.PatchCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransitionRevoke_CancelsPendingConfirmationAndBlocksApiCall()
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([task]));
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.TransitionCommand.ExecuteAsync("InProgress");
        Assert.True(viewModel.PendingTransition.HasValue);

        viewModel.UpdateCapabilities(["Task.Read", "Task.Create", "Task.Update"]);
        await viewModel.ConfirmTransitionCommand.ExecuteAsync();

        Assert.Null(viewModel.PendingTransition);
        Assert.False(viewModel.ConfirmTransitionCommand.CanExecute(null));
        Assert.Equal(0, client.TransitionCallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async global::System.Threading.Tasks.Task TerminalReadFailure_DisablesAllMutationCommands(
        bool sessionEnded)
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task),
        };
        client.EnqueuePage(SucceededPage([task]));
        client.EnqueuePage(sessionEnded
            ? new DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure()
            : new DesktopTasksApiResult<DesktopTaskPage>.Forbidden());
        using var viewModel = new TasksViewModel(client, WriteCapabilities);
        await viewModel.ActivateAsync();
        await viewModel.EditTaskCommand.ExecuteAsync();
        viewModel.Editor!.Title = "Сохранённый черновик";
        await viewModel.TransitionCommand.ExecuteAsync("InProgress");
        var saveChanges = 0;
        var confirmChanges = 0;
        viewModel.SaveEditorCommand.CanExecuteChanged += (_, _) => saveChanges++;
        viewModel.ConfirmTransitionCommand.CanExecuteChanged += (_, _) => confirmChanges++;

        await viewModel.RefreshAsync();

        Assert.Equal(sessionEnded ? TasksScreenState.SessionEnded : TasksScreenState.Forbidden, viewModel.State);
        Assert.False(viewModel.SaveEditorCommand.CanExecute(null));
        Assert.False(viewModel.ConfirmTransitionCommand.CanExecute(null));
        Assert.True(saveChanges > 0);
        Assert.True(confirmChanges > 0);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OpenById_OpensTaskOutsideFirstPageForReadOnlyUser()
    {
        var task = CreateTask("Из сводки Сегодня");
        var client = new FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task) };
        client.EnqueuePage(SucceededPage([]));
        using var vm = new TasksViewModel(client, ["Task.Read"]);
        await vm.ActivateAsync();
        await vm.OpenByIdAsync(task.Id);
        Assert.Equal(task.Id, vm.SelectedItem?.Id);
        Assert.Contains(vm.SelectedItem!, vm.Items);
        Assert.Equal(task.Id, vm.SelectedDetails?.Id);
        Assert.Equal(TasksScreenState.Loaded, vm.State);
        Assert.True(vm.IsReadOnly);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OpenById_PreservesExistingDraft()
    {
        var task = CreateTask();
        var client = new FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task) };
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        await vm.EditTaskCommand.ExecuteAsync();
        vm.Editor!.Title = "Несохранённый черновик";
        await vm.OpenByIdAsync(Guid.NewGuid());
        Assert.Equal("Несохранённый черновик", vm.Editor.Title);
        Assert.Equal(task.Id, vm.SelectedItem?.Id);
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

    internal sealed class FakeTasksApiClient : IDesktopTasksApiClient
    {
        private readonly ConcurrentQueue<Func<string?, CancellationToken,
            global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>>>> _pages = new();

        public int PageCallCount { get; private set; }
        public int CreateCallCount { get; private set; }
        public int PatchCallCount { get; private set; }
        public int TransitionCallCount { get; private set; }
        public List<DesktopCreateTaskCommand> CreateCommands { get; } = [];
        public ConcurrentQueue<DesktopTaskWriteResult<DesktopTaskDto>> CreateResults { get; } = new();
        public DesktopPatchTaskCommand? LastPatch { get; private set; }
        public DesktopTransitionTaskCommand? LastTransition { get; private set; }
        public DesktopTaskWriteResult<DesktopTaskDto> CreateResult { get; set; } =
            new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public DesktopTaskWriteResult<DesktopTaskDto> PatchResult { get; set; } =
            new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public DesktopTaskWriteResult<DesktopTaskDto> TransitionResult { get; set; } =
            new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public Func<DesktopCreateTaskCommand, CancellationToken,
            global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>>>? CreateHandler
        { get; init; }

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

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(
            DesktopCreateTaskCommand command,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            CreateCommands.Add(command);
            return CreateHandler is not null
                ? CreateHandler(command, cancellationToken)
                : global::System.Threading.Tasks.Task.FromResult(
                    CreateResults.TryDequeue(out var result) ? result : CreateResult);
        }

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(
            DesktopPatchTaskCommand command,
            CancellationToken cancellationToken = default)
        {
            PatchCallCount++;
            LastPatch = command;
            return global::System.Threading.Tasks.Task.FromResult(PatchResult);
        }

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(
            DesktopTransitionTaskCommand command,
            CancellationToken cancellationToken = default)
        {
            TransitionCallCount++;
            LastTransition = command;
            return global::System.Threading.Tasks.Task.FromResult(TransitionResult);
        }
    }
}
