using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class InboxViewModelTests
{
    private static readonly string[] Capabilities = ["Task.Read", "Task.Create", "Task.Update", "Project.Read"];

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

    private static DesktopTasksApiResult<DesktopTaskPage> Page(params DesktopTaskDto[] tasks) =>
        new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new DesktopTaskPage(tasks, null, tasks.Length));

    private static DesktopTaskDto CreateTask(string title, TaskCardContent? card = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow,
        title, Guid.NewGuid(), DesktopTaskStatus.New, DesktopTaskPriority.Normal, null, null, [], [], null, card);

    private sealed class FakeInboxClient : IDesktopTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public DesktopTasksApiResult<DesktopTaskPage> PageResult { get; set; } = Page();
        public DesktopTaskWriteResult<DesktopTaskDto> CreateResult { get; set; } = new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public DesktopTaskWriteResult<DesktopTaskDto> PatchResult { get; set; } = new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable();
        public TaskWorkspaceResult OptionsResult { get; set; } = new(new JsonObject(), 0, null);
        public DesktopCreateTaskCommand? LastCreate { get; private set; }
        public DesktopPatchTaskCommand? LastPatch { get; private set; }

        public global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(PageResult);

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
