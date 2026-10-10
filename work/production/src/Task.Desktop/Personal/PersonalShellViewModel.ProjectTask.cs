namespace Task.Desktop.Personal;

public sealed partial class PersonalShellViewModel
{
    public ViewModels.AsyncCommand AddProjectTaskCommand { get; private set; } = null!;
    public bool CanAddProjectTask => IsProjects && CanNavigate && Tasks?.CanNavigate == true
        && Planning is { IsBusy: false, IsProjectEditing: false, SelectedProject.Lifecycle: "active" };
    public string ProjectTaskActionHint => CanAddProjectTask ? "Открыть форму с выбранным личным проектом."
        : "Выберите активный личный проект и завершите текущее действие или редактирование.";
    private async System.Threading.Tasks.Task OpenProjectTaskAsync()
    {
        if (!CanAddProjectTask || Planning?.SelectedProject is not { } project || Tasks is null) return;
        SelectedSection = Sections.First(s => s.Route == "tasks");
        await Tasks.CreateFromProjectAsync(project);
    }
}
