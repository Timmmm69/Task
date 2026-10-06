using System.ComponentModel;
using System.Text.Json.Nodes;
using Task.Desktop.Modes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Personal;

public sealed record PersonalTaskItem(DesktopTaskDto Source)
{
    // WPF automation must never expose the compatibility DTO's technical actor.
    public override string ToString() => Title;
    public string Title => Source.Title;
    public string StatusText => TaskItemViewModel.LocalizeStatus(Source.Status);
    public string PriorityText => TaskItemViewModel.LocalizePriority(Source.Priority);
    public string ScheduleText => Source.StartAtUtc is { } start ? start.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        : Source.Card?.ScheduledDate?.ToString("dd.MM.yyyy") ?? "Без даты";
    public string DeadlineText => TaskItemViewModel.FormatDate(Source.DeadlineAtUtc, "Без срока");
}

/// <summary>Personal presentation uses the shared draft, statuses and Inbox semantics.</summary>
public sealed class PersonalTasksViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalTasksClient _client;
    private readonly Func<IReadOnlyList<PersonalProject>> _projects;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<DesktopTaskDto> _all = [];
    private bool _loadFailed;
    private IReadOnlyList<PersonalTaskItem> _items = [];
    private PersonalTaskItem? _selected;
    private TaskEditorViewModel? _editor;
    private string _capture = "", _check = "", _message = "";
    private bool _busy, _disposed;
    private PersonalSection _section;
    public PersonalTasksViewModel(PersonalTasksClient client, IReadOnlyList<PersonalSection> sections, Func<IReadOnlyList<PersonalProject>>? projects = null)
    {
        _projects = projects ?? (() => []);
        _client = client; _section = sections.First(s => s.Title == "Задачи");
        RefreshCommand = new(async (_, _) => await RefreshAsync(), _ => !IsBusy && string.IsNullOrEmpty(CheckText));
        NewCommand = new(async (_, _) => await OpenAsync(null), _ => CanSelectTask);
        EditCommand = new(async (_, _) => await OpenAsync(Selected?.Source), _ => CanEdit && CanSelectTask);
        SaveCommand = new(async (_, _) => await SaveAsync(), _ => Editor?.CanSubmit == true && !IsBusy);
        CancelCommand = new((_, _) => { Editor = null; Message = ""; return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsBusy);
        CaptureCommand = new(async (_, _) => await CaptureAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(CaptureText) && CaptureText.Trim().Length <= 500);
        TransitionCommand = new(async (p, _) => { if (p is DesktopTaskStatus status) await TransitionAsync(status); }, p => CanSelectTask && Selected is not null && p is DesktopTaskStatus status && TaskRules.CanTransition((TaskWorkStatus)Selected.Source.Status, (TaskWorkStatus)status));
        ReloadEditorCommand = new(async (_, _) => await ReloadEditorAsync(), _ => !IsBusy && Editor?.HasConflict == true);
        AddCheckCommand = new(async (_, _) => await ChecklistWriteAsync(null), _ => CanEdit && Editor is null && !string.IsNullOrWhiteSpace(CheckText) && CheckText.Trim().Length <= 2000);
        ToggleCheckCommand = new(async (p, _) => { if (p is TaskWorkspaceItem item) await ChecklistWriteAsync(item); }, _ => CanEdit && CanSelectTask);
        RemoveCheckCommand = new(async (p, _) => { if (p is TaskWorkspaceItem item) await ChecklistWriteAsync(item, true); }, _ => CanEdit && CanSelectTask);
        foreach (var command in Commands) command.ExecutionFailed += _ => Message = "Не удалось выполнить действие. Введённые данные остались в форме.";
    }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, NewCommand, EditCommand, SaveCommand, CancelCommand, CaptureCommand, TransitionCommand, ReloadEditorCommand, AddCheckCommand, ToggleCheckCommand, RemoveCheckCommand];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand TransitionCommand { get; }
    public AsyncCommand ReloadEditorCommand { get; }
    public AsyncCommand AddCheckCommand { get; }
    public AsyncCommand ToggleCheckCommand { get; }
    public AsyncCommand RemoveCheckCommand { get; }
    public PersonalSection SelectedSection { get => _section; set { if (value is not null && SetProperty(ref _section, value)) { Filter(); OnPropertyChanged(nameof(IsInbox)); OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyBody)); } } }
    public bool IsInbox => SelectedSection.Title == "Входящие";
    public bool IsSettings => SelectedSection.Title == "Настройки";
    public IReadOnlyList<PersonalTaskItem> Items { get => _items; private set { SetProperty(ref _items, value); OnPropertyChanged(nameof(ShowEmpty)); } }
    public bool HasSelection => Selected is not null;
    public bool ShowEmpty => !IsBusy && Items.Count == 0 && !_loadFailed;
    public string EmptyTitle => IsInbox ? "Входящие разобраны" : SelectedSection.Title == "Сегодня" ? "На сегодня задач нет" : "Задач пока нет";
    public string EmptyBody => IsInbox ? "Запишите новую задачу в поле выше." : "Создайте задачу или запланируйте существующую.";
    public PersonalTaskItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { Checklist = []; OnPropertyChanged(nameof(Checklist)); OnPropertyChanged(nameof(HasSelection)); Notify(); if (value is not null) _ = LoadChecklistAsync(value.Source.Id); } }
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
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(IsNotBusy)); OnPropertyChanged(nameof(ShowEmpty)); Notify(); } }
    public bool IsNotBusy => !IsBusy;
    public bool CanEdit => !IsBusy && Selected is not null && !TaskRules.IsTerminal((TaskWorkStatus)Selected.Source.Status);
    public string CaptureText { get => _capture; set { SetProperty(ref _capture, value); OnPropertyChanged(nameof(CanNavigate)); CaptureCommand.RaiseCanExecuteChanged(); } }
    public string CheckText { get => _check; set { SetProperty(ref _check, value); Notify(); } }
    public bool CanSelectTask => !IsBusy && Editor is null && string.IsNullOrEmpty(CheckText);
    public bool CanNavigate => CanSelectTask && string.IsNullOrEmpty(CaptureText);
    public string Message { get => _message; private set { SetProperty(ref _message, value); OnPropertyChanged(nameof(ShowEmpty)); } }
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
        var result = await _client.GetTasksAsync(cancellationToken: _lifetime.Token);
        if (result is DesktopTasksApiResult<DesktopTaskPage>.Succeeded ok)
        {
            _loadFailed = false;
            _all = ok.Value.Items; Filter();
            if (Selected is { } selection) Selected = Items.FirstOrDefault(t => t.Source.Id == selection.Source.Id);
        }
        else { _loadFailed = true; Message = "Не удалось прочитать личную базу. Проверьте доступ к файлу и обновите список."; }
        OnPropertyChanged(nameof(ShowEmpty));
    }
    private void Filter()
    {
        IEnumerable<DesktopTaskDto> tasks = _all;
        if (IsInbox) tasks = tasks.Where(InboxItemViewModel.IsCaptureOnly);
        else if (SelectedSection.Title == "Сегодня")
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            tasks = tasks.Where(t => !TaskRules.IsTerminal((TaskWorkStatus)t.Status) &&
                (t.Card?.ScheduledDate == today || t.DeadlineAtUtc?.ToLocalTime().Date <= DateTime.Today));
        }
        Items = tasks.Select(t => new PersonalTaskItem(t)).ToArray();
    }
    private async System.Threading.Tasks.Task OpenAsync(DesktopTaskDto? source)
    {
        if (Editor is not null) return;
        // Fresh choices are derived exclusively from the same local store.
        await RefreshAsync();
        Editor = new(source is null ? TaskEditorMode.Create : TaskEditorMode.Edit, source, personal: true);
        SetChoices();
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
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded)
            {
                Editor = null; Message = "Задача сохранена."; await ReloadAsync();
            }
            else
            {
                var message = Error(result); editor.SetStatus(message); Message = message;
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
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded) { if (CaptureText == text) CaptureText = ""; Message = "Входящая задача сохранена."; await ReloadAsync(); }
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
        new("Входящая задача", () => !string.IsNullOrEmpty(CaptureText), () => IsBusy, CaptureCommand),
        new("Чек-лист", () => !string.IsNullOrEmpty(CheckText), () => IsBusy, AddCheckCommand),
    ];
    private void Notify() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSelectTask)); OnPropertyChanged(nameof(CanNavigate)); foreach (var command in Commands) command?.RaiseCanExecuteChanged(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _lifetime.Cancel(); foreach (var command in Commands) command.Dispose();
        Editor = null; _lifetime.Dispose();
    }
}
