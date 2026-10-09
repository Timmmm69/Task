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
    public async global::System.Threading.Tasks.Task CreateWithoutDetails_ConfirmsInboxAndTasksDestination()
    {
        var created = CreateTask("Записать мысль") with { DeadlineAtUtc = null };
        var client = new FakeTasksApiClient { CreateResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(created, 1, false) };
        using var vm = new TasksViewModel(client, WriteCapabilities);
        await vm.ActivateAsync(); await vm.NewTaskCommand.ExecuteAsync();
        vm.Editor!.Title = created.Title; await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Contains("«Входящих»", vm.Announcement); Assert.Contains("«Задачи»", vm.Announcement);
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
        vm.SelectedStatusFilter = "В работе";
        await vm.NewTaskCommand.ExecuteAsync();
        Assert.Equal("Создать задачу", vm.Editor!.SubmitText);
        vm.Editor.Title = created.Title;
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Null(vm.Editor);
        Assert.Contains("разделе «Задачи»", vm.Announcement);
        Assert.DoesNotContain(vm.Items, x => x.Id == created.Id);
        Assert.True(vm.ShowSavedTaskAction);
        vm.UpdateCapabilities(["Task.Read"]);
        Assert.True(vm.OpenSavedTaskCommand.CanExecute(null));
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        Assert.Equal(created.Id, vm.SelectedItem?.Id);
        Assert.Equal("Все статусы", vm.SelectedStatusFilter);
        Assert.False(vm.ShowSavedTaskAction);
        vm.UpdateSessionState(false);
        Assert.False(vm.OpenSavedTaskCommand.CanExecute(null));
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
