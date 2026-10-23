using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed partial class TodayViewModel
{
    private Dictionary<Guid, DesktopTaskDto> _taskSnapshots = [];
    // Retain only command identities for an explicit retry after an ambiguous outcome.
    // Nothing is queued or sent automatically, including after reconnect.
    private readonly Dictionary<Guid, DesktopTransitionTaskCommand> _unconfirmedCommands = [];
    private readonly HashSet<Guid> _tasksNeedingReload = [];
    private readonly HashSet<Guid> _taskReloadAttempted = [];
    private bool _isActionBusy;
    private readonly HashSet<Guid> _statusPermissionDeniedTasks = [];
    private long _actionEpoch;
    private DesktopTransitionTaskCommand? _confirmation;
    private string? _actionMessage;

    private bool SelectedIsTask => SelectedItem is { Source: TaskItemViewModel }
        or { Source: CalendarItemViewModel { IsCalendarEvent: false } };
    private DesktopTaskDto? SelectedTask => SelectedIsTask && SelectedItem is { } item
        ? _taskSnapshots.GetValueOrDefault(item.Id) : null;

    public bool ShowTaskActions => SelectedIsTask && CanReadTasks && _tasksClient is not null;
    internal bool HasUnconfirmedMutation => _unconfirmedCommands.Count > 0 || _planningRetries.Count > 0;

    public bool IsActionBusy => _isActionBusy;
    public bool NeedsTaskReload => ShowTaskActions && (SelectedTask is null
        || _tasksNeedingReload.Contains(SelectedItem!.Id));
    public bool CanStart => SelectedTask?.Status == DesktopTaskStatus.New && CanShowTransition(DesktopTaskStatus.InProgress);
    public bool CanReturnForRework => SelectedTask?.Status == DesktopTaskStatus.Review && CanShowTransition(DesktopTaskStatus.InProgress);
    public bool CanSubmitForReview => CanShowTransition(DesktopTaskStatus.Review);
    public bool CanComplete => CanShowTransition(DesktopTaskStatus.Completed);
    public bool CanCancel => CanShowTransition(DesktopTaskStatus.Cancelled);
    public bool IsTransitionConfirmationVisible => _confirmation?.Id == SelectedItem?.Id && _confirmation is not null;
    private bool IsReturnForReworkConfirmation => _confirmation?.TargetStatus == DesktopTaskStatus.InProgress
        && SelectedTask?.Status == DesktopTaskStatus.Review;
    public string TransitionConfirmationLabel => IsReturnForReworkConfirmation ? "Вернуть" : "Подтвердить";
    public string PendingTransitionText => IsReturnForReworkConfirmation ? "Вернуть задачу на доработку?"
        : _confirmation?.TargetStatus == DesktopTaskStatus.Completed
        ? "Завершить задачу? Действие нельзя отменить."
        : "Отменить задачу? Действие нельзя отменить.";
    public string? ActionMessage { get => _actionMessage; private set => SetProperty(ref _actionMessage, value); }
    public AsyncCommand TransitionCommand { get; private set; } = null!;
    public AsyncCommand ConfirmTransitionCommand { get; private set; } = null!;
    public AsyncCommand CancelTransitionCommand { get; private set; } = null!;
    public AsyncCommand ReloadSelectedTaskCommand { get; private set; } = null!;

    private bool CanWriteStatus => !_disposed && IsActive && _sessionAvailable && IsConnected
        && CanReadTasks && _capabilities.Contains("Task.ChangeStatus")
        && SelectedTask is { } task && !_statusPermissionDeniedTasks.Contains(task.Id)
        && !IsBusy && !IsActionBusy && !NeedsTaskReload && !HasPlanningRetry;

    private bool CanShowTransition(DesktopTaskStatus target) => ShowTaskActions
        && _capabilities.Contains("Task.ChangeStatus") && SelectedTask is { } task && !_statusPermissionDeniedTasks.Contains(task.Id)
        && TasksViewModel.IsAllowedTransition(task.Status, target)
        && (!_unconfirmedCommands.TryGetValue(task.Id, out var pending) || pending.TargetStatus == target);

    private bool CanOfferTransition(DesktopTaskStatus target) => CanWriteStatus
        && _confirmation is null && CanShowTransition(target);

    private void InitializeQuickActions()
    {
        TransitionCommand = new AsyncCommand(async (parameter, token) =>
        {
            if (!Enum.TryParse<DesktopTaskStatus>(parameter?.ToString(), out var target)
                || !CanOfferTransition(target) || SelectedTask is not { } task) return;
            var command = _unconfirmedCommands.GetValueOrDefault(task.Id)
                ?? new DesktopTransitionTaskCommand(task.Id, task.Version, target);
            ActionMessage = null;
            if (target is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled
                || task.Status == DesktopTaskStatus.Review && target == DesktopTaskStatus.InProgress)
            {
                _confirmation = command;
                NotifyQuickActions();
            }
            else await ExecuteTransitionAsync(command, token).ConfigureAwait(true);
        }, parameter => Enum.TryParse<DesktopTaskStatus>(parameter?.ToString(), out var target) && CanOfferTransition(target));
        ConfirmTransitionCommand = new AsyncCommand(async (_, token) =>
        {
            if (_confirmation is { } command) await ExecuteTransitionAsync(command, token).ConfigureAwait(true);
        }, _ => CanWriteStatus && IsTransitionConfirmationVisible);
        CancelTransitionCommand = new AsyncCommand((_, _) =>
        {
            ClearActionConfirmation();
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, _ => IsTransitionConfirmationVisible && !IsActionBusy);
        ReloadSelectedTaskCommand = new AsyncCommand((_, token) => ReloadSelectedTaskAsync(token),
            _ => ShowTaskActions && IsActive && _sessionAvailable && IsConnected && !IsBusy && !IsActionBusy);
    }

    private void SetActionBusy(bool busy)
    {
        _isActionBusy = busy;
        OnPropertyChanged(nameof(IsActionBusy));
        RefreshCommand?.RaiseCanExecuteChanged();
        NotifyQuickActions();
        if (!busy && NeedsTaskReload && !_taskReloadAttempted.Contains(SelectedItem!.Id))
            _ = ReloadSelectedTaskAsync(CancellationToken.None);
    }

    private void ClearActionConfirmation()
    {
        _confirmation = null;
        NotifyQuickActions();
    }

    private void NotifyQuickActions()
    {
        NotifyPlanning();
        foreach (var property in new[] { nameof(ShowTaskActions), nameof(NeedsTaskReload), nameof(CanStart), nameof(CanReturnForRework),
            nameof(CanSubmitForReview), nameof(CanComplete), nameof(CanCancel),
            nameof(IsTransitionConfirmationVisible), nameof(PendingTransitionText), nameof(TransitionConfirmationLabel) }) OnPropertyChanged(property);
        TransitionCommand?.RaiseCanExecuteChanged();
        ConfirmTransitionCommand?.RaiseCanExecuteChanged();
        CancelTransitionCommand?.RaiseCanExecuteChanged();
        ReloadSelectedTaskCommand?.RaiseCanExecuteChanged();
    }

    private void StopQuickActions()
    {
        StopPlanning();
        _actionEpoch++;
        TransitionCommand.Cancel();
        ConfirmTransitionCommand.Cancel();
        ReloadSelectedTaskCommand.Cancel();
        ClearActionConfirmation();
        ActionMessage = null;
        if (!_sessionAvailable) { _unconfirmedCommands.Clear(); _tasksNeedingReload.Clear(); _statusPermissionDeniedTasks.Clear(); }
    }

    private async global::System.Threading.Tasks.Task ExecuteTransitionAsync(
        DesktopTransitionTaskCommand command, CancellationToken token)
    {
        if (!CanWriteStatus || SelectedTask is not { } task || task.Id != command.Id
            || !TasksViewModel.IsAllowedTransition(task.Status, command.TargetStatus)) return;
        var epoch = _actionEpoch;
        _unconfirmedCommands[command.Id] = command;
        SetActionBusy(true);
        try
        {
            var result = await _tasksClient!.TransitionTaskAsync(command, token).ConfigureAwait(true);
            if (!CanApplyActionResponse(epoch)) return;
            switch (result)
            {
                case DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success
                    when success.Value.Id == command.Id && success.Version == success.Value.Version
                        && success.Version > command.ExpectedVersion && success.Value.Status == command.TargetStatus:
                    _unconfirmedCommands.Remove(command.Id);
                    ApplyConfirmedTask(success.Value);
                    ActionMessage = $"Статус задачи «{task.Title}»: {TaskItemViewModel.LocalizeStatus(success.Value.Status)}.";
                    ClearActionConfirmation();
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure:
                    UpdateSessionState(false);
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.Forbidden:
                    _statusPermissionDeniedTasks.Add(command.Id);
                    _unconfirmedCommands.Remove(command.Id);
                    ClearActionConfirmation();
                    ActionMessage = "Нет права Task.ChangeStatus. Доступен только просмотр.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.NotFound:
                    RemoveUnavailableTask(command.Id);
                    _unconfirmedCommands.Remove(command.Id);
                    ClearActionConfirmation();
                    ActionMessage = "Задача больше недоступна.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict:
                case DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition:
                    _unconfirmedCommands.Remove(command.Id);
                    _tasksNeedingReload.Add(command.Id);
                    ClearActionConfirmation();
                    await ReloadTaskAsync(command.Id, epoch, token).ConfigureAwait(true);
                    if (CanApplyActionResponse(epoch))
                        ActionMessage = "Задача изменена другим пользователем или переход недопустим. Проверьте актуальные данные и повторите действие.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress:
                    ActionMessage = "Сервер ещё обрабатывает запрос. Повторите действие позже; ключ запроса сохранён.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict:
                    _unconfirmedCommands.Remove(command.Id);
                    _tasksNeedingReload.Add(command.Id);
                    ClearActionConfirmation();
                    ActionMessage = "Ключ запроса уже использован. Обновите задачу и подтвердите новое действие.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation:
                    _unconfirmedCommands.Remove(command.Id);
                    ClearActionConfirmation();
                    ActionMessage = validation.Message;
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired:
                    _unconfirmedCommands.Remove(command.Id);
                    _tasksNeedingReload.Add(command.Id);
                    ClearActionConfirmation();
                    ActionMessage = "Обновите версию задачи перед изменением статуса.";
                    break;
                default:
                    ActionMessage = "Смена статуса не подтверждена. Обновите задачу или повторите действие с тем же ключом.";
                    if (result is DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable) UpdateConnectivity(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (CanApplyActionResponse(epoch)) ActionMessage = "Запрос отменён. Результат не подтверждён; безопасное повторение сохраняет ключ.";
        }
        catch (Exception)
        {
            if (CanApplyActionResponse(epoch))
            {
                ActionMessage = "Сервер недоступен. Смена статуса не подтверждена.";
                UpdateConnectivity(false);
            }
        }
        finally { SetActionBusy(false); }
    }

    private bool CanApplyActionResponse(long epoch) => epoch == _actionEpoch && !_disposed
        && IsActive && _sessionAvailable && CanReadTasks;

    private async global::System.Threading.Tasks.Task ReloadSelectedTaskAsync(CancellationToken token)
    {
        if (!ShowTaskActions || !IsActive || !_sessionAvailable || !IsConnected || IsActionBusy) return;
        var id = SelectedItem!.Id;
        _taskReloadAttempted.Add(id);
        var epoch = _actionEpoch;
        ClearActionConfirmation();
        SetActionBusy(true);
        try { await ReloadTaskAsync(id, epoch, token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (CanApplyActionResponse(epoch)) ActionMessage = "Не удалось обновить задачу. Повторите обновление.";
        }
        finally { SetActionBusy(false); }
    }

    private async global::System.Threading.Tasks.Task ReloadTaskAsync(Guid id, long epoch, CancellationToken token)
    {
        var result = await _tasksClient!.GetTaskByIdAsync(id, token).ConfigureAwait(true);
        if (!CanApplyActionResponse(epoch) || token.IsCancellationRequested) return;
        switch (result)
        {
            case DesktopTasksApiResult<DesktopTaskDto>.Succeeded success when success.Value.Id == id:
                _tasksNeedingReload.Remove(id);
                ApplyConfirmedTask(success.Value);
                break;
            case DesktopTasksApiResult<DesktopTaskDto>.AuthenticationFailure:
                UpdateSessionState(false);
                break;
            case DesktopTasksApiResult<DesktopTaskDto>.Forbidden:
            case DesktopTasksApiResult<DesktopTaskDto>.NotFound:
                RemoveUnavailableTask(id);
                ActionMessage = "Задача недоступна для просмотра.";
                break;
            default:
                _tasksNeedingReload.Add(id);
                ActionMessage = "Не удалось загрузить актуальную задачу. Повторите обновление.";
                break;
        }
    }

    private void ApplyConfirmedTask(DesktopTaskDto task)
    {
        if (_taskSnapshots.TryGetValue(task.Id, out var current) && current.Version > task.Version) return;
        _taskSnapshots[task.Id] = task;
        if (_planningRetries.TryGetValue(task.Id, out var planning) && task.Version > planning.Command.ExpectedVersion)
            _planningRetries.Remove(task.Id);
        if (_unconfirmedCommands.TryGetValue(task.Id, out var pending) && task.Version > pending.ExpectedVersion)
            _unconfirmedCommands.Remove(task.Id);
        if (_planningProjectionIds.Contains(task.Id) || task.Card?.ScheduledDate is not null
            || current is not null && (current.StartAtUtc != task.StartAtUtc || current.Card != task.Card))
            UpdateTaskPlanningProjection(task);
        else if (task.Status is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled)
            RemoveTaskCalendarProjection(task.Id);
        else
        {
            CalendarItemViewModel Update(CalendarItemViewModel item) => item.Id != task.Id || item.IsCalendarEvent ? item
                : new CalendarItemViewModel(item.Source with
                {
                    Title = task.Title,
                    Status = task.Status switch
                    {
                        DesktopTaskStatus.InProgress => "in_progress",
                        DesktopTaskStatus.Review => "review",
                        _ => "new",
                    },
                }, null, _timeZone);
            TimedItems = TimedItems.Select(Update).ToArray();
            UntimedItems = UntimedItems.Select(Update).ToArray();
        }
        RebuildTaskSummaries();
        RebuildPlannerItems();
        SyncPlanning();
        State = HasItems ? TodayScreenState.Loaded : TodayScreenState.Empty;
        NotifyQuickActions();
    }

    private void RemoveTaskCalendarProjection(Guid id)
    {
        TimedItems = TimedItems.Where(item => item.Id != id || item.IsCalendarEvent).ToArray();
        UntimedItems = UntimedItems.Where(item => item.Id != id || item.IsCalendarEvent).ToArray();
    }

    private void RemoveUnavailableTask(Guid id)
    {
        _taskSnapshots.Remove(id);
        _tasksNeedingReload.Remove(id);
        _planningRetries.Remove(id);
        _planningDenied.Remove(id);
        _statusPermissionDeniedTasks.Remove(id);
        RemoveTaskCalendarProjection(id);
        RebuildTaskSummaries();
        RebuildPlannerItems();
        State = HasItems ? TodayScreenState.Loaded : TodayScreenState.Empty;
    }
}
