using Task.Desktop.Projects;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public AsyncCommand AddProjectTaskCommand { get; private set; } = null!;
    public bool CanAddProjectTask => IsConnected && Tasks?.CanCreateFromShell == true
        && Tasks.Editor is null && !Tasks.IsBusy && !Tasks.IsMutationBusy
        && Tasks.Workspace?.IsBusy != true && string.IsNullOrEmpty(Tasks.Workspace?.CheckText)
        && string.IsNullOrEmpty(Tasks.Workspace?.Comment)
        && Projects is { IsActive: true, CanRead: true, IsDetailLoading: false, Editor: null,
            SelectedItem.Source.LifecycleState: "active" }
        && Projects.State == ProjectsScreenState.Loaded
        && Calendar?.Editor is null && Inbox?.HasConversion != true;
    public string ProjectTaskActionHint => CanAddProjectTask ? "Открыть форму с выбранным проектом."
        : Tasks?.CanCreateFromShell != true ? Tasks?.CreateActionHint ?? "Создание задач недоступно."
        : "Выберите доступный активный проект и завершите текущую загрузку или редактирование.";
    private async System.Threading.Tasks.Task OpenProjectTaskAsync(object? _, CancellationToken token)
    {
        if (!CanAddProjectTask || Projects?.SelectedItem is not { } project || Tasks is null) return;
        var id = project.Source.Id; var name = project.Name;
        SelectedSection = Sections.First(s => s.Route == "tasks");
        await Tasks.ActivateAsync(token);
        // Seed before the asynchronous options request; later responses use the current selection.
        await Tasks.NewTaskCommand.ExecuteAsync(new TaskChoice(id, name), token);
    }
    private void NotifyProjectTaskAction()
    {
        AddProjectTaskCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ProjectTaskActionHint));
    }
}
