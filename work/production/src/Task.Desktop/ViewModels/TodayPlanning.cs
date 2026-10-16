using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed partial class TodayViewModel
{
    private TodayPlanningEditor? _planning;
    private readonly Dictionary<Guid, (DesktopPatchTaskCommand Command, TodayPlanningEditor Draft)> _planningRetries = [];
    private readonly HashSet<Guid> _planningDenied = [];
    private readonly HashSet<Guid> _planningProjectionIds = [];
    private long _planningEpoch;
    private string? _planningMessage;

    public TodayPlanningEditor? Planning => _planning;
    public bool ShowPlanning => ShowTaskActions;
    public string? PlanningMessage { get => _planningMessage; private set => SetProperty(ref _planningMessage, value); }
    private bool HasPlanningRetry => SelectedTask is { } task && _planningRetries.ContainsKey(task.Id);
    private bool CanWritePlanning => ShowPlanning && SelectedTask is { } task && IsActive && _sessionAvailable
        && IsConnected && !IsBusy && !IsActionBusy && !NeedsTaskReload
        && _capabilities.Contains("Task.Update") && !_planningDenied.Contains(task.Id)
        && !_unconfirmedCommands.ContainsKey(task.Id) && _confirmation is null;
    public bool CanEditPlanning => CanWritePlanning && !HasPlanningRetry;
    public bool ShowPlanningReload => ShowPlanning && (NeedsTaskReload || HasPlanningRetry);
    public string PlanningAccessText => !_capabilities.Contains("Task.Update") || SelectedTask is { } task && _planningDenied.Contains(task.Id)
        ? "Только просмотр: изменение требует Task.Update и доступа к этой задаче."
        : !IsConnected ? "Только просмотр: нет подключения."
        : HasPlanningRetry ? "Результат запроса не подтверждён. Повторите сохранение с тем же ключом или загрузите актуальные данные."
        : NeedsTaskReload ? "Загрузите актуальную задачу перед изменением."
        : "Срок выполнения не меняется. Длительность: 1–10080 минут, пустое поле — не задана.";
    public AsyncCommand SavePlanningCommand { get; private set; } = null!;
    public AsyncCommand CancelPlanningCommand { get; private set; } = null!;

    private void InitializePlanning()
    {
        SavePlanningCommand = new AsyncCommand((_, token) => SavePlanningAsync(token),
            _ => CanWritePlanning && Planning is { } draft && draft.Error is null && draft.HasChanges);
        CancelPlanningCommand = new AsyncCommand((_, _) =>
        {
            SetPlanning(SelectedTask is { } task ? new(task, _timeZone) : null);
            PlanningMessage = null;
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, _ => Planning?.HasChanges == true && !IsActionBusy && !HasPlanningRetry);
    }

    private void SetPlanning(TodayPlanningEditor? draft)
    {
        if (_planning is not null) _planning.Changed -= NotifyPlanning;
        _planning = draft;
        if (_planning is not null) _planning.Changed += NotifyPlanning;
        OnPropertyChanged(nameof(Planning));
        NotifyPlanning();
    }

    private void SyncPlanning()
    {
        var task = SelectedTask;
        if (_planning?.TaskId == task?.Id && _planning?.Version == task?.Version) return;
        PlanningMessage = null;
        SetPlanning(task is null ? null : _planningRetries.TryGetValue(task.Id, out var pending)
            && pending.Command.ExpectedVersion == task.Version ? pending.Draft : new(task, _timeZone));
    }

    private void NotifyPlanning()
    {
        foreach (var property in new[] { nameof(ShowPlanning), nameof(CanEditPlanning), nameof(ShowPlanningReload), nameof(PlanningAccessText) })
            OnPropertyChanged(property);
        SavePlanningCommand?.RaiseCanExecuteChanged();
        CancelPlanningCommand?.RaiseCanExecuteChanged();
    }

    private void StopPlanning()
    {
        _planningEpoch++;
        SavePlanningCommand?.Cancel();
        PlanningMessage = null;
        if (!_sessionAvailable) { _planningRetries.Clear(); _planningDenied.Clear(); }
    }

    private async global::System.Threading.Tasks.Task SavePlanningAsync(CancellationToken token)
    {
        if (!CanWritePlanning || Planning is not { } draft || SelectedTask is not { } task || !draft.Validate()) return;
        var command = _planningRetries.TryGetValue(task.Id, out var retry) ? retry.Command : draft.BuildPatch();
        if (command is null) return;
        var epoch = _actionEpoch;
        var planningEpoch = _planningEpoch;
        bool CanApply() => CanApplyActionResponse(epoch) && planningEpoch == _planningEpoch;
        _planningRetries[task.Id] = (command, draft);
        ClearActionConfirmation();
        PlanningMessage = null;
        SetActionBusy(true);
        try
        {
            var result = await _tasksClient!.PatchTaskAsync(command, token).ConfigureAwait(true);
            if (!CanApply()) return;
            void Message(string text) { if (SelectedTask?.Id == task.Id) PlanningMessage = text; }
            switch (result)
            {
                case DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success
                    when success.Value.Id == task.Id && success.Version == success.Value.Version && success.Version > command.ExpectedVersion:
                    _planningRetries.Remove(task.Id);
                    _planningProjectionIds.Add(task.Id);
                    ApplyConfirmedTask(success.Value);
                    ActionMessage = $"Планирование задачи «{task.Title}» сохранено.";
                    Message("Планирование сохранено.");
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure:
                    UpdateSessionState(false);
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.Forbidden:
                    _planningRetries.Remove(task.Id);
                    _planningDenied.Add(task.Id);
                    Message("Нет права Task.Update или доступа к изменению этой задачи. Доступен только просмотр.");
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.NotFound:
                    _planningRetries.Remove(task.Id);
                    RemoveUnavailableTask(task.Id);
                    ActionMessage = "Задача больше недоступна.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict:
                case DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict:
                case DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired:
                    _planningRetries.Remove(task.Id);
                    _tasksNeedingReload.Add(task.Id);
                    // Do not silently rebase an edited draft after a conflict.
                    _taskReloadAttempted.Add(task.Id);
                    Message("Задача или ключ запроса изменены. Загрузите актуальную версию и повторите изменение вручную.");
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation:
                    _planningRetries.Remove(task.Id);
                    Message(validation.Message);
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress:
                    Message("Сервер ещё обрабатывает запрос. Повторите сохранение позже; ключ сохранён.");
                    break;
                default:
                    Message("Сохранение не подтверждено. Повторите запрос с тем же ключом или обновите задачу.");
                    if (result is DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable) UpdateConnectivity(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (CanApply() && SelectedTask?.Id == task.Id)
                PlanningMessage = "Запрос отменён. Результат не подтверждён; ключ сохранён для безопасного повторения.";
        }
        catch (Exception)
        {
            if (CanApply())
            {
                if (SelectedTask?.Id == task.Id) PlanningMessage = "Сервер недоступен. Сохранение не подтверждено.";
                UpdateConnectivity(false);
            }
        }
        finally { SetActionBusy(false); }
    }

    // A confirmed Task DTO owns planning. Never manufacture a deadline to obtain a calendar row.
    private void UpdateTaskPlanningProjection(DesktopTaskDto task)
    {
        RemoveTaskCalendarProjection(task.Id);
        if (task.Status is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled) return;
        var start = task.StartAtUtc;
        var date = start is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, _timeZone).DateTime) : task.Card?.ScheduledDate;
        if (date is null) return;
        var end = start is { } s && task.Card?.PlannedDurationMinutes is { } duration ? s.AddMinutes(duration) : (DateTimeOffset?)null;
        var item = new CalendarItemViewModel(new DesktopScheduleItem(task.Id, DesktopScheduleItemType.Task, task.Title,
            date.Value, start, end, start is null, task.Card?.ProjectId, task.Status switch
            { DesktopTaskStatus.InProgress => "in_progress", DesktopTaskStatus.Review => "review", _ => "new" },
            (DesktopCalendarPriority)task.Priority, task.RecurrenceSeriesId, task.Card?.Description), null, _timeZone);
        if (!item.AppearsOn(_today, _timeZone)) return;
        if (start.HasValue) TimedItems = TimedItems.Append(item).OrderBy(i => i.Source.StartAtUtc).ToArray();
        else UntimedItems = UntimedItems.Append(item).ToArray();
    }
}
