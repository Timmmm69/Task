using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Personal;

// Local presentation only: Projects remains the unfiltered active-project snapshot
// used by the shell and reminder workflows.
public sealed partial class PersonalPlanningViewModel
{
    private string _projectSearch = "";
    private int _projectStatusFilter, _projectSort;
    private bool _filteringProjects, _projectLoadFailed;
    private IReadOnlyList<PersonalProject> _visibleProjects = [];
    private IReadOnlyList<PersonalTaskItem> _relatedProjectTasks = [];
    private Dictionary<Guid, PersonalTaskItem[]> _tasksByProject = [];

    public string ProjectSearch { get => _projectSearch; set { if (SetProperty(ref _projectSearch, value)) FilterProjects(); } }
    public int ProjectStatusFilter { get => _projectStatusFilter; set { if (SetProperty(ref _projectStatusFilter, value)) FilterProjects(); } }
    public int ProjectSort { get => _projectSort; set { if (SetProperty(ref _projectSort, value)) FilterProjects(); } }
    public IReadOnlyList<string> ProjectStatusFilters { get; } = ["Все статусы", "Планирование", "Активен", "Приостановлен", "Завершён"];
    public IReadOnlyList<string> ProjectSortOrders { get; } = ["Название: А → Я", "Название: Я → А", "Дата начала: ближайшие", "Плановое окончание", "Завершение: новые сначала"];
    public IReadOnlyList<PersonalProject> VisibleProjects { get => _visibleProjects; private set => SetProperty(ref _visibleProjects, value); }
    public IReadOnlyList<PersonalTaskItem> RelatedProjectTasks { get => _relatedProjectTasks; private set => SetProperty(ref _relatedProjectTasks, value); }
    public AsyncCommand OpenProjectTaskCommand { get; }
    public bool CanSelectProject => !IsBusy && !IsProjectEditing;
    public bool HasProjectSelection => SelectedProject is not null;
    public bool HasProjectFilters => !string.IsNullOrWhiteSpace(ProjectSearch) || ProjectStatusFilter != 0 || ProjectSort != 0;
    public bool ShowProjectEmpty => VisibleProjects.Count == 0 && !IsBusy && !_projectLoadFailed;
    public bool ProjectLoadFailed => _projectLoadFailed;
    public string ProjectResultCount => $"Найдено {VisibleProjects.Count} из {Projects.Count}";
    public string ProjectEmptyTitle => Projects.Count == 0 ? "Проектов пока нет" : "Ничего не найдено";
    public string ProjectEmptyBody => Projects.Count == 0 ? "Создайте проект, чтобы собрать задачи вместе." : "Попробуйте другое название или сбросьте фильтры.";
    public bool NoRelatedProjectTasks => HasProjectSelection && RelatedProjectTasks.Count == 0;
    public string ProjectTaskSummary => $"Задач: {RelatedProjectTasks.Count} · выполнено: {RelatedProjectTasks.Count(t => t.Source.Status == DesktopTaskStatus.Completed)} · отменено: {RelatedProjectTasks.Count(t => t.Source.Status == DesktopTaskStatus.Cancelled)}";
    public string ProjectDescriptionText => string.IsNullOrWhiteSpace(SelectedProject?.Description) ? "Описание не добавлено." : SelectedProject.Description;

    public void ResetProjectFilters()
    {
        _projectSearch = ""; _projectStatusFilter = 0; _projectSort = 0;
        OnPropertyChanged(nameof(ProjectSearch)); OnPropertyChanged(nameof(ProjectStatusFilter)); OnPropertyChanged(nameof(ProjectSort));
        FilterProjects();
    }

    private IReadOnlyList<DesktopTaskDto> RefreshProjectBrowser()
    {
        try
        {
            var projects = _store.Projects();
            var tasks = _store.List();
            _tasksByProject = tasks.Where(t => t.Card?.ProjectId is not null).GroupBy(t => t.Card!.ProjectId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).Select(t => new PersonalTaskItem(t)).ToArray());
            Projects = projects;
            _projectLoadFailed = false;
            FilterProjects();
            UpdateProjectDetails();
            return tasks;
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        {
            _projectLoadFailed = true;
            throw;
        }
        finally { OnPropertyChanged(nameof(ProjectLoadFailed)); OnPropertyChanged(nameof(ShowProjectEmpty)); }
    }

    private void FilterProjects()
    {
        if (_filteringProjects) return;
        _filteringProjects = true;
        try
        {
            var selectedId = SelectedProject?.Id;
            var query = Projects.Where(p => p.Name.Contains(ProjectSearch.Trim(), StringComparison.CurrentCultureIgnoreCase)
                && (ProjectStatusFilter == 0 || (int)p.Status == ProjectStatusFilter - 1));
            var ordered = ProjectSort switch
            {
                1 => query.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                2 => query.OrderBy(p => p.StartDate is null).ThenBy(p => p.StartDate).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                3 => query.OrderBy(p => p.PlannedEndDate is null).ThenBy(p => p.PlannedEndDate).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                4 => query.OrderBy(p => p.ActualEndAt is null).ThenByDescending(p => p.ActualEndAt).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => query.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            };
            VisibleProjects = ordered.ThenBy(p => p.Id).ToArray();
            if (!IsProjectEditing) SelectedProject = VisibleProjects.FirstOrDefault(p => p.Id == selectedId) ?? VisibleProjects.FirstOrDefault();
            foreach (var property in new[] { nameof(ProjectResultCount), nameof(ShowProjectEmpty), nameof(ProjectEmptyTitle), nameof(ProjectEmptyBody), nameof(HasProjectFilters) }) OnPropertyChanged(property);
        }
        finally { _filteringProjects = false; }
    }

    private void UpdateProjectDetails()
    {
        RelatedProjectTasks = SelectedProject is { } project && _tasksByProject.TryGetValue(project.Id, out var tasks) ? tasks : [];
        foreach (var property in new[] { nameof(HasProjectSelection), nameof(NoRelatedProjectTasks), nameof(ProjectTaskSummary), nameof(ProjectDescriptionText) }) OnPropertyChanged(property);
    }
}
