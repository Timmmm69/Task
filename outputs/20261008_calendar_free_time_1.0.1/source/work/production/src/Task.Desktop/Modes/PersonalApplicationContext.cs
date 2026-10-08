using Task.Desktop.Personal;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Modes;

public sealed record PersonalSection(string Title, string Description);

/// <summary>Local application model. No sessions, endpoints or corporate clients.</summary>
public sealed class PersonalApplicationModel : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PersonalTaskStore? _store;
    public PersonalApplicationModel() { }
    public PersonalApplicationModel(string dataRoot, TimeProvider? clock = null)
        : this(dataRoot, clock, null) { }
    internal PersonalApplicationModel(string dataRoot, TimeProvider? clock, PersonalStoreOwnership? ownership, bool restoring = false)
    {
        var time = clock ?? TimeProvider.System;
        _store = new(new PersonalDataPaths(dataRoot), time, ownership, restoring: restoring);
        try
        {
            Tasks = new(new PersonalTasksClient(_store), Sections, _store.Projects);
            Planning = new(_store, time);
            Workspace = new(_store);
            var recurrence = new RecurrencePaneViewModel(new PersonalRecurrenceClient(_store), _store.LocalActorId, true);
            Calendar = new(new PersonalCalendarClient(_store), ["Calendar.Read", "CalendarEvent.Create", "CalendarEvent.Update", "Recurrence.Read", "Recurrence.Manage"],
                recurrence: recurrence, clock: time.GetUtcNow, personal: true,
                firstDay: () => (DayOfWeek)(_store.WorkspaceSettings().FirstDayOfWeek % 7),
                workdayStart: () => TimeOnly.Parse(_store.WorkspaceSettings().WorkdayStart),
                userSettings: _ => System.Threading.Tasks.Task.FromResult<Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopUserSettings>>(
                    new Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopUserSettings>.Succeeded(_store.WorkspaceSettings())),
                projects: () => new CalendarProjectChoice[] { new(null, "Без проекта") }.Concat(_store.Projects().Where(p => p.Lifecycle == "active").Select(p => new CalendarProjectChoice(p.Id, p.Name))).ToArray());
            _ = _store.List();
            _ = _store.Projects();
        }
        catch { Dispose(); throw; }
    }
    internal PersonalTaskStore Store => _store ?? throw new InvalidOperationException("Personal storage not initialized.");
    public PersonalTasksViewModel? Tasks { get; }
    public PersonalPlanningViewModel? Planning { get; }
    public CalendarViewModel? Calendar { get; }
    public PersonalWorkspaceViewModel? Workspace { get; }
    public bool IsBusy => Tasks?.IsBusy == true || Planning?.IsBusy == true || Calendar?.IsBusy == true || Calendar?.Recurrence?.IsBusy == true || Workspace?.IsBusy == true;
    internal IReadOnlyList<ModeSwitchEditor> InspectDrafts()
    {
        var editors = new List<ModeSwitchEditor>();
        if (Tasks is { } tasks) editors.AddRange(tasks.InspectDrafts());
        if (Planning is { } planning) editors.AddRange(planning.InspectDrafts());
        if (Workspace is { } workspace) editors.AddRange(workspace.InspectDrafts());
        if (Calendar is { } calendar)
        {
            editors.Add(new("Личное событие", () => calendar.Editor is not null, () => calendar.IsSaving, calendar.SaveEventCommand));
            if (calendar.Recurrence is { } recurrence)
            {
                var revision = recurrence.SuccessfulSaveRevision;
                editors.Add(new("Личные повторения", () => recurrence.IsOpen && recurrence.SuccessfulSaveRevision == revision,
                    () => recurrence.IsBusy, recurrence.SaveCommand, () => recurrence.SuccessfulSaveRevision > revision));
            }
        }
        return editors;
    }
    internal IReadOnlyList<PersonalNotification> ClaimPresentations() => _store?.ClaimPresentations() ?? [];
    internal void CompletePresentation(Guid id, bool accepted) => _store?.CompletePresentation(id, accepted);
    internal bool NotificationSound => _store?.WorkspaceNotificationPreferences().SoundEnabled != false;
    public ApplicationMode Mode => ApplicationMode.Personal;
    public bool IsDisposed { get; private set; }
    public CancellationToken Lifetime => _lifetime.Token;
    public IReadOnlyList<PersonalSection> Sections { get; } =
    [
        new("Сегодня", "Личные задачи, запланированные на сегодня, и задачи с наступившим сроком."),
        new("Задачи", "Все личные задачи, включая выполненные."),
        new("Входящие", "Быстрый сбор задач. Откройте задачу, чтобы добавить описание или планирование."),
        new("Настройки", "Выбран режим Personal. Используйте «Сменить режим» для перехода в пространство компании."),
    ];
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _lifetime.Cancel();
        Tasks?.Dispose();
        Calendar?.Dispose();
        Planning?.Dispose();
        Workspace?.Dispose();
        _store?.Dispose();
        _lifetime.Dispose();
    }
}

