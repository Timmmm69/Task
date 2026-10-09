using System.IO;
using Task.Desktop.Modes;
using Task.Desktop.Personal;

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
    public async global::System.Threading.Tasks.Task CaptureAndInboxEdit_ConfirmCorrectDestination_AndOpenConvertedTask()
    {
        using var model = new PersonalApplicationModel(_root);
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
        var vm = model.Tasks!; vm.CaptureText = "Запись"; await vm.CaptureCommand.ExecuteAsync();
        Assert.Contains("«Входящие»", vm.Message);
        vm.Selected = Assert.Single(vm.Items); var id = vm.Selected.Source.Id;
        await vm.EditCommand.ExecuteAsync(); vm.Editor!.Card.Description = "Уточнили задачу";
        await vm.SaveCommand.ExecuteAsync(); Assert.Empty(vm.Items);
        Assert.True(vm.ShowSavedTaskAction);
        await vm.OpenSavedTaskCommand.ExecuteAsync();
        Assert.Equal("tasks", shell.SelectedSection.Route); Assert.Equal(id, vm.Selected?.Source.Id);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
