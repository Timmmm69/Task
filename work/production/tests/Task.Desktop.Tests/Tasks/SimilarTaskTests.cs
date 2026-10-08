using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class SimilarTaskTests
{
    private static readonly string[] Capabilities = ["Task.Read", "Task.Create", "Task.Assign", "Task.Watch"];
    private static DesktopTaskDto Source(DesktopTaskStatus status = DesktopTaskStatus.New) => new(
        Guid.NewGuid(), Guid.NewGuid(), 99, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        "Договор", Guid.NewGuid(), status, DesktopTaskPriority.High, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddDays(3), [Guid.NewGuid()], [Guid.NewGuid()], Guid.NewGuid(),
        new TaskCardContent { Description = "Подготовить", PlannedDurationMinutes = 45,
            ProjectId = Guid.NewGuid(), PrimaryCounterpartyObjectId = Guid.NewGuid(),
            ParentTaskId = Guid.NewGuid(), RequesterUserId = Guid.NewGuid(),
            ScheduledDate = new(2026, 10, 8), StartTimeLocal = new(10, 0), ScheduleTimeZone = "Europe/Minsk" },
        status == DesktopTaskStatus.Completed ? DateTimeOffset.UtcNow : null);

    private static JsonObject Options(DesktopTaskDto source) => new()
    {
        ["projects"] = new JsonArray(new JsonObject { ["id"] = source.Card!.ProjectId?.ToString(), ["name"] = "Проект" }),
        ["counterparties"] = new JsonArray(new JsonObject { ["id"] = source.Card.PrimaryCounterpartyObjectId?.ToString(), ["name"] = "Компания" }),
        ["people"] = new JsonArray(source.AssigneeIds.Concat(source.WatcherIds)
            .Select(id => (JsonNode)new JsonObject { ["id"] = id.ToString(), ["name"] = "Участник" }).ToArray())
    };

    [Theory]
    [InlineData(DesktopTaskStatus.New)]
    [InlineData(DesktopTaskStatus.Completed)]
    [InlineData(DesktopTaskStatus.Cancelled)]
    public void Mapping_OnlySafeCreateFields_NoScheduleOrIdentity(DesktopTaskStatus status)
    {
        var source = Source(status);
        var editor = TaskEditorViewModel.FromSimilar(source, Options(source), true, true);
        var command = editor.BuildCreateCommand()!;
        Assert.Equal(TaskEditorMode.Create, editor.Mode);
        Assert.Null(editor.SourceId);
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal(source.Title, command.Title);
        Assert.Equal(source.Priority, command.Priority);
        Assert.Null(command.StartAtUtc); Assert.Null(command.DeadlineAtUtc);
        var card = command.Card!;
        Assert.Equal(source.Card!.Description, card.Description);
        Assert.Equal(45, card.PlannedDurationMinutes);
        Assert.Equal(source.Card.ProjectId, card.ProjectId);
        Assert.Equal(source.Card.PrimaryCounterpartyObjectId, card.PrimaryCounterpartyObjectId);
        Assert.Equal(source.AssigneeIds, card.AssigneeIds); Assert.Equal(source.WatcherIds, card.WatcherIds);
        Assert.Null(card.ParentTaskId); Assert.Null(card.RequesterUserId);
        Assert.Null(card.ScheduledDate); Assert.Null(card.StartTimeLocal); Assert.Null(card.ScheduleTimeZone);
        // No task identity/status/lifecycle/recurrence or workspace children exist in this create command.
        Assert.DoesNotContain(source.Id.ToString(), card.ToJson());
        Assert.DoesNotContain(source.RecurrenceSeriesId!.ToString()!, card.ToJson());
        Assert.Contains("Даты и повторение очищены", editor.SimilarNotice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableOrUnverifiedRelationsAreClearedAndExplained(bool optionsFailed)
    {
        var editor = TaskEditorViewModel.FromSimilar(Source(), optionsFailed ? null : new JsonObject(), true, true);
        var card = editor.BuildCreateCommand()!.Card!;
        Assert.Null(card.ProjectId); Assert.Null(card.PrimaryCounterpartyObjectId);
        Assert.Empty(card.AssigneeIds); Assert.Empty(card.WatcherIds);
        Assert.Contains("проект, контрагент, исполнители, наблюдатели", editor.SimilarNotice);
        // Subsequent searches must not restore removed values from the source.
        editor.Card.SetOptions(new JsonObject());
        Assert.Null(editor.Card.Build(null).ProjectId);
        Assert.Empty(editor.Card.Build(null).AssigneeIds);
    }

    [Fact]
    public void AssignmentAndWatcherCapabilitiesAreIndependent()
    {
        var source = Source();
        var editor = TaskEditorViewModel.FromSimilar(source, Options(source), false, false);
        Assert.Empty(editor.BuildCreateCommand()!.Card!.AssigneeIds);
        Assert.Empty(editor.BuildCreateCommand()!.Card!.WatcherIds);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async System.Threading.Tasks.Task ActionRequiresReadAndCreate(bool read, bool create)
    {
        var source = Source(); var client = new Client(source);
        using var vm = new TasksViewModel(client, new[] { read ? "Task.Read" : "", create ? "Task.Create" : "" });
        await vm.ActivateAsync(); vm.SelectedItem = vm.Items.Single();
        Assert.Equal(read && create, vm.CanCreateSimilar);
        await vm.CreateSimilarTaskCommand.ExecuteAsync();
        Assert.Equal(read && create, vm.Editor is not null);
        Assert.Equal(0, client.Inner.CreateCallCount);
    }

    [Theory]
    [InlineData("lost-access")]
    [InlineData("archived")]
    [InlineData("trashed")]
    public async System.Threading.Tasks.Task UnreadableSourceNeverSeedsDraft(string reason)
    {
        var source = Source(); var client = new Client(source);
        using var vm = new TasksViewModel(client, Capabilities);
        await vm.ActivateAsync(); vm.SelectedItem = vm.Items.Single();
        Assert.True(vm.CanCreateSimilar);
        // Current canonical detail endpoint excludes archive/trash, as well as lost object visibility.
        client.Inner.DetailResult = reason == "lost-access"
            ? new DesktopTasksApiResult<DesktopTaskDto>.Forbidden() : new DesktopTasksApiResult<DesktopTaskDto>.NotFound();
        await vm.CreateSimilarTaskCommand.ExecuteAsync();
        Assert.Null(vm.Editor); Assert.False(vm.CanCreateSimilar);
        Assert.Equal(0, client.Inner.CreateCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task SimilarSave_OfflineAndRevokedCreateNeverSubmit()
    {
        var source = Source(); var client = new Client(source);
        using var vm = new TasksViewModel(client, Capabilities);
        await vm.ActivateAsync(); vm.SelectedItem = vm.Items.Single();
        await vm.CreateSimilarTaskCommand.ExecuteAsync();
        vm.UpdateConnectivity(false);
        Assert.False(await vm.SaveEditorCommand.ExecuteAsync());
        vm.UpdateConnectivity(true); vm.UpdateCapabilities(["Task.Read"]);
        Assert.False(await vm.SaveEditorCommand.ExecuteAsync());
        Assert.Equal(source.Title, vm.Editor!.Title); Assert.Equal(0, client.Inner.CreateCallCount);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(503)]
    public async System.Threading.Tasks.Task SimilarSave_OrdinaryErrorHandlingPreservesDraft(int status)
    {
        var source = Source(DesktopTaskStatus.Completed); var client = new Client(source);
        client.Inner.CreateResult = status switch
        {
            403 => new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
            422 => new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Правила изменились", new Dictionary<string, IReadOnlyList<string>> { ["title"] = ["Исправьте название"] }),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable()
        };
        using var vm = new TasksViewModel(client, Capabilities);
        await vm.ActivateAsync(); vm.SelectedItem = vm.Items.Single();
        await vm.CreateSimilarTaskCommand.ExecuteAsync();
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Equal(source.Title, vm.Editor!.Title);
        Assert.Single(client.Inner.CreateCommands); Assert.Equal(0, client.WorkspaceWrites);
        if (status == 403) Assert.False(vm.SaveEditorCommand.CanExecute(null));
        if (status == 422) Assert.Equal("Исправьте название", vm.Editor.TitleError);
        if (status == 503)
        {
            await vm.SaveEditorCommand.ExecuteAsync();
            Assert.Equal(client.Inner.CreateCommands[0].IdempotencyKey, client.Inner.CreateCommands[1].IdempotencyKey);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SimilarSave_DoubleSaveSingleFlight()
    {
        var source = Source(); var client = new Client(source);
        var release = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        client.Inner.CreateHandler = (_, _) => release.Task;
        using var vm = new TasksViewModel(client, Capabilities);
        await vm.ActivateAsync(); vm.SelectedItem = vm.Items.Single();
        await vm.CreateSimilarTaskCommand.ExecuteAsync();
        var save = vm.SaveEditorCommand.ExecuteAsync();
        Assert.False(await vm.SaveEditorCommand.ExecuteAsync());
        release.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(source with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.New }, 1, false));
        await save;
        Assert.Single(client.Inner.CreateCommands); Assert.Null(vm.Editor);
    }

    private sealed class Client : IDesktopTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public TasksViewModelTests.FakeTasksApiClient Inner { get; } = new();
        private readonly JsonObject _options;
        public int WorkspaceWrites { get; private set; }
        public Client(DesktopTaskDto source)
        {
            Inner.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([source], null, null)));
            Inner.DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(source); _options = Options(source);
        }
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) => Inner.GetTasksAsync(cursor, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) => Inner.GetTaskByIdAsync(id, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => Inner.CreateTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => Inner.PatchTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => Inner.TransitionTaskAsync(command, cancellationToken);
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) => System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(_options, 0, null));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) => System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), 1, null));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token)
        { WorkspaceWrites++; throw new InvalidOperationException("Similar creation must not write children."); }
    }
}
