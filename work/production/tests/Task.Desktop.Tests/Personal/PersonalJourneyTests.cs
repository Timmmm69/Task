using System.IO;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Projects;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalJourneyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-journey", Guid.NewGuid().ToString("N"));

    [Fact]
    public async global::System.Threading.Tasks.Task TodayEmptyAction_OpensTasks_AndPreservesCaptureDraft()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        var vm = model.Tasks!;
        await vm.RefreshAsync(); Assert.True(vm.IsToday); Assert.True(vm.ShowEmpty);
        await vm.OpenTasksCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection.Route);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
        vm.CaptureText = "Не потерять";
        Assert.False(vm.OpenTasksCommand.CanExecute(null));
        await vm.OpenTasksCommand.ExecuteAsync();
        Assert.Equal("inbox", shell.SelectedSection.Route); Assert.Equal("Не потерять", vm.CaptureText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SaveOutsideTodayFilter_OffersPersistedTask_AndOpenNavigatesAndSelectsIt()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        var vm = model.Tasks!;
        await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "Будущий договор";
        vm.Editor.Card.Date = "20.12.2030";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.True(vm.ShowSavedTaskAction);
        Assert.Contains("разделе «Задачи»", vm.Message);
        Assert.True(vm.OpenSavedTaskCommand.CanExecute(null));
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection.Route);
        Assert.Equal("Будущий договор", vm.Selected?.Title);
        Assert.Equal(new DateOnly(2030, 12, 20), model.Store.Get(vm.Selected!.Source.Id)!.Card!.ScheduledDate);
        await vm.EditCommand.ExecuteAsync(); Assert.Equal("Сохранить изменения", vm.Editor!.SubmitText);
        Assert.False(vm.OpenSavedTaskCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ProjectCreate_OpenHiddenTask_AndMovePlanWithoutMovingDeadline()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        var project = model.Store.SaveProject(new(Guid.Empty, 0, "Проект", null, DesktopProjectStatus.Active, null, null));
        var vm = model.Tasks!;
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        vm.SearchText = "Другая задача";

        await vm.CreateFromProjectAsync(project);
        Assert.Equal(project.Id, vm.Editor!.Card.Project?.Id);
        vm.Editor.Title = "Договор";
        vm.Editor.Card.Date = "12.07.2030";
        vm.Editor.DeadlineText = "15.07.2030";
        await vm.SaveCommand.ExecuteAsync();

        Assert.Contains("Договор", vm.Message);
        Assert.True(vm.SavedTaskHiddenByFilters);
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        var taskId = vm.Selected!.Source.Id;
        Assert.True(vm.DetailsExpanded);
        Assert.Equal("12.07.2030", vm.Selected.ScheduleText);
        Assert.Equal("15.07.2030", vm.Selected.DeadlineText);

        await vm.CreateAnotherTaskCommand.ExecuteAsync();
        Assert.Equal(project.Id, vm.Editor!.Card.Project?.Id);
        Assert.True(string.IsNullOrEmpty(vm.Editor.Title));
        Assert.True(string.IsNullOrEmpty(vm.Editor.Card.Date));
        await vm.CancelCommand.ExecuteAsync();

        vm.Selected = vm.Items.Single(item => item.Source.Id == taskId);
        await vm.EditCommand.ExecuteAsync();
        vm.Editor!.Card.Date = "13.07.2030";
        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("13.07.2030", vm.Selected!.ScheduleText);
        Assert.Equal("15.07.2030", vm.Selected.DeadlineText);
        Assert.Equal(new DateOnly(2030, 7, 15), model.Store.Get(taskId)!.Card!.DeadlineDate);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CaptureAndInboxEdit_ConfirmCorrectDestination_AndOpenConvertedTask()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
        var vm = model.Tasks!; vm.CaptureText = "Запись"; await vm.CaptureCommand.ExecuteAsync();
        Assert.Contains("«Пометки»", vm.Message);
        vm.Selected = Assert.Single(vm.Items); var id = vm.Selected.Source.Id;
        await vm.EditCommand.ExecuteAsync(); vm.Editor!.Card.Description = "Уточнили задачу";
        await vm.SaveCommand.ExecuteAsync(); Assert.Empty(vm.Items);
        Assert.True(vm.ShowSavedTaskAction);
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection.Route); Assert.Equal(id, vm.Selected?.Source.Id);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