internal sealed class PersonalApplicationContext(
    global::System.Windows.Application application, string dataRoot, Action shutdown, Action switchMode, Action corporate) : IApplicationExecutionContext
{
    private global::System.Windows.Window? _window;
    private PersonalApplicationModel? _model;
    private PersonalStoreOwnership? _ownership;
    private readonly PersonalDataPaths _paths = new(dataRoot);
    private bool _disposed;
    private PersonalBackupService BackupService => new(_paths, _ownership ?? throw new PersonalStoreBusyException());
    public ApplicationMode Mode => ApplicationMode.Personal;
    public global::System.Threading.Tasks.Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try { _ownership = PersonalStoreOwnership.Acquire(_paths); Open(); ShowPersonal(); }
        catch (Exception error) { ShowRecovery(error); }
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
    private void Open(bool restoring = false) => _model = new(dataRoot, null, _ownership, restoring);
    private void CloseModel() { _model?.Dispose(); _model = null; }
    private void ShowPersonal()
    {
        var window = new PersonalWindow(_model!);
        window.SwitchModeRequested += switchMode;
        window.BackupRequested += Backup;
        window.RestoreRequested += Restore;
        Show(window);
    }
    private void ShowRecovery(Exception error)
    {
        CloseModel();
        var window = new PersonalRecoveryWindow(error.Message, _paths.DatabasePath, _ownership is not null,
            _ownership is not null && BackupService.HasPendingRestore);
        window.RestoreRequested += Restore;
        window.CorporateRequested += corporate;
        window.ExitRequested += shutdown;
        window.SafetyRequested += () =>
        {
            try { BackupService.RecoverSafetyCopy(); Open(); ShowPersonal(); }
            catch (Exception failure) { ShowRecovery(failure); }
        };
        Show(window);
    }
    private void Show(global::System.Windows.Window next)
    {
        var old = _window;
        _window = next;
        next.Closed += OnClosed;
        application.MainWindow = next;
        next.Show();
        if (old is not null) { old.Closed -= OnClosed; old.Close(); old.DataContext = null; }
    }
    private void Backup()
    {
        if (_model is null || _model.IsBusy) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Резервная копия Task|*.taskbackup", DefaultExt = ".taskbackup", FileName = $"Task-Personal-{DateTime.Now:yyyyMMdd-HHmmss}.taskbackup" };
        if (dialog.ShowDialog(_window) != true) return;
        try
        {
            BackupService.Backup(_model.Store, dialog.FileName);
            PersonalConfirmationDialog.Notice(_window!, "Резервная копия создана", "Копия записана и проверена. Файлы каталога в неё не входят.");
        }
        catch (Exception error) { ReportFailure("Резервная копия не создана. Исходная база не изменена.", error); }
    }
    private void Restore()
    {
        if (_ownership is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Резервная копия Task|*.taskbackup" };
        if (dialog.ShowDialog(_window) != true) return;
        if (_model is { } model && (model.IsBusy || model.InspectDrafts().Any(d => d.HasState())))
        {
            PersonalConfirmationDialog.Notice(_window!, "Восстановление из копии", "Перед восстановлением сохраните или отмените открытые редакторы.");
            return;
        }
        if (!PersonalConfirmationDialog.Confirm(_window!, "Восстановить личное пространство?", "Данные будут заменены выбранной копией. Перед заменой Task сохранит страховочную копию текущей базы.", "Восстановить")) return;
        try
        {
            var safety = BackupService.Restore(dialog.FileName, CloseModel, () => Open(restoring: true));
            ShowPersonal();
            PersonalConfirmationDialog.Notice(_window!, "Личное пространство восстановлено", "Данные из резервной копии готовы к работе. Страховочная копия прежней базы сохранена.", $"Страховочная копия: {safety}");
        }
        catch (Exception error)
        {
            if (_model is null) ShowRecovery(error);
            else ShowPersonal();
            ReportFailure("Восстановление не выполнено. Исходная база или её страховочная копия сохранены.", error);
        }
    }
    private void ReportFailure(string message, Exception error) => PersonalConfirmationDialog.Notice(_window!, "Не удалось выполнить действие", message, error.Message);
    public global::System.Threading.Tasks.Task<bool> PrepareSwitchAsync() => _model is null
        ? global::System.Threading.Tasks.Task.FromResult(true)
        : ModeSwitchGuard.PrepareAsync(_model.InspectDrafts(), () => !_model.IsBusy,
            (names, canSave) => ModeSwitchDialog.Choose(_window!, names, canSave));
    private void OnClosed(object? sender, EventArgs e) { if (!_disposed) shutdown(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseModel();
        _ownership?.Dispose(); _ownership = null;
        if (_window is not null) { _window.Closed -= OnClosed; _window.Close(); _window.DataContext = null; _window = null; }
    }
}
