using System.ComponentModel;
using System.Text.Json.Nodes;
using Task.Desktop.Modes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Personal;

public sealed record PersonalTaskItem(DesktopTaskDto Source)
{
    public string CompactDurationText => Source.Card?.PlannedDurationMinutes is { } minutes ? $" · {minutes} мин" : "";
    // WPF automation must never expose the compatibility DTO's technical actor.
    public override string ToString() => Title;
    public string Title => Source.Title;
    public string StatusText => TaskItemViewModel.LocalizeStatus(Source.Status);
    public string PriorityText => TaskItemViewModel.LocalizePriority(Source.Priority);
    public string ScheduleText => Source.StartAtUtc is { } start ? start.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        : Source.Card?.ScheduledDate?.ToString("dd.MM.yyyy") ?? "Без даты";
    public string CapturedText => Source.CreatedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy · HH:mm") ?? "Время не указано";
    public string DeadlineText => Source.Card?.DeadlineDate?.ToString("dd.MM.yyyy") ?? TaskItemViewModel.FormatDate(Source.DeadlineAtUtc, "Без срока");
}

/// <summary>Personal presentation uses the shared draft, statuses and Inbox semantics.</summary>
public sealed class PersonalTasksViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalTasksClient _client;
    private readonly Func<IReadOnlyList<PersonalProject>> _projects;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<DesktopTaskDto> _all = [];
    private readonly PersonalCalendarClient? _calendar;
    private bool _todayCalendarIsEmpty;
    private bool _loadFailed;
    private bool _hasLoaded;
    private IReadOnlyList<PersonalTaskItem> _items = [];
    private PersonalTaskItem? _selected;
    private TaskEditorViewModel? _editor;
    private Guid? _savedTaskId;
    private DesktopTaskDto? _createdTask;
    private Action<TaskEditorViewModel>? _createContext, _savedCreateContext;
    public event Action<Guid?>? OpenTasksRequested;
    public event Action? SavedTaskOpened;
    private string _capture = "", _check = "", _message = "";
    private bool _busy, _disposed, _hideCompleted, _filtering;
    private PersonalSection _section;
    public PersonalTasksViewModel(PersonalTasksClient client, IReadOnlyList<PersonalSection> sections, Func<IReadOnlyList<PersonalProject>>? projects = null, PersonalCalendarClient? calendar = null)
    {
        _projects = projects ?? (() => []);
        _calendar = calendar;
        _client = client; _section = sections.First(s => s.Title == "Задачи");
        RefreshCommand = new(async (_, _) => await RefreshAsync(), _ => !IsBusy && string.IsNullOrEmpty(CheckText));
        NewCommand = new(async (parameter, _) => await OpenAsync(null, parameter is Calendar.FreeTimeSlot slot
            ? draft => draft.SeedCalendarSlot(slot) : null), _ => CanSelectTask);
        CreateAnotherTaskCommand = new(async (_, _) => await OpenAsync(null, _savedCreateContext), _ => ShowCreateSuccess && CanNavigate);
        OpenTasksCommand = new((_, _) => { OpenTasksRequested?.Invoke(null); return System.Threading.Tasks.Task.CompletedTask; }, _ => CanNavigate);
        OpenSavedTaskCommand = new((_, _) => { OpenTasksRequested?.Invoke(_savedTaskId); return System.Threading.Tasks.Task.CompletedTask; }, _ => CanNavigate && _savedTaskId.HasValue);
        EditCommand = new(async (_, _) => await OpenAsync(Selected?.Source), _ => CanEdit && CanSelectTask);
        OpenRowCommand = new(async (p, _) =>
        {
            if (p is not PersonalTaskItem row) return;
            Selected = row;
            if (EditCommand.CanExecute(null)) await EditCommand.ExecuteAsync();
            else DetailsExpanded = true;
        }, p => CanSelectTask && p is PersonalTaskItem row && Items.Contains(row));
        AddSubtaskCommand = new(async (_, _) =>
        {
            var parent = Selected!.Source;
            await OpenAsync(null, draft => draft.Card.SeedParent(parent.Id, parent.Title));
        }, _ => CanAddSubtask);
        SaveCommand = new(async (_, _) => await SaveAsync(), _ => Editor?.CanSubmit == true && !IsBusy);
        CancelCommand = new((_, _) => { Editor = null; _createContext = null; Message = ""; Filter(); return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsBusy);
        CaptureCommand = new(async (_, _) => await CaptureAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(CaptureText) && CaptureText.Trim().Length <= 500);
        TransitionCommand = new(async (p, _) => { if (p is DesktopTaskStatus status) await TransitionAsync(status); }, p => CanSelectTask && Selected is not null && p is DesktopTaskStatus status && TaskRules.CanTransition((TaskWorkStatus)Selected.Source.Status, (TaskWorkStatus)status));
        ReloadEditorCommand = new(async (_, _) => await ReloadEditorAsync(), _ => !IsBusy && Editor?.HasConflict == true);
        AddCheckCommand = new(async (_, _) => await ChecklistWriteAsync(null), _ => CanEdit && Editor is null && !string.IsNullOrWhiteSpace(CheckText) && CheckText.Trim().Length <= 2000);
        ToggleCheckCommand = new(async (p, _) => { if (p is TaskWorkspaceItem item) await ChecklistWriteAsync(item); }, _ => CanEdit && CanSelectTask);
        RemoveCheckCommand = new(async (p, _) => { if (p is TaskWorkspaceItem item) await ChecklistWriteAsync(item, true); }, _ => CanEdit && CanSelectTask);
        foreach (var command in Commands) command.ExecutionFailed += _ => Message = "Не удалось выполнить действие. Введённые данные остались в форме.";
    }
    private IEnumerable<AsyncCommand> Commands => [OpenRowCommand, AddSubtaskCommand, RefreshCommand, NewCommand, OpenTasksCommand, OpenSavedTaskCommand, CreateAnotherTaskCommand, EditCommand, SaveCommand, CancelCommand, CaptureCommand, TransitionCommand, ReloadEditorCommand, AddCheckCommand, ToggleCheckCommand, RemoveCheckCommand, ShowCompletedCommand];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand OpenRowCommand { get; }
    public AsyncCommand AddSubtaskCommand { get; }
    private bool _detailsExpanded = true;
    public bool DetailsExpanded { get => _detailsExpanded; set => SetProperty(ref _detailsExpanded, value); }
    public bool CanAddSubtask => CanSelectTask && CanEdit && Selected?.Source.Card?.ParentTaskId is null;
    public string SubtaskActionHint => !CanSelectTask ? "Сначала завершите текущее действие или сохраните черновик."
        : !CanAddSubtask ? "Подзадачу можно добавить только к незавершённой задаче верхнего уровня."
        : "Открыть форму с выбранной родительской задачей.";
    public async System.Threading.Tasks.Task CreateFromProjectAsync(PersonalProject project)
    {
        if (!CanNavigate || project.Lifecycle != "active" || !_projects().Any(p => p.Id == project.Id && p.Lifecycle == "active")) return;
        await OpenAsync(null, draft =>
        {
            if (_projects().FirstOrDefault(item => item.Id == project.Id && item.Lifecycle == "active") is { } current)
                draft.Card.SeedProject(current.Id, current.Name);
        });
    }
    public AsyncCommand OpenTasksCommand { get; }
    public AsyncCommand OpenSavedTaskCommand { get; }
    public AsyncCommand CreateAnotherTaskCommand { get; }
    public bool ShowSavedTaskAction => _savedTaskId.HasValue;
    public bool ShowCreateSuccess => _createdTask is not null;
    public bool SavedTaskHiddenByFilters => _createdTask is { } task && !MatchesTaskFilters(task);

    internal bool IsLastSavedTask(Guid id) => _savedTaskId == id;

    internal bool SelectSavedTask(Guid id)
    {
        if (!IsTasks) return false;
        if (_all.FirstOrDefault(task => task.Id == id) is { } task && !MatchesTaskFilters(task))
        {
            SearchText = "";
            StatusFilter = 0;
            PriorityFilter = 0;
            HideCompleted = false;
        }

        if (Items.FirstOrDefault(row => row.Source.Id == id) is not { } item) return false;
        Selected = item;
        DetailsExpanded = true;
        SavedTaskOpened?.Invoke();
        return true;
    }
    public bool IsToday => SelectedSection.Title == "Сегодня";
    public AsyncCommand EditCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand TransitionCommand { get; }
    public AsyncCommand ReloadEditorCommand { get; }
    public AsyncCommand AddCheckCommand { get; }
    public AsyncCommand ToggleCheckCommand { get; }
    public AsyncCommand RemoveCheckCommand { get; }
    public PersonalSection SelectedSection { get => _section; set { if (value is not null && SetProperty(ref _section, value)) { Filter(); OnPropertyChanged(nameof(IsInbox)); OnPropertyChanged(nameof(IsToday)); OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyBody)); } } }
    public bool IsInbox => SelectedSection.Title == "Пометки";
    public bool IsTasks => SelectedSection.Title == "Задачи";
    private string _inboxSearchText = "";
    public string InboxSearchText { get => _inboxSearchText; set { if (SetProperty(ref _inboxSearchText, value)) Filter(); } }
    public bool HasInboxSearch => !string.IsNullOrWhiteSpace(InboxSearchText);
    public int InboxCount => _all.Count(InboxItemViewModel.IsCaptureOnly);
    public string InboxResultCount => HasInboxSearch ? $"Найдено {Items.Count} из {InboxCount}" : $"Пометок: {InboxCount}";
    public bool InboxReadFailed => _loadFailed && !IsBusy;
    public string CaptureValidation => CaptureText.Length == 0 ? "" : string.IsNullOrWhiteSpace(CaptureText)
        ? "Введите мысль или название задачи." : CaptureText.Trim().Length > 500 ? "Не более 500 символов." : "";
    private string _searchText = "";
    private int _statusFilter, _priorityFilter, _sortOrder;
    public string[] StatusFilters { get; } = ["Все статусы", "Новая", "В работе", "На проверке", "Выполнена", "Отменена"];
    public string[] PriorityFilters { get; } = ["Все приоритеты", "Низкий", "Обычный", "Высокий", "Критический"];
    public string[] SortOrders { get; } = ["Сначала новые", "По названию · А–Я", "По приоритету ↓", "По плану · ближайшие"];
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) Filter(); } }
    public int StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value)) Filter(); } }
    public int PriorityFilter { get => _priorityFilter; set { if (SetProperty(ref _priorityFilter, value)) Filter(); } }
    public int SortOrder { get => _sortOrder; set { if (SetProperty(ref _sortOrder, value)) Filter(); } }
    private bool HasTaskQueryFilters => !string.IsNullOrWhiteSpace(SearchText) || StatusFilter > 0 || PriorityFilter > 0;
    public bool HasViewFilters => HasTaskQueryFilters || HideCompleted;
    public string ResultCount => $"Найдено {Items.Count} из {_all.Count}";
    public string SelectedProject => _projects().FirstOrDefault(p => p.Id == Selected?.Source.Card?.ProjectId)?.Name ?? "Без проекта";
    public IReadOnlyList<PersonalTaskItem> Subtasks => _all.Where(t => t.Card?.ParentTaskId == Selected?.Source.Id && Selected is not null).Select(t => new PersonalTaskItem(t)).ToArray();
    public void ResetViewFilters() { _searchText = ""; _statusFilter = _priorityFilter = _sortOrder = 0; _hideCompleted = false; foreach (var name in new[] { nameof(SearchText), nameof(StatusFilter), nameof(PriorityFilter), nameof(SortOrder), nameof(HideCompleted) }) OnPropertyChanged(name); Filter(); }
    public bool HideCompleted { get => _hideCompleted; set { if (SetProperty(ref _hideCompleted, value)) Filter(); } }
    public bool HasHiddenTasks => IsTasks && HideCompleted && _all.Any(t => TaskRules.IsTerminal((TaskWorkStatus)t.Status));
    public AsyncCommand ShowCompletedCommand => _showCompletedCommand ??= new((_, _) => { HideCompleted = false; return System.Threading.Tasks.Task.CompletedTask; });
    private AsyncCommand? _showCompletedCommand;
    public bool IsSettings => SelectedSection.Title == "Настройки";
    public IReadOnlyList<PersonalTaskItem> Items { get => _items; private set { SetProperty(ref _items, value); OnPropertyChanged(nameof(ShowEmpty)); OnPropertyChanged(nameof(ShowEmptyTodayPlan)); OnPropertyChanged(nameof(SavedTaskHiddenByFilters)); } }
    public bool HasSelection => Selected is not null;
    public bool ShowEmptyTodayPlan => IsToday && ShowEmpty && _todayCalendarIsEmpty;
    public bool ShowEmpty => _hasLoaded && !IsBusy && Items.Count == 0 && !_loadFailed;
    public string EmptyTitle => (IsInbox && HasInboxSearch) || (IsTasks && HasTaskQueryFilters) ? "Ничего не найдено" : HasHiddenTasks ? "Нет незавершённых задач" : IsInbox ? "Все пометки разобраны" : SelectedSection.Title == "Сегодня" ? "На сегодня задач нет" : "Задач пока нет";
    public string EmptyBody => IsInbox && HasInboxSearch ? "Попробуйте другое название или очистите поиск." : IsTasks && HasTaskQueryFilters ? "Измените запрос или сбросьте фильтры." : HasHiddenTasks ? "Завершённые и отменённые задачи скрыты фильтром." : IsInbox ? "Запишите новую задачу в поле выше." : "Создайте задачу или запланируйте существующую.";
    public PersonalTaskItem? Selected
    {
        get => _selected;
        set
        {
            if (IsInbox && (HasEditor || !string.IsNullOrEmpty(CheckText)) && value?.Source.Id != _selected?.Source.Id) return;
            if (value is null && (_filtering || _selected is { } old && HasDrafts && !Items.Any(t => t.Source.Id == old.Source.Id))) return;
            if (SetProperty(ref _selected, value)) { OnPropertyChanged(nameof(SelectedProject)); OnPropertyChanged(nameof(Subtasks)); Checklist = []; OnPropertyChanged(nameof(Checklist)); OnPropertyChanged(nameof(HasSelection)); Notify(); if (value is not null) _ = LoadChecklistAsync(value.Source.Id); }
        }
    }
    public TaskEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (_editor is not null) _editor.PropertyChanged -= EditorChanged;
            SetProperty(ref _editor, value);
            if (_editor is not null) _editor.PropertyChanged += EditorChanged;
            OnPropertyChanged(nameof(HasEditor)); Notify();
        }
    }
    private void EditorChanged(object? sender, PropertyChangedEventArgs e) => SaveCommand.RaiseCanExecuteChanged();
    public bool HasEditor => Editor is not null;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(IsNotBusy)); OnPropertyChanged(nameof(ShowEmpty)); OnPropertyChanged(nameof(ShowEmptyTodayPlan)); OnPropertyChanged(nameof(InboxReadFailed)); Notify(); } }
    public bool IsNotBusy => !IsBusy;
    public bool CanEdit => !IsBusy && Selected is not null && !TaskRules.IsTerminal((TaskWorkStatus)Selected.Source.Status);
    public string CaptureText { get => _capture; set { SetProperty(ref _capture, value); OnPropertyChanged(nameof(CaptureValidation)); OnPropertyChanged(nameof(CanNavigate)); CaptureCommand.RaiseCanExecuteChanged(); OpenTasksCommand.RaiseCanExecuteChanged(); OpenSavedTaskCommand.RaiseCanExecuteChanged(); } }
    public string CheckText { get => _check; set { SetProperty(ref _check, value); Notify(); } }
    public bool CanSelectTask => !IsBusy && Editor is null && string.IsNullOrEmpty(CheckText);
    public bool CanNavigate => CanSelectTask && string.IsNullOrEmpty(CaptureText);
    public string Message
    {
        get => _message;
        private set
        {
            _savedTaskId = null;
            _createdTask = null;
            _savedCreateContext = null;
            SetProperty(ref _message, value);
            OnPropertyChanged(nameof(ShowEmpty));
            NotifySavedTask();
        }
    }

    private void NotifySavedTask()
    {
        OnPropertyChanged(nameof(ShowSavedTaskAction));
        OnPropertyChanged(nameof(ShowCreateSuccess));
        OnPropertyChanged(nameof(SavedTaskHiddenByFilters));
        OpenSavedTaskCommand.RaiseCanExecuteChanged();
        CreateAnotherTaskCommand.RaiseCanExecuteChanged();
    }

    public IReadOnlyList<TaskWorkspaceItem> Checklist { get; private set; } = [];
    public bool HasDrafts => Editor is not null || !string.IsNullOrEmpty(CaptureText) || !string.IsNullOrEmpty(CheckText);

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try { await ReloadAsync(); if (!_loadFailed) Message = ""; }
        finally { IsBusy = false; }
    }
    private async System.Threading.Tasks.Task ReloadAsync()
    {
        _loadFailed = true;
        _todayCalendarIsEmpty = false;
        var result = await _client.GetTasksAsync(cancellationToken: _lifetime.Token);
        if (result is DesktopTasksApiResult<DesktopTaskPage>.Succeeded ok)
        {
            _loadFailed = false;
            _hasLoaded = true;
            _all = ok.Value.Items;
            if (IsToday && _calendar is not null)
            {
                var zone = TimeZoneInfo.Local;
                var today = DateOnly.FromDateTime(DateTime.Today);
                var (from, to) = CalendarViewModel.GetUtcRange(today, today.AddDays(1), zone);
                var schedule = await _calendar.GetScheduleAsync(from, to, zone.Id, _lifetime.Token);
                _todayCalendarIsEmpty = schedule is Calendar.DesktopCalendarResult<Calendar.DesktopSchedulePage>.Succeeded day
                    && day.Value.Items.All(item => item.Status is "completed" or "cancelled");
            }
            Filter();
            if (Editor is null && string.IsNullOrEmpty(CheckText) && Selected is { } selection
                && Items.FirstOrDefault(t => t.Source.Id == selection.Source.Id) is { } refreshed) Selected = refreshed;
        }
        else { _loadFailed = true; Message = "Не удалось прочитать личную базу. Проверьте доступ к файлу и обновите список."; }
        OnPropertyChanged(nameof(ShowEmpty));
    }
    private void Filter()
    {
        IEnumerable<DesktopTaskDto> tasks = _all;
        if (IsInbox)
        {
            tasks = tasks.Where(InboxItemViewModel.IsCaptureOnly);
            if (HasInboxSearch) tasks = tasks.Where(t => t.Title.Contains(InboxSearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
            tasks = tasks.OrderByDescending(t => t.CreatedAtUtc).ThenBy(t => t.Id);
        }
        else if (SelectedSection.Title == "Сегодня")
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            tasks = tasks.Where(t => !TaskRules.IsTerminal((TaskWorkStatus)t.Status) &&
                (t.Card?.ScheduledDate == today || t.StartAtUtc?.ToLocalTime().Date == DateTime.Today
                    || t.DeadlineAtUtc?.ToLocalTime().Date <= DateTime.Today));
        }
        if (IsTasks)
        {
            tasks = tasks.Where(MatchesTaskFilters);
            tasks = SortOrder switch
            {
                1 => tasks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id),
                2 => tasks.OrderByDescending(t => t.Priority).ThenBy(t => t.Title).ThenBy(t => t.Id),
                3 => tasks.OrderBy(t => t.StartAtUtc?.ToLocalTime().Date ?? t.Card?.ScheduledDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue).ThenBy(t => t.Title).ThenBy(t => t.Id),
                _ => tasks.OrderByDescending(t => t.CreatedAtUtc).ThenBy(t => t.Id)
            };
        }
        var selected = Selected;
        var selectedIndex = selected is null ? 0 : Math.Max(0, Items.ToList().FindIndex(t => t.Source.Id == selected.Source.Id));
        _filtering = true;
        try
        {
            Items = tasks.Select(t => new PersonalTaskItem(t)).ToArray();
            if (IsInbox) _filtering = false;
            // A checklist draft owns its selection and loaded checklist until it is saved.
            if (!HasDrafts) Selected = Items.FirstOrDefault(t => t.Source.Id == selected?.Source.Id)
                ?? (IsInbox && Items.Count > 0 ? Items[Math.Min(selectedIndex, Items.Count - 1)] : null);
        }
        finally { _filtering = false; }
        if (!IsInbox && !HasDrafts && selected is not null && !Items.Any(t => t.Source.Id == selected.Source.Id)) Selected = null;
        OnPropertyChanged(nameof(InboxCount)); OnPropertyChanged(nameof(InboxResultCount)); OnPropertyChanged(nameof(HasInboxSearch));
        OnPropertyChanged(nameof(ResultCount)); OnPropertyChanged(nameof(HasViewFilters)); OnPropertyChanged(nameof(Subtasks));
        OnPropertyChanged(nameof(IsTasks)); OnPropertyChanged(nameof(HasHiddenTasks));
        OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyBody));
    }
    private bool MatchesTaskFilters(DesktopTaskDto task) =>
        (!HideCompleted || !TaskRules.IsTerminal((TaskWorkStatus)task.Status))
        && (string.IsNullOrWhiteSpace(SearchText) || task.Title.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
        && (StatusFilter <= 0 || (int)task.Status == StatusFilter - 1)
        && (PriorityFilter <= 0 || (int)task.Priority == PriorityFilter - 1);

    private async System.Threading.Tasks.Task OpenAsync(DesktopTaskDto? source, Action<TaskEditorViewModel>? context = null)
    {
        if (!CanSelectTask) return;
        // Fresh choices are derived exclusively from the same local store.
        await RefreshAsync();
        if (_disposed || Editor is not null) return;
        if (source is not null)
        {
            source = _all.FirstOrDefault(t => t.Id == source.Id);
            if (source is null || TaskRules.IsTerminal((TaskWorkStatus)source.Status)) { DetailsExpanded = true; return; }
        }
        var editor = new TaskEditorViewModel(source is null ? TaskEditorMode.Create : TaskEditorMode.Edit, source, personal: true);
        _createContext = context;
        context?.Invoke(editor);
        Editor = editor;
        if (await _client.GetDeadlineSettingsAsync(_lifetime.Token) is { } settings && ReferenceEquals(Editor, editor))
            editor.SetReminderDefaults(settings);
        if (ReferenceEquals(Editor, editor)) SetChoices();
    }
    private void SetChoices()
    {
        var choices = _all.Where(t => t.Id != Editor?.SourceId && t.Card?.ParentTaskId is null && !TaskRules.IsTerminal((TaskWorkStatus)t.Status))
            .Select(t => (JsonNode)new JsonObject { ["id"] = t.Id.ToString("D"), ["name"] = t.Title }).ToArray();
        var projects = _projects().Where(p => p.Lifecycle == "active").Select(p => (JsonNode)new JsonObject { ["id"] = p.Id.ToString("D"), ["name"] = p.Name }).ToArray();
        Editor!.Card.SetOptions(new JsonObject { ["tasks"] = new JsonArray(choices), ["projects"] = new JsonArray(projects) });
    }
    private async System.Threading.Tasks.Task SaveAsync()
    {
        var editor = Editor; if (editor is null || IsBusy) return;
        var create = editor.Mode == TaskEditorMode.Create ? editor.BuildCreateCommand() : null;
        var patch = editor.Mode == TaskEditorMode.Edit ? editor.BuildPatchCommand() : null;
        if (create is null && patch is null) return;
        IsBusy = true; editor.IsBusy = true;
        try
        {
            var result = create is not null ? await _client.CreateTaskAsync(create, _lifetime.Token) : await _client.PatchTaskAsync(patch!, _lifetime.Token);
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success)
            {
                Editor = null;
                if (create is not null)
                {
                    Message = InboxItemViewModel.IsCaptureOnly(success.Value)
                        ? $"Задача «{success.Value.Title}» создана в «Пометках» и доступна в разделе «Задачи»."
                        : $"Задача «{success.Value.Title}» создана в разделе «Задачи».";
                    _createdTask = success.Value;
                    _savedTaskId = success.Value.Id;
                    _savedCreateContext = _createContext;
                    _all = _all.Where(task => task.Id != success.Value.Id).Prepend(success.Value).ToArray();
                    Filter();
                }
                else
                {
                    Message = IsInbox
                        ? InboxItemViewModel.IsCaptureOnly(success.Value) ? "Изменения сохранены. Запись остаётся в «Пометках»." : "Запись разобрана и доступна в разделе «Задачи»."
                        : "Изменения сохранены. Задача доступна в разделе «Задачи».";
                    await ReloadAsync();
                    if (!_loadFailed) _savedTaskId = success.Value.Id;
                }

                _createContext = null;
                NotifySavedTask();
            }
            else
            {
                var message = Error(result);
                if (result is DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation)
                    editor.ApplyServerValidation(message, validation.FieldErrors);
                else
                    editor.SetStatus(message);
                Message = message;
                if (result is DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict) { editor.SetConflict(); editor.SetStatus(message); }
            }
        }
        finally { editor.IsBusy = false; IsBusy = false; }
    }
    private async System.Threading.Tasks.Task CaptureAsync()
    {
        var text = CaptureText; IsBusy = true;
        try
        {
            var result = await _client.CreateTaskAsync(new(text.Trim(), DesktopTaskPriority.Normal), _lifetime.Token);
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded) { if (CaptureText == text) CaptureText = ""; Message = "Задача добавлена в «Пометки»."; await ReloadAsync(); }
            else Message = Error(result);
        }
        finally { IsBusy = false; }
    }
    private async System.Threading.Tasks.Task TransitionAsync(DesktopTaskStatus status)
    {
        if (Selected is not { } item) return; IsBusy = true;
        try
        {
            var result = await _client.TransitionTaskAsync(new(item.Source.Id, item.Source.Version, status), _lifetime.Token);
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded) { Message = "Статус сохранён."; await ReloadAsync(); }
            else Message = Error(result);
        }
        finally { IsBusy = false; }
    }
    private async System.Threading.Tasks.Task ReloadEditorAsync()
    {
        if (Editor?.SourceId is not { } id) return;
        var result = await _client.GetTaskByIdAsync(id, _lifetime.Token);
        if (result is DesktopTasksApiResult<DesktopTaskDto>.Succeeded ok) { Editor.LoadLatest(ok.Value); SetChoices(); }
        else Message = "Не удалось загрузить актуальную задачу. Черновик сохранён.";
    }
    private async System.Threading.Tasks.Task LoadChecklistAsync(Guid id)
    {
        try
        {
            var items = await _client.GetChecklistAsync(id, _lifetime.Token);
            if (!_disposed && Selected?.Source.Id == id) { Checklist = items; OnPropertyChanged(nameof(Checklist)); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed) Message = "Не удалось прочитать чек-лист. Обновите список."; }
    }
    private async System.Threading.Tasks.Task ChecklistWriteAsync(TaskWorkspaceItem? item, bool remove = false)
    {
        if (Selected is not { } selected) return; var text = CheckText; IsBusy = true;
        try
        {
            var result = await _client.WriteChecklistAsync(selected.Source.Id, selected.Source.Version, item?.Id, item is null ? text : null, item is null ? null : !item.Completed, remove, _lifetime.Token);
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded)
            {
                if (item is null && CheckText == text) CheckText = "";
                Message = "Чек-лист сохранён."; await ReloadAsync(); await LoadChecklistAsync(selected.Source.Id);
            }
            else Message = Error(result);
        }
        finally { IsBusy = false; }
    }
    private static string Error(DesktopTaskWriteResult<DesktopTaskDto> result) => result switch
    {
        DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict => "Задача уже изменена в личной базе. Черновик сохранён; загрузите актуальную версию для повторного редактирования.",
        DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure failure => failure.Message,
        DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition => "Действие недоступно в текущем статусе задачи.",
        DesktopTaskWriteResult<DesktopTaskDto>.NotFound => "Задача или пункт больше не доступны. Обновите список.",
        _ => "Данные не сохранены в личную базу. Введённые значения остались в форме; проверьте свободное место и повторите действие.",
    };
    internal IReadOnlyList<ModeSwitchEditor> InspectDrafts() =>
    [
        new("Личная задача", () => Editor is not null, () => IsBusy, SaveCommand),
        new("Новая пометка", () => !string.IsNullOrEmpty(CaptureText), () => IsBusy, CaptureCommand),
        new("Чек-лист", () => !string.IsNullOrEmpty(CheckText), () => IsBusy, AddCheckCommand),
    ];
    private void Notify() { OnPropertyChanged(nameof(CanAddSubtask)); OnPropertyChanged(nameof(SubtaskActionHint)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSelectTask)); OnPropertyChanged(nameof(CanNavigate)); foreach (var command in Commands) command?.RaiseCanExecuteChanged(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _lifetime.Cancel(); foreach (var command in Commands) command.Dispose();
        Editor = null; _lifetime.Dispose();
    }
}
