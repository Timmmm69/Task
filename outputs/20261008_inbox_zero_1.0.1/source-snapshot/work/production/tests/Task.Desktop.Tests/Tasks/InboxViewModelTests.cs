using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class InboxViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task ModeSwitchCancel_PreservesCaptureAndConversionDraft()
    {
        var source = CreateTask("Запись");
        var client = new FakeInboxClient { PageResult = Page(source) };
        using var inbox = new InboxViewModel(client, Capabilities);
        using var shell = new MainWindowViewModel(null, null, inbox: inbox);
        await inbox.ActivateAsync();
        await inbox.ConvertCommand.ExecuteAsync(inbox.SelectedItem);
        var editor = inbox.Conversion!;
        editor.Title = "Черновик преобразования";
        inbox.CaptureText = "Черновик новой записи";
        await ApplicationModeTests.AssertCancelledAsync(shell);
        Assert.Same(editor, inbox.Conversion);
        Assert.Equal("Черновик преобразования", inbox.Conversion!.Title);
        Assert.Equal("Черновик новой записи", inbox.CaptureText);
    }
    internal static readonly string[] Capabilities = ["Task.Read", "Task.Create", "Task.Update", "Project.Read"];

    [Fact]
    public async global::System.Threading.Tasks.Task ActivationShowsOnlyCaptureOnlyServerTasks()
    {
        var capture = CreateTask("Необработанная запись");
        var classified = CreateTask("Обычная задача", card: new TaskCardContent { Description = "Есть карточка" });
        var client = new FakeInboxClient
        {
            PageResult = Page(capture, classified),
        };
        using var viewModel = new InboxViewModel(client, Capabilities);

        await viewModel.ActivateAsync();

        var item = Assert.Single(viewModel.Items);
        Assert.Equal(capture.Id, item.Id);
        Assert.Equal("Необработанная запись", item.Title);
        Assert.Equal(InboxScreenState.Loaded, viewModel.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task QuickCaptureCreatesRealUnclassifiedTask()
    {
        var created = CreateTask("Длинная входящая запись для проверки русской строки", new TaskCardContent());
        var client = new FakeInboxClient
        {
            PageResult = Page(),
            CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, created.Version, false),
        };
        using var viewModel = new InboxViewModel(client, Capabilities);
        await viewModel.ActivateAsync();
        viewModel.CaptureText = created.Title;

        await viewModel.CaptureCommand.ExecuteAsync();

        Assert.Equal(created.Title, Assert.Single(viewModel.Items).Title);
        Assert.NotNull(client.LastCreate);
        Assert.Null(client.LastCreate!.Card);
        Assert.Equal(DesktopTaskPriority.Normal, client.LastCreate.Priority);
        Assert.Equal(string.Empty, viewModel.CaptureText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ConversionPatchesSameTaskAndClosesInboxSource()
    {
        var projectId = Guid.NewGuid();
        var source = CreateTask("Согласовать сценарии usability", new TaskCardContent());
        var converted = source with
        {
            Version = 2,
            DeadlineAtUtc = DateTimeOffset.UtcNow.AddDays(3),
            Card = new TaskCardContent { ProjectId = projectId },
        };
        var client = new FakeInboxClient
        {
            PageResult = Page(source),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(converted, converted.Version, false),
            OptionsResult = new TaskWorkspaceResult(new JsonObject
            {
                ["projects"] = new JsonArray(new JsonObject { ["id"] = projectId.ToString("D"), ["name"] = "Отчётность" }),
            }, 0, null),
        };
        using var viewModel = new InboxViewModel(client, Capabilities);
        await viewModel.ActivateAsync();

        await viewModel.ConvertCommand.ExecuteAsync(viewModel.SelectedItem);
        Assert.NotNull(viewModel.Conversion);
        viewModel.Conversion!.Card.Project = viewModel.Conversion.Card.Projects.Single(project => project.Id == projectId);
        viewModel.Conversion.DeadlineText = DateTime.Now.AddDays(3).ToString("dd.MM.yyyy HH:mm");
        await viewModel.SaveConversionCommand.ExecuteAsync();

        Assert.NotNull(client.LastPatch);
        Assert.Equal(source.Id, client.LastPatch!.Id);
        Assert.NotNull(client.LastPatch.CardPatch);
        Assert.True(client.LastPatch.DeadlineAtUtc.IsSpecified);
        Assert.Empty(viewModel.Items);
        Assert.Null(viewModel.Conversion);
        Assert.Equal(InboxScreenState.Empty, viewModel.State);
        Assert.Contains("Связь сохранена", viewModel.ScreenMessage, StringComparison.Ordinal);
        Assert.True(viewModel.ShowInboxZero);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OfflineKeepsConfirmedItemsAndDisablesWrites()
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Сохранённая запись")) };
        using var viewModel = new InboxViewModel(client, Capabilities);
        await viewModel.ActivateAsync();

        viewModel.UpdateConnectivity(false);

        Assert.Single(viewModel.Items);
        Assert.True(viewModel.IsReadOnly);
        Assert.False(viewModel.CaptureCommand.CanExecute(null));
        Assert.Equal(InboxScreenState.Offline, viewModel.State);
    }

    internal static DesktopTasksApiResult<DesktopTaskPage> Page(params DesktopTaskDto[] tasks) =>
        new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new DesktopTaskPage(tasks, null, tasks.Length));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async global::System.Threading.Tasks.Task LastConversion_WaitsForRefresh_AndDoesNotHideConcurrentAddOrError(bool concurrentAdd)
    {
        var source = CreateTask("Последняя запись");
        var client = new FakeInboxClient
        {
            PageResult = Page(source),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { DeadlineAtUtc = DateTimeOffset.UtcNow.AddDays(1) }, 2, false)
        };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
        model.Conversion!.DeadlineText = DateTime.Now.AddDays(1).ToString("dd.MM.yyyy HH:mm");
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        client.Read = (_, _) => pending.Task;
        var save = model.SaveConversionCommand.ExecuteAsync();
        Assert.True(model.IsBusy);
        Assert.True(model.IsRefreshing);
        Assert.False(model.ShowInboxZero);
        pending.SetResult(concurrentAdd ? Page(CreateTask("Новая запись другого клиента")) : new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        await save;
        Assert.False(model.ShowInboxZero);
        Assert.Equal(concurrentAdd ? InboxScreenState.Loaded : InboxScreenState.Offline, model.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CaptureFailure_DoesNotLeavePositiveZeroBehindError()
    {
        var client = new FakeInboxClient();
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        model.CaptureText = "Не потерять текст";
        await model.CaptureCommand.ExecuteAsync();
        Assert.False(model.ShowInboxZero);
        Assert.Equal("Не потерять текст", model.CaptureText);
        Assert.True(model.IsReadOnly);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CaptureDraft_IsNotAnInboxFilter()
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Запись")) };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        model.CaptureText = "Нет совпадений";
        Assert.Single(model.Items);
        Assert.False(model.ShowInboxZero);
        client.PageResult = Page();
        await model.RefreshCommand.ExecuteAsync();
        Assert.True(model.ShowInboxZero);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_PreventsCaptureFromBeingOverwrittenByOlderSnapshot()
    {
        var client = new FakeInboxClient();
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        model.CaptureText = "Новая запись";
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        client.Read = (_, _) => pending.Task;
        var refresh = model.RefreshCommand.ExecuteAsync();
        Assert.False(model.CanCapture);
        await model.CaptureCommand.ExecuteAsync();
        Assert.Null(client.LastCreate);
        pending.SetResult(Page());
        await refresh;
        Assert.True(model.CanCapture);
        Assert.Equal("Новая запись", model.CaptureText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task InitialEmpty_IsConfirmedOnlyAfterLoad()
    {
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        var client = new FakeInboxClient { Read = (_, _) => pending.Task };
        using var model = new InboxViewModel(client, Capabilities);
        Assert.False(model.ShowInboxZero);
        var load = model.ActivateAsync();
        Assert.True(model.IsInitialLoading);
        Assert.False(model.ShowInboxZero);
        pending.SetResult(Page());
        await load;
        Assert.True(model.ShowInboxZero);
        Assert.False(model.IsInitialLoading);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async global::System.Threading.Tasks.Task FirstPageWithoutInbox_LoadsRemainingPages(bool emptyFirstPage)
    {
        var item = CreateTask("Поздняя запись");
        var first = emptyFirstPage ? Array.Empty<DesktopTaskDto>() : [CreateTask("Задача", new TaskCardContent { Description = "Разобрана" })];
        var seen = new List<string?>();
        var client = new FakeInboxClient
        {
            Read = (cursor, _) =>
        {
            seen.Add(cursor);
            return global::System.Threading.Tasks.Task.FromResult<DesktopTasksApiResult<DesktopTaskPage>>(
                new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(cursor is null
                    ? new(first, "next", first.Length + 1) : new([item], null, first.Length + 1)));
        }
        };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        Assert.Equal(new string?[] { null, "next" }, seen);
        Assert.Equal(item.Id, Assert.Single(model.Items).Id);
        Assert.False(model.ShowInboxZero);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("repeat")]
    [InlineData("failure")]
    public async global::System.Threading.Tasks.Task IncompletePagination_NeverClaimsZero(string kind)
    {
        var calls = 0;
        var client = new FakeInboxClient
        {
            Read = (_, _) =>
        {
            calls++;
            DesktopTasksApiResult<DesktopTaskPage> result = kind == "failure" && calls > 1
                ? new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable()
                : new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([], kind == "missing" ? null : "next", 1));
            return global::System.Threading.Tasks.Task.FromResult(result);
        }
        };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        Assert.False(model.ShowInboxZero);
        Assert.True(model.ShowBlockingState);
        Assert.InRange(calls, 1, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async global::System.Threading.Tasks.Task OfflineOrFailedRefresh_HidesPreviousZero(bool offline)
    {
        var client = new FakeInboxClient();
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        Assert.True(model.ShowInboxZero);
        if (offline) model.UpdateConnectivity(false);
        else
        {
            client.Read = (_, _) => throw new InvalidOperationException();
            await model.RefreshCommand.ExecuteAsync();
        }
        Assert.False(model.ShowInboxZero);
        Assert.True(model.ShowBlockingState);
        Assert.Equal(offline ? InboxScreenState.Offline : InboxScreenState.Error, model.State);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OfflineDuringLoad_IgnoresLateSuccess()
    {
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        using var model = new InboxViewModel(new FakeInboxClient { Read = (_, _) => pending.Task }, Capabilities);
        var load = model.ActivateAsync();
        model.UpdateConnectivity(false);
        pending.SetResult(Page());
        await load;
        Assert.Equal(InboxScreenState.Offline, model.State);
        Assert.False(model.ShowInboxZero);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ExternalAddThenDelete_AuthoritativeRefreshEntersAndLeavesZero()
    {
        var client = new FakeInboxClient();
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        client.PageResult = Page(CreateTask("Добавлено другим клиентом"));
        await model.RefreshCommand.ExecuteAsync();
        Assert.False(model.ShowInboxZero);
        Assert.Single(model.Items);
        var pending = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>();
        client.Read = (_, _) => pending.Task;
        var refresh = model.RefreshCommand.ExecuteAsync();
        Assert.False(model.ShowInboxZero);
        Assert.Single(model.Items);
        pending.SetResult(Page());
        await refresh;
        Assert.True(model.ShowInboxZero);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task StaleItems_AreRetainedWhenRefreshFails()
    {
        var client = new FakeInboxClient { PageResult = Page(CreateTask("Кеш")) };
        using var model = new InboxViewModel(client, Capabilities);
        await model.ActivateAsync();
        client.PageResult = new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable();
        await model.RefreshCommand.ExecuteAsync();
        Assert.Single(model.Items);
        Assert.False(model.ShowInboxZero);
        Assert.Contains("ранее загруженные", model.ScreenMessage);
    }

    internal static DesktopTaskDto CreateTask(string title, TaskCardContent? card = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
        title, Guid.NewGuid(), DesktopTaskStatus.New, DesktopTaskPriority.Normal, null, null, [], [], null, card);

    internal sealed class FakeInboxClient : IDesktopTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public DesktopTasksApiResult<DesktopTaskPage> PageResult { get; set; } = Page();
        public DesktopTaskWriteResult<DesktopTaskDto> CreateResult { get; set; } = new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public DesktopTaskWriteResult<DesktopTaskDto> PatchResult { get; set; } = new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public TaskWorkspaceResult OptionsResult { get; set; } = new(new JsonObject(), 0, null);
        public DesktopCreateTaskCommand? LastCreate { get; private set; }
        public DesktopPatchTaskCommand? LastPatch { get; private set; }
        public Func<string?, CancellationToken, global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>>>? Read { get; set; }

        public global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            Read?.Invoke(cursor, cancellationToken) ?? global::System.Threading.Tasks.Task.FromResult(PageResult);

        public global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<DesktopTasksApiResult<DesktopTaskDto>>(new DesktopTasksApiResult<DesktopTaskDto>.NotFound());

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default)
        {
            LastCreate = command;
            return global::System.Threading.Tasks.Task.FromResult(CreateResult);
        }

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default)
        {
            LastPatch = command;
            if (PatchResult is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success)
                PageResult = Page(success.Value);
            return global::System.Threading.Tasks.Task.FromResult(PatchResult);
        }

        public global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<DesktopTaskWriteResult<DesktopTaskDto>>(new DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition());

        public global::System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) =>
            global::System.Threading.Tasks.Task.FromResult(OptionsResult);

        public global::System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) =>
            global::System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), 0, null));

        public global::System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token) =>
            global::System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), version, null));
    }
}
