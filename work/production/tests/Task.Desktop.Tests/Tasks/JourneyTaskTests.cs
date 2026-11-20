using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class TasksViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task ReadOnlyShell_EmptyTodayActionRoutesToTasks()
    {
        using var vm = new TasksViewModel(new FakeTasksApiClient(), ["Task.Read"]);
        using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, vm);
        Assert.Equal("today", shell.SelectedSection!.Route);
        Assert.True(shell.OpenTasksCommand.CanExecute(null));
        await shell.OpenTasksCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection!.Route); Assert.True(vm.IsActive);
        Assert.False(vm.CanCreate);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ShellCreate_WaitsForTheFirstTaskLoadBeforeOpeningEditor()
    {
        var page = new TaskCompletionSource<DesktopTasksApiResult<DesktopTaskPage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeTasksApiClient();
        client.EnqueuePage((_, _) => page.Task);
        using var vm = new TasksViewModel(client, WriteCapabilities);
        using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, vm);

        var opening = shell.NewTaskCommand.ExecuteAsync();
        Assert.False(opening.IsCompleted);
        Assert.Equal("tasks", shell.SelectedSection!.Route);
        Assert.Null(vm.Editor);

        page.SetResult(SucceededPage([]));
        Assert.True(await opening);
        Assert.Equal(TaskEditorMode.Create, vm.Editor?.Mode);
        Assert.Equal(1, client.PageCallCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CreateWithoutDetails_ConfirmsTaskDestinationWithoutInferringInboxMembership()
    {
        var created = CreateTask("Записать мысль") with { DeadlineAtUtc = null };
        var client = new FakeTasksApiClient { CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, false) };
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync(); await vm.NewTaskCommand.ExecuteAsync();
        vm.Editor!.Title = created.Title; await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Contains(created.Title, vm.Announcement);
        Assert.DoesNotContain("«Входящих»", vm.Announcement); Assert.Contains("«Задачи»", vm.Announcement);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SavedTaskHiddenByFilter_CanBeOpenedWithReadPermissionAfterWriteRevocation()
    {
        var existing = CreateTask("Другая", DesktopTaskStatus.InProgress);
        var created = CreateTask("Новая");
        var client = new FakeTasksApiClient
        {
            CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, false),
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(created),
        };
        client.EnqueuePage(SucceededPage([existing]));
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = DesktopTaskStatus.InProgress;
        await vm.NewTaskCommand.ExecuteAsync();
        Assert.Equal("Создать задачу", vm.Editor!.SubmitText);
        vm.Editor.Title = created.Title;
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Null(vm.Editor);
        Assert.Contains("разделе «Задачи»", vm.Announcement);
        Assert.DoesNotContain(vm.Items, x => x.Id == created.Id);
        Assert.True(vm.SavedTaskHiddenByFilters);
        Assert.True(vm.ShowSavedTaskAction);
        vm.UpdateCapabilities(["Task.Read"]);
        Assert.True(vm.OpenSavedTaskCommand.CanExecute(null));
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        Assert.Equal(created.Id, vm.SelectedItem?.Id);
        Assert.Null(vm.SelectedStatusFilter);
        Assert.False(vm.ShowSavedTaskAction);
        vm.UpdateSessionState(false);
        Assert.False(vm.OpenSavedTaskCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CreateAnother_UsesLaunchProjectInsteadOfEditedValues()
    {
        var created = CreateTask("Готовая задача");
        var client = new FakeTasksApiClient { CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, false) };
        client.EnqueuePage(SucceededPage([]));
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync();

        var project = new TaskChoice(Guid.NewGuid(), "Исходный проект");
        await vm.NewTaskCommand.ExecuteAsync(project);
        Assert.Equal(project, vm.Editor!.Card.Project);
        vm.Editor.Title = created.Title;
        vm.Editor.Card.Date = "12.07.2030";
        vm.Editor.DeadlineText = "15.07.2030";
        vm.Editor.Card.SeedProject(Guid.NewGuid(), "Другой проект");
        await vm.SaveEditorCommand.ExecuteAsync();

        Assert.True(vm.CreateAnotherTaskCommand.CanExecute(null));
        await vm.CreateAnotherTaskCommand.ExecuteAsync();
        Assert.Equal(project, vm.Editor!.Card.Project);
        Assert.True(string.IsNullOrEmpty(vm.Editor.Title));
        Assert.True(string.IsNullOrEmpty(vm.Editor.Card.Date));
        Assert.True(string.IsNullOrEmpty(vm.Editor.DeadlineText));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task FailedSave_KeepsDraftAndDoesNotOfferSuccessfulResult()
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([]));
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync(); await vm.NewTaskCommand.ExecuteAsync();
        var draft = vm.Editor!; draft.Title = "Сохранить позже";
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Same(draft, vm.Editor); Assert.Equal("Сохранить позже", draft.Title);
        Assert.False(vm.ShowSavedTaskAction); Assert.False(vm.OpenSavedTaskCommand.CanExecute(null));
        Assert.Contains("Сервер недоступен", draft.StatusMessage);
    }

    [Fact]
    public void DayField_AndTimedSchedule_PreserveExistingContractAndValidation()
    {
        var draft = new TaskEditorViewModel(TaskEditorMode.Create) { Title = "План" };
        draft.Card.Date = "12.10.2026";
        Assert.True(draft.Card.IsDateOnly);
        Assert.Equal(new DateOnly(2026, 10, 12), draft.BuildCreateCommand()!.Card!.ScheduledDate);
        var changes = new List<string?>();
        draft.Card.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        draft.StartText = "13.10.2026 10:30";
        Assert.False(draft.Card.IsDateOnly); Assert.Contains("очистите", draft.Card.DateHint);
        Assert.Contains(nameof(TaskCardEditor.DateHint), changes);
        var timed = draft.BuildCreateCommand()!;
        Assert.Equal(new DateOnly(2026, 10, 13), timed.Card!.ScheduledDate);
        Assert.Equal(new TimeOnly(10, 30), timed.Card.StartTimeLocal);
        draft.StartText = "";
        Assert.True(draft.Card.IsDateOnly);
        Assert.Equal(new DateOnly(2026, 10, 12), draft.BuildCreateCommand()!.Card!.ScheduledDate);
        draft.Card.Date = "31.02.2026";
        Assert.False(draft.CanSubmit); Assert.NotNull(draft.CardError);
    }

    [Fact]
    public void SubmitText_PreservesRetryState_AndDistinguishesCreateFromEdit()
    {
        var create = new TaskEditorViewModel(TaskEditorMode.Create);
        var edit = new TaskEditorViewModel(TaskEditorMode.Edit, CreateTask());
        Assert.Equal("Создать задачу", create.SubmitText);
        Assert.Equal("Сохранить изменения", edit.SubmitText);
        create.SetRetryAvailable(false); Assert.Equal("Повтор станет доступен…", create.SubmitText);
        create.SetRetryAvailable(true); Assert.Equal("Создать задачу", create.SubmitText);
    }
}
