using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class TaskContextActionsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async System.Threading.Tasks.Task RowUsesClickedItemAndProtectsDraft(bool readOnly)
    {
        var first = ActionTasksClient.Source("Первая"); var second = ActionTasksClient.Source("Вторая");
        var client = new ActionTasksClient(first, second);
        using var vm = new TasksViewModel(client, readOnly ? ["Task.Read"] : ["Task.Read", "Task.Update", "Task.Create"]);
        await vm.ActivateAsync(); await Until(() => vm.Workspace?.IsBusy != true);
        vm.SelectedItem = vm.Items.First(x => x.Id == first.Id);
        var details = 0; vm.RowDetailsRequested += () => details++;
        await vm.OpenRowCommand.ExecuteAsync(vm.Items.First(x => x.Id == second.Id));
        Assert.Equal(second.Id, vm.SelectedItem!.Id);
        if (readOnly) { Assert.Null(vm.Editor); Assert.Equal(1, details); }
        else
        {
            Assert.Equal(second.Id, vm.Editor!.SourceId);
            var draft = vm.Editor; draft.Title = "Не потерять";
            Assert.False(await vm.OpenRowCommand.ExecuteAsync(vm.Items.First(x => x.Id == first.Id)));
            Assert.False(await vm.NewTaskCommand.ExecuteAsync());
            Assert.Same(draft, vm.Editor); Assert.Equal("Не потерять", draft.Title);
        }
        Assert.Empty(client.Creates);
    }

    [Theory]
    [InlineData(DesktopTaskStatus.Completed, false)] [InlineData(DesktopTaskStatus.Cancelled, false)]
    [InlineData(DesktopTaskStatus.New, true)]
    public async System.Threading.Tasks.Task TerminalAndNestedParentsCannotOpenCreation(DesktopTaskStatus status, bool nested)
    {
        var source = ActionTasksClient.Source("Родитель") with { Status = status, Card = new() { ParentTaskId = nested ? Guid.NewGuid() : null } };
        var client = new ActionTasksClient(source);
        using var vm = new TasksViewModel(client, ["Task.Read", "Task.Create", "Task.Update"]);
        await vm.ActivateAsync(); await Until(() => vm.Workspace?.IsBusy != true);
        Assert.False(vm.AddSubtaskCommand.CanExecute(null));
        if (!nested) { await vm.OpenRowCommand.ExecuteAsync(vm.Items.Single()); Assert.Null(vm.Editor); }
        Assert.Empty(client.Creates);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async System.Threading.Tasks.Task ContextSeedLateOptionsUserClearCancelAndFailure(bool parent, bool forbidden)
    {
        var source = ActionTasksClient.Source("Родитель"); var client = new ActionTasksClient(source);
        using var vm = new TasksViewModel(client, ["Task.Read", "Task.Create"]);
        await vm.ActivateAsync(); await Until(() => vm.Workspace?.IsBusy != true);
        client.DelayOptions = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var seeded = parent ? source.Id : Guid.NewGuid();
        var opening = parent ? vm.AddSubtaskCommand.ExecuteAsync() : vm.NewTaskCommand.ExecuteAsync(new TaskChoice(seeded, "Понятное название"));
        await Until(() => vm.Editor is not null);
        var draft = vm.Editor!;
        Assert.Equal(seeded, parent ? draft.Card.Parent!.Id : draft.Card.Project!.Id);
        Assert.DoesNotContain(seeded.ToString(), parent ? draft.Card.Parent!.Name : draft.Card.Project!.Name);
        draft.Title = "Дочерняя";
        var replacement = new TaskChoice(Guid.NewGuid(), "Пользователь выбрал другой объект");
        if (parent) draft.Card.Parent = replacement; else draft.Card.Project = replacement;
        client.DelayOptions.SetResult(new(new JsonObject(), 0, null)); await opening;
        Assert.Equal(replacement.Id, parent ? draft.Card.Parent!.Id : draft.Card.Project!.Id);
        Assert.Equal(replacement.Name, parent ? draft.Card.Parent!.Name : draft.Card.Project!.Name);
        var command = draft.BuildCreateCommand()!;
        Assert.Null(command.StartAtUtc); Assert.Null(command.DeadlineAtUtc); Assert.Equal(DesktopTaskPriority.Normal, command.Priority);
        Assert.Null(command.Card!.Description); Assert.Empty(command.Card.AssigneeIds); Assert.Empty(command.Card.WatcherIds);
        if (parent) { Assert.Equal(replacement.Id, command.Card.ParentTaskId); Assert.Null(command.Card.ProjectId); }
        else { Assert.Equal(replacement.Id, command.Card.ProjectId); Assert.Null(command.Card.ParentTaskId); }
        client.Failure = forbidden ? new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden() : new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Объект недоступен", new Dictionary<string, IReadOnlyList<string>>());
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Same(draft, vm.Editor); Assert.Equal("Дочерняя", draft.Title); Assert.False(vm.ShowSavedTaskAction);
        Assert.Single(client.Creates);
        if (parent) draft.Card.Parent = null; else draft.Card.Project = null;
        draft.Card.SetOptions(new());
        Assert.Null(parent ? draft.Card.Build(null).ParentTaskId : draft.Card.Build(null).ProjectId);
        await vm.DiscardEditorCommand.ExecuteAsync(); Assert.Null(vm.Editor); Assert.Single(client.Creates);
    }

    [Fact]
    public async System.Threading.Tasks.Task PermissionBusyWorkspaceDraftAndFreshTerminalAreRespected()
    {
        var source = ActionTasksClient.Source("Родитель"); var client = new ActionTasksClient(source);
        using var vm = new TasksViewModel(client, ["Task.Read", "Task.Create", "Task.Update"]);
        await vm.ActivateAsync(); await Until(() => vm.Workspace?.IsBusy != true);
        vm.Workspace!.Comment = "Текст комментария";
        Assert.False(vm.AddSubtaskCommand.CanExecute(null)); Assert.False(vm.OpenRowCommand.CanExecute(vm.Items.Single()));
        vm.Workspace.Comment = ""; vm.Workspace.CheckText = "Пункт";
        Assert.False(vm.AddSubtaskCommand.CanExecute(null)); vm.Workspace.CheckText = "";
        vm.UpdateCapabilities(["Task.Read"]); Assert.False(vm.AddSubtaskCommand.CanExecute(null));
        vm.UpdateCapabilities(["Task.Read", "Task.Create", "Task.Update"]);
        client.Tasks[source.Id] = source with { Status = DesktopTaskStatus.Completed };
        vm.SelectedItem = null;
        await vm.OpenRowCommand.ExecuteAsync(vm.Items.Single()); Assert.Null(vm.Editor);
        Assert.Empty(client.Creates);
    }

    [Fact]
    public async System.Threading.Tasks.Task BusyRefreshBlocksAllContextActionsWithoutWrites()
    {
        var source = ActionTasksClient.Source("Задача"); var client = new ActionTasksClient(source);
        using var vm = new TasksViewModel(client, ["Task.Read", "Task.Create", "Task.Update"]);
        await vm.ActivateAsync(); await Until(() => vm.Workspace?.IsBusy != true);
        client.DelayPage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = vm.RefreshCommand.ExecuteAsync(); await Until(() => vm.IsBusy);
        Assert.False(vm.OpenRowCommand.CanExecute(vm.Items.Single()));
        Assert.False(vm.AddSubtaskCommand.CanExecute(null)); Assert.False(vm.NewTaskCommand.CanExecute(null));
        client.DelayPage.SetResult(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([source], null, 1))); await refresh;
        Assert.Empty(client.Creates); Assert.Null(vm.Editor);
    }

    [Fact]
    public async System.Threading.Tasks.Task PersonalUsesLocalProjectsAndPersistsParentWithoutInheritingProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "Task-context-actions", Guid.NewGuid().ToString("N"));
        try
        {
            using var model = new PersonalApplicationModel(root); using var shell = new PersonalShellViewModel(model);
            var project = model.Store.SaveProject(new(Guid.Empty, 0, "Личный проект", null, DesktopProjectStatus.Active, null, null));
            model.Planning!.Refresh(); model.Planning.SelectedProject = model.Planning.Projects.Single();
            shell.SelectedSection = shell.Sections.Single(s => s.Route == "projects");
            Assert.True(shell.AddProjectTaskCommand.CanExecute(null)); await shell.AddProjectTaskCommand.ExecuteAsync();
            var vm = model.Tasks!; Assert.Equal(project.Id, vm.Editor!.Card.Project!.Id); Assert.Equal(project.Name, vm.Editor.Card.Project.Name);
            vm.Editor.Title = "Родитель"; await vm.SaveCommand.ExecuteAsync(); await vm.OpenSavedTaskCommand.ExecuteAsync();
            var parent = vm.Selected!.Source;
            var localClient = new PersonalTasksClient(model.Store);
            var read = Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.Succeeded>(await localClient.GetTaskByIdAsync(parent.Id));
            Assert.Equal(project.Id, read.Value.Card!.ProjectId);
            await vm.AddSubtaskCommand.ExecuteAsync(); Assert.Equal(parent.Id, vm.Editor!.Card.Parent!.Id); Assert.Equal("Родитель", vm.Editor.Card.Parent.Name);
            Assert.Null(vm.Editor.Card.Project!.Id); vm.Editor.Title = "Подзадача"; await vm.SaveCommand.ExecuteAsync(); await vm.OpenSavedTaskCommand.ExecuteAsync();
            var child = Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.Succeeded>(await localClient.GetTaskByIdAsync(vm.Selected!.Source.Id));
            Assert.Equal(parent.Id, child.Value.Card!.ParentTaskId); Assert.Null(child.Value.Card.ProjectId);
            Assert.False(vm.AddSubtaskCommand.CanExecute(null));
            vm.Selected = vm.Items.Single(t => t.Source.Id == parent.Id);
            await vm.AddSubtaskCommand.ExecuteAsync(); var draft = vm.Editor!; draft.Title = "Не терять";
            model.Store.Transition(new(parent.Id, parent.Version, DesktopTaskStatus.Cancelled));
            await vm.SaveCommand.ExecuteAsync(); Assert.Same(draft, vm.Editor); Assert.Equal("Не терять", draft.Title);
            await vm.CancelCommand.ExecuteAsync(); Assert.Equal(2, model.Store.List().Count);
            var foreign = new PersonalProject(Guid.NewGuid(), 1, "Чужой проект", null, DesktopProjectStatus.Active, null, null);
            await vm.CreateFromProjectAsync(foreign); Assert.Null(vm.Editor);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static async System.Threading.Tasks.Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await System.Threading.Tasks.Task.Delay(10, timeout.Token);
    }
}

