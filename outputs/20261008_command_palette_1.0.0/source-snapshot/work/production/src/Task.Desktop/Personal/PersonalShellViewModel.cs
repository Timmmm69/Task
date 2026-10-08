using System.ComponentModel;
using Task.Desktop.Modes;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Personal;

/// <summary>Presentation routing only; local models keep their storage and drafts.</summary>
public sealed partial class PersonalShellViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalApplicationModel _model;
    private NavigationSection _selectedSection;
    private bool _backgroundAvailable;
    private Guid? _requestedTaskId;
    public bool BackgroundAvailable { get => _backgroundAvailable; internal set => SetProperty(ref _backgroundAvailable, value); }
    public PersonalShellViewModel(PersonalApplicationModel model)
    {
        _model = model;
        _selectedSection = Sections[0];
        Palette = new(model.Workspace?.Hub, PaletteCommands, this);
        foreach (var child in Children) child.PropertyChanged += OnChildChanged;
        if (model.Workspace is { } workspace) workspace.OpenObjectRequested += OpenObject;
        ApplyRoute();
    }
    public PersonalTasksViewModel? Tasks => _model.Tasks;
    public PersonalPlanningViewModel? Planning => _model.Planning;
    public CalendarViewModel? Calendar => _model.Calendar;
    public PersonalWorkspaceViewModel? Workspace => _model.Workspace;
    public IReadOnlyList<NavigationSection> Sections { get; } =
    [
        new("today", "Сегодня", "", "Task.Icon.Today", "План на текущий день"),
        new("inbox", "Входящие", "", "Task.Icon.Inbox", "Быстрый сбор задач"),
        new("calendar", "Календарь", "", "Task.Icon.Calendar", "Личное расписание"),
        new("tasks", "Задачи", "", "Task.Icon.Tasks", "Все личные задачи"),
        new("projects", "Проекты", "", "Task.Icon.Projects", "Личные проекты"),
        new("catalog", "Каталог", "", "Task.Icon.Catalog", "Файлы и папки"),
        new("contacts", "Контакты", "", "Task.Icon.Contacts", "Личные контакты"),
        new("search", "Поиск", "", "Task.Icon.Search", "Поиск по личному пространству"),
        new("notifications", "Уведомления", "", "Task.Icon.Notifications", "Напоминания и события"),
        new("archive", "Архив", "", "Task.Icon.Archive", "Архивные записи"),
        new("trash", "Корзина", "", "Task.Icon.Trash", "Удалённые записи"),
        new("settings", "Настройки", "", "Task.Icon.Settings", "Параметры приложения"),
    ];
    public NavigationSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (!CanNavigate || !Sections.Contains(value) || ReferenceEquals(_selectedSection, value)) return;
            _selectedSection = value;
            ApplyRoute();
            foreach (var name in new[] { nameof(IsTasks), nameof(IsProjects), nameof(IsCalendar), nameof(IsNotifications), nameof(IsWorkspace), nameof(IsSettings) }) OnPropertyChanged(name);
            OnPropertyChanged(nameof(SelectedSection));
        }
    }
    public bool IsTasks => SelectedSection.Route is "today" or "inbox" or "tasks";
    public bool IsProjects => SelectedSection.Route == "projects";
    public bool IsCalendar => SelectedSection.Route == "calendar";
    public bool IsNotifications => SelectedSection.Route == "notifications";
    public bool IsSettings => SelectedSection.Route == "settings";
    public bool IsWorkspace => SelectedSection.Route is "catalog" or "contacts" or "search" or "archive" or "trash" or "settings";
    public bool CanNavigate => !_model.IsBusy && Tasks?.HasDrafts != true && Planning?.IsProjectEditing != true && Calendar?.Editor is null && Calendar?.Recurrence?.IsOpen != true;
    public string NavigationHint => CanNavigate ? "Данные сохраняются на этом компьютере" : "Завершите редактирование, чтобы перейти в другой раздел";
    private IEnumerable<ViewModelBase> Children => new ViewModelBase?[] { Tasks, Planning, Calendar, Calendar?.Recurrence, Workspace, Workspace?.Hub }.OfType<ViewModelBase>();
    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Tasks) && e.PropertyName == nameof(PersonalTasksViewModel.Items)) SelectRequestedTask();
        OnPropertyChanged(nameof(CanNavigate)); OnPropertyChanged(nameof(NavigationHint));
    }
    private void ApplyRoute()
    {
        if (IsTasks && Tasks is { } tasks) tasks.SelectedSection = _model.Sections.First(s => s.Title == SelectedSection.Title);
        if (IsWorkspace && Workspace is { } workspace)
        {
            var area = workspace.Areas.First(a => a.Title == SelectedSection.Title);
            if (workspace.Area == area) workspace.Hub.Activate(area.Area);
            else workspace.Area = area;
        }
    }
    private void OpenObject(string type, Guid id)
    {
        if (!CanNavigate) return;
        var route = type switch { "task" => "tasks", "project" => "projects", "contact" => "contacts", "catalog_item" => "catalog", _ => null };
        if (route is null) return;
        if (type == "task") _requestedTaskId = id;
        SelectedSection = Sections.First(s => s.Route == route);
        SelectRequestedTask();
        if (type == "project" && Planning is { } planning) planning.SelectedProject = planning.Projects.FirstOrDefault(p => p.Id == id);
    }
    private void SelectRequestedTask()
    {
        if (SelectedSection.Route != "tasks" || _requestedTaskId is not { } id || Tasks is not { } tasks) return;
        if (tasks.Items.FirstOrDefault(t => t.Source.Id == id) is not { } item) return;
        _requestedTaskId = null;
        tasks.Selected = item;
    }
    public void Dispose()
    {
        Palette.Dispose();
        foreach (var child in Children) child.PropertyChanged -= OnChildChanged;
        if (Workspace is { } workspace) workspace.OpenObjectRequested -= OpenObject;
    }
}
