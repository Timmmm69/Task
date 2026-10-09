using System.Globalization;
using Task.Desktop.Calendar;
using Task.Desktop.Modes;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Domain.Reminders;

namespace Task.Desktop.Personal;

public sealed record PersonalReminderTarget(Guid Id, DesktopScheduleItemType Kind, string Title)
{
    public override string ToString() => Title;
}
public sealed record PersonalReminderTrigger(ReminderTriggerType Type, string Name);
public sealed record PersonalProjectStatusChoice(DesktopProjectStatus Value, string Name);
public sealed record PersonalLifecycleChoice(string Value, string Name);
public sealed record PersonalReminderItem(PersonalReminder Source, string Title)
{
    public override string ToString() => Title;
    public string State => Source.State switch { "pending" => "Ожидает", "due" => "Пора напомнить", "delivered" => "Доставлено", "expired" => "Неактуально", _ => "Отменено" };
    public string DueText => Source.DueAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Ожидает точного времени";
}

public sealed class PersonalPlanningViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalTaskStore _store;
    private readonly PersonalPlanningScheduler _scheduler;
    private readonly TimeProvider _clock;
    private IReadOnlyList<PersonalProject> _projects = [];
    private IReadOnlyList<PersonalReminderTarget> _targets = [];
    private IReadOnlyList<PersonalReminderItem> _reminders = [];
    private IReadOnlyList<PersonalNotification> _notifications = [];
    private PersonalProject? _selectedProject, _projectSource;
    private PersonalReminderTarget? _target;
    private PersonalReminderItem? _selectedReminder;
    private PersonalNotification? _selectedNotification;
    private bool _projectEditing, _busy;
    private string _name = "", _description = "", _offset = "15", _absolute = "", _message = "";
    private DateTime? _start, _end;
    private DesktopProjectStatus _status;
    private string _lifecycle = "active";
    private PersonalReminderTrigger _trigger;
    private string _defaultOffset = "15";
    public PersonalPlanningViewModel(PersonalTaskStore store, TimeProvider? clock = null)
    {
        _store = store; _clock = clock ?? TimeProvider.System; _scheduler = new(store, _clock); _trigger = Triggers[0];
        _offset = store.WorkspaceSettings().DefaultReminderOffsetMinutes.ToString(CultureInfo.InvariantCulture);
        _defaultOffset = _offset;
        RefreshCommand = new((_, _) => Run(Refresh), _ => !IsBusy);
        NewProjectCommand = new((_, _) => { EditProject(null); return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsBusy && !IsProjectEditing);
        EditProjectCommand = new((_, _) => { EditProject(SelectedProject); return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsBusy && !IsProjectEditing && SelectedProject is not null);
        SaveProjectCommand = new((_, _) => Run(SaveProject), _ => !IsBusy && IsProjectEditing);
        CancelProjectCommand = new((_, _) => { IsProjectEditing = false; return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsBusy);
        AddReminderCommand = new((_, _) => Run(AddReminder), _ => !IsBusy && Target is not null);
        CancelReminderCommand = new((_, _) => Run(() => { var r = SelectedReminder!.Source; _store.CancelReminder(r.Id, r.Version); Refresh(); }), _ => !IsBusy && SelectedReminder?.Source.State is "pending" or "due" or "delivered");
        ReadNotificationCommand = new((_, _) => Run(() => { _store.MarkNotificationRead(SelectedNotification!.Id); Refresh(); }), _ => !IsBusy && SelectedNotification is not null);
        ReadAllNotificationsCommand = new((_, _) => Run(() => { _store.MarkAllPersonalNotificationsRead(); Refresh(); }), _ => !IsBusy && Notifications.Any(n => !n.IsRead));
        SnoozeNotificationCommand = new((_, _) => Run(() => Snooze(SelectedNotification!.Id)), _ => !IsBusy && SelectedNotification is { IsRead: false });
        OpenNotificationCommand = new((_, _) => Run(() => OpenNotification(SelectedNotification!.Id)), _ => !IsBusy && SelectedNotification is not null);
        Refresh();
    }
    public IReadOnlyList<DesktopProjectStatus> Statuses { get; } = Enum.GetValues<DesktopProjectStatus>();
    public IReadOnlyList<string> Lifecycles { get; } = ["active", "archived", "trashed"];
    public IReadOnlyList<PersonalProjectStatusChoice> StatusChoices { get; } = [new(DesktopProjectStatus.Planning, "Планирование"), new(DesktopProjectStatus.Active, "Активен"), new(DesktopProjectStatus.Paused, "Приостановлен"), new(DesktopProjectStatus.Completed, "Завершён")];
    public IReadOnlyList<PersonalLifecycleChoice> LifecycleChoices { get; } = [new("active", "Активный"), new("archived", "В архиве"), new("trashed", "В корзине")];
    public IReadOnlyList<PersonalReminderTrigger> Triggers { get; } =
    [new(ReminderTriggerType.BeforeStart, "До начала"), new(ReminderTriggerType.AtStart, "В момент начала"),
        new(ReminderTriggerType.BeforeDeadline, "До срока"), new(ReminderTriggerType.AtDeadline, "В момент срока"), new(ReminderTriggerType.Absolute, "В конкретное время")];
    public IReadOnlyList<PersonalProject> Projects { get => _projects; private set => SetProperty(ref _projects, value); }
    public IReadOnlyList<PersonalReminderTarget> Targets { get => _targets; private set => SetProperty(ref _targets, value); }
    public IReadOnlyList<PersonalReminderItem> Reminders { get => _reminders; private set => SetProperty(ref _reminders, value); }
    public IReadOnlyList<PersonalNotification> Notifications { get => _notifications; private set => SetProperty(ref _notifications, value); }
    public PersonalProject? SelectedProject { get => _selectedProject; set { SetProperty(ref _selectedProject, value); Notify(); } }
    public PersonalReminderTarget? Target { get => _target; set { SetProperty(ref _target, value); Notify(); } }
    public PersonalReminderItem? SelectedReminder { get => _selectedReminder; set { SetProperty(ref _selectedReminder, value); Notify(); } }
    public PersonalNotification? SelectedNotification { get => _selectedNotification; set { SetProperty(ref _selectedNotification, value); Notify(); } }
    public bool IsProjectEditing { get => _projectEditing; private set { SetProperty(ref _projectEditing, value); Notify(); } }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); } }
    public string ProjectName { get => _name; set => SetProperty(ref _name, value); }
    public string ProjectDescription { get => _description; set => SetProperty(ref _description, value); }
    public DesktopProjectStatus ProjectStatus { get => _status; set => SetProperty(ref _status, value); }
    public DateTime? StartDate { get => _start; set => SetProperty(ref _start, value); }
    public DateTime? EndDate { get => _end; set => SetProperty(ref _end, value); }
    public string Lifecycle { get => _lifecycle; set => SetProperty(ref _lifecycle, value); }
    public string OffsetMinutes { get => _offset; set => SetProperty(ref _offset, value); }
    public string AbsoluteTime { get => _absolute; set => SetProperty(ref _absolute, value); }
    public PersonalReminderTrigger Trigger { get => _trigger; set { SetProperty(ref _trigger, value); OnPropertyChanged(nameof(IsRelativeReminder)); OnPropertyChanged(nameof(IsAbsoluteReminder)); } }
    public bool IsAbsoluteReminder => Trigger.Type == ReminderTriggerType.Absolute;
    public bool IsRelativeReminder => Trigger.Type is ReminderTriggerType.BeforeStart or ReminderTriggerType.BeforeDeadline;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    internal void ReportPresentationError(string message) => Message = message;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewProjectCommand { get; }
    public AsyncCommand EditProjectCommand { get; }
    public AsyncCommand SaveProjectCommand { get; }
    public AsyncCommand CancelProjectCommand { get; }
    public AsyncCommand AddReminderCommand { get; }
    public AsyncCommand CancelReminderCommand { get; }
    public AsyncCommand ReadNotificationCommand { get; }
    public AsyncCommand ReadAllNotificationsCommand { get; }
    public AsyncCommand SnoozeNotificationCommand { get; }
    public AsyncCommand OpenNotificationCommand { get; }
    public IReadOnlyList<string> SnoozeChoices { get; } = ["По умолчанию", "5 минут", "15 минут", "30 минут", "1 час", "До завтра", "Дата и время"];
    public string SnoozeChoice { get; set; } = "По умолчанию";
    public string SnoozeDateTime { get; set; } = "";
    public event Action<string, Guid>? OpenObjectRequested;
    public void Snooze(Guid id, bool useDefault = false)
    {
        DateTimeOffset? until = useDefault ? null : SnoozeChoice switch
        {
            "5 минут" => _clock.GetUtcNow().AddMinutes(5), "15 минут" => _clock.GetUtcNow().AddMinutes(15),
            "30 минут" => _clock.GetUtcNow().AddMinutes(30), "1 час" => _clock.GetUtcNow().AddHours(1),
            "До завтра" => PersonalTimePolicy.ToUtc(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZoneInfo.Local).Date.AddDays(1).AddHours(9), TimeZoneInfo.Local),
            "Дата и время" => PersonalTimePolicy.ToUtc(DateTime.ParseExact(SnoozeDateTime.Trim(), "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), TimeZoneInfo.Local),
            _ => null,
        };
        _store.SnoozeNotification(id, until); Refresh();
    }
    public void OpenNotification(Guid id)
    {
        var notification = _store.Notifications().FirstOrDefault(n => n.Id == id);
        if (notification is null) return;
        var reminder = _store.Reminders().FirstOrDefault(r => r.Id == notification.ReminderId);
        if (reminder is null) return;
        if (reminder.TargetKind == DesktopScheduleItemType.Task && _store.Get(notification.TargetId) is not null) OpenObjectRequested?.Invoke("task", notification.TargetId);
        else if (reminder.TargetKind == DesktopScheduleItemType.CalendarEvent && _store.GetEvent(notification.TargetId) is { Status: "scheduled" }) OpenObjectRequested?.Invoke("calendar_event", notification.TargetId);
    }
    public event Action? ProjectsChanged;
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, NewProjectCommand, EditProjectCommand, SaveProjectCommand, CancelProjectCommand, AddReminderCommand, CancelReminderCommand, ReadNotificationCommand, ReadAllNotificationsCommand, SnoozeNotificationCommand, OpenNotificationCommand];
    private System.Threading.Tasks.Task Run(Action action)
    {
        IsBusy = true;
        try { action(); Message = "Сохранено на этом компьютере."; }
        catch (PersonalVersionConflictException) { Message = "Данные изменены. Обновите список; черновик сохранён в форме."; }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { Message = e.Message; }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        { Message = "Данные не сохранены. Черновик остался в форме; проверьте свободное место и доступ к личной базе."; }
        finally { IsBusy = false; }
        return System.Threading.Tasks.Task.CompletedTask;
    }
    private void EditProject(PersonalProject? source)
    {
        _projectSource = source; ProjectName = source?.Name ?? ""; ProjectDescription = source?.Description ?? "";
        ProjectStatus = source?.Status ?? DesktopProjectStatus.Planning;
        StartDate = source?.StartDate?.ToDateTime(TimeOnly.MinValue); EndDate = source?.PlannedEndDate?.ToDateTime(TimeOnly.MinValue);
        Lifecycle = source?.Lifecycle ?? "active"; IsProjectEditing = true;
    }
    private void SaveProject()
    {
        var draft = new PersonalProject(_projectSource?.Id ?? Guid.Empty, _projectSource?.Version ?? 0, ProjectName, ProjectDescription,
            ProjectStatus, StartDate is { } s ? DateOnly.FromDateTime(s) : null, EndDate is { } e ? DateOnly.FromDateTime(e) : null,
            ProjectStatus == DesktopProjectStatus.Completed ? _projectSource?.ActualEndAt ?? _clock.GetUtcNow() : null,
            _projectSource?.DefaultTimeZone, _projectSource?.ColorCode, Lifecycle);
        var saved = _store.SaveProject(draft); IsProjectEditing = false; Refresh(); SelectedProject = Projects.FirstOrDefault(p => p.Id == saved.Id); ProjectsChanged?.Invoke();
    }
    private void AddReminder()
    {
        var target = Target!;
        int? offset = Trigger.Type is ReminderTriggerType.BeforeStart or ReminderTriggerType.BeforeDeadline ? int.Parse(OffsetMinutes, CultureInfo.InvariantCulture) : null;
        DateTimeOffset? at = Trigger.Type == ReminderTriggerType.Absolute
            ? PersonalTimePolicy.ToUtc(DateTime.ParseExact(AbsoluteTime.Trim(), "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), TimeZoneInfo.Local) : null;
        _store.AddReminder(target.Id, target.Kind, Trigger.Type, offset, at);
        Target = null; AbsoluteTime = ""; OffsetMinutes = _store.WorkspaceSettings().DefaultReminderOffsetMinutes.ToString(CultureInfo.InvariantCulture); Refresh();
    }
    public void Refresh()
    {
        var configuredOffset = _store.WorkspaceSettings().DefaultReminderOffsetMinutes.ToString(CultureInfo.InvariantCulture);
        if (_offset == _defaultOffset) OffsetMinutes = configuredOffset;
        _defaultOffset = configuredOffset;
        var projectId = SelectedProject?.Id; Projects = _store.Projects();
        SelectedProject = projectId is { } id ? Projects.FirstOrDefault(p => p.Id == id) : null;
        Targets = _store.List().Where(t => t.Status is not (TaskApi.DesktopTaskStatus.Completed or TaskApi.DesktopTaskStatus.Cancelled))
            .Select(t => new PersonalReminderTarget(t.Id, DesktopScheduleItemType.Task, t.Title))
            .Concat(_store.Events().Where(e => e.Status == "scheduled").Select(e => new PersonalReminderTarget(e.Id, DesktopScheduleItemType.CalendarEvent, e.Title))).ToArray();
        Reminders = _store.Reminders().Select(r => new PersonalReminderItem(r, Targets.FirstOrDefault(t => t.Id == r.TargetId)?.Title
            ?? _store.Get(r.TargetId)?.Title ?? _store.GetEvent(r.TargetId)?.Title ?? "Недоступный объект")).ToArray();
        var selected = SelectedNotification?.Id;
        Notifications = _store.Notifications(); SelectedNotification = Notifications.FirstOrDefault(n => n.Id == selected);
        Notify();
    }
    public void Reconcile()
    {
        if (IsBusy) return;
        try { if (_scheduler.RunPass() > 0) Refresh(); if (_scheduler.LastError is { } error) Message = "Не удалось расширить серию: " + error; }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Message = "Локальный планировщик повторит попытку. " + e.Message; }
    }
    internal IReadOnlyList<ModeSwitchEditor> InspectDrafts() =>
    [new("Личный проект", () => IsProjectEditing, () => IsBusy, SaveProjectCommand),
        new("Личное напоминание", () => Target is not null || AbsoluteTime.Length != 0 || OffsetMinutes != _defaultOffset, () => IsBusy, AddReminderCommand)];
    private void Notify() { foreach (var command in Commands) command?.RaiseCanExecuteChanged(); }
    public void Dispose() { foreach (var command in Commands) command.Dispose(); }
}