internal sealed class ActionTasksClient(params DesktopTaskDto[] initial) : IDesktopTasksApiClient, IDesktopTaskWorkspaceClient
{
    public Dictionary<Guid, DesktopTaskDto> Tasks { get; } = initial.ToDictionary(t => t.Id);
    public List<DesktopCreateTaskCommand> Creates { get; } = [];
    public TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>? DelayPage { get; set; }
    public TaskCompletionSource<TaskWorkspaceResult>? DelayOptions { get; set; }
    public DesktopTaskWriteResult<DesktopTaskDto>? Failure { get; set; }
    public static DesktopTaskDto Source(string title) => new(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, title, Guid.NewGuid(), DesktopTaskStatus.New, DesktopTaskPriority.Normal, null, null, [], [], null, new());
    public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default) => DelayPage?.Task ?? System.Threading.Tasks.Task.FromResult<DesktopTasksApiResult<DesktopTaskPage>>(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(Tasks.Values.ToArray(), null, Tasks.Count)));
    public System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<DesktopTasksApiResult<DesktopTaskDto>>(Tasks.TryGetValue(id, out var task) ? new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task) : new DesktopTasksApiResult<DesktopTaskDto>.NotFound());
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        Creates.Add(command); if (Failure is not null) return System.Threading.Tasks.Task.FromResult(Failure);
        var task = Source(command.Title) with { Card = command.Card }; Tasks.Add(task.Id, task);
        return System.Threading.Tasks.Task.FromResult<DesktopTaskWriteResult<DesktopTaskDto>>(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task, 1, false));
    }
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) => DelayOptions?.Task ?? System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), 0, null));
    public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) => System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(new JsonObject(), 1, null));
    public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token) => throw new InvalidOperationException();
}
