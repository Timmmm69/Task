using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed partial class TodayViewModel
{
    private readonly IDesktopTasksApiClient? _tasksClient;
    private readonly Guid? _currentUserId;
    private IReadOnlyList<TaskItemViewModel> _overdueTasks = [];
    private IReadOnlyList<TaskItemViewModel> _reviewTasks = [];
    private IReadOnlyList<TaskItemViewModel> _waitingTasks = [];
    private string? _tasksMessage;
    private bool _todayProjectionUnsupported;

    public IReadOnlyList<TaskItemViewModel> OverdueTasks => _overdueTasks;
    public IReadOnlyList<TaskItemViewModel> ReviewTasks => _reviewTasks;
    public IReadOnlyList<TaskItemViewModel> WaitingTasks => _waitingTasks;
    public bool CanReadTasks => _capabilities.Contains("Task.Read");
    public string? TasksMessage { get => _tasksMessage; private set => SetProperty(ref _tasksMessage, value); }
    public AsyncCommand OpenItemCommand { get; }
    public event Action<object?>? OpenItemRequested;

    private void ClearTaskItems()
    {
        if (!CanReadTasks || !_sessionAvailable) _planningRetries.Clear();
        _taskSnapshots.Clear();
        _taskReloadAttempted.Clear();
        _activeTaskItems = [];
        _overdueTasks = [];
        _reviewTasks = [];
        _waitingTasks = [];
        TasksMessage = null;
        NotifyTaskItems();
        RebuildPlannerItems();
    }

    private void NotifyTaskItems()
    {
        OnPropertyChanged(nameof(OverdueTasks));
        OnPropertyChanged(nameof(ReviewTasks));
        OnPropertyChanged(nameof(WaitingTasks));
        OnPropertyChanged(nameof(HasItems));
    }

    private async global::System.Threading.Tasks.Task<bool> LoadTaskItemsAsync(
        DateOnly date, DateTimeOffset fromUtc, DateTimeOffset toUtc, long generation, CancellationToken token)
    {
        if (_tasksClient is null) return false;
        if (!CanReadTasks)
        {
            ClearTaskItems();
            TasksMessage = "Нет права Task.Read для просмотра задач.";
            return false;
        }

        if (!_currentUserId.HasValue)
        {
            ClearTaskItems();
            return false;
        }

        var tasks = new Dictionary<Guid, DesktopTaskDto>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var projection = _todayProjectionUnsupported ? null : _tasksClient as IDesktopTodayTasksApiClient;
        string? cursor = null;
        try
        {
            while (true)
            {
                var result = projection is null
                    ? await _tasksClient.GetTasksAsync(cursor, token).ConfigureAwait(true)
                    : await projection.GetTodayTasksAsync(date, fromUtc, toUtc, cursor, token).ConfigureAwait(true);
                if (!CanApplyTodayLoad(generation, token) || !CanReadTasks) return false;
                if (projection is not null && cursor is null && result is DesktopTasksApiResult<DesktopTaskPage>.NotFound)
                {
                    _todayProjectionUnsupported = true;
                    projection = null;
                    continue;
                }
                if (result is not DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
                {
                    if (result is DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure)
                    {
                        UpdateSessionState(false);
                        return false;
                    }
                    if (result is DesktopTasksApiResult<DesktopTaskPage>.Forbidden) ClearTaskItems();
                    TasksMessage = result is DesktopTasksApiResult<DesktopTaskPage>.Forbidden
                        ? "Нет права Task.Read для просмотра задач."
                        : "Не удалось обновить задачи. Повторите обновление; ранее загруженные задачи могут быть неактуальны.";
                    return false;
                }
                foreach (var task in success.Value.Items) tasks[task.Id] = task;
                cursor = success.Value.NextCursor;
                if (string.IsNullOrEmpty(cursor)) break;
                if (!cursors.Add(cursor))
                {
                    TasksMessage = "Сервер повторил страницу задач. Повторите обновление.";
                    return false;
                }
            }

            if (projection is not null)
            {
                var missing = _unconfirmedCommands.Keys.Concat(_planningRetries.Keys).Concat(_tasksNeedingReload)
                    .Distinct().Where(id => !tasks.ContainsKey(id)).ToArray();
                foreach (var id in missing)
                {
                    var result = await _tasksClient.GetTaskByIdAsync(id, token).ConfigureAwait(true);
                    if (!CanApplyTodayLoad(generation, token) || !CanReadTasks) return false;
                    if (result is DesktopTasksApiResult<DesktopTaskDto>.AuthenticationFailure)
                    {
                        UpdateSessionState(false);
                        return false;
                    }
                    if (result is DesktopTasksApiResult<DesktopTaskDto>.Forbidden)
                    {
                        ClearTaskItems();
                        TasksMessage = "Нет права Task.Read для просмотра задач.";
                        return false;
                    }
                    if (result is DesktopTasksApiResult<DesktopTaskDto>.Succeeded { Value: var task } && task.Id == id)
                        tasks[id] = task;
                }
            }

            _taskSnapshots = tasks;
            foreach (var task in tasks.Values)
                if (_planningRetries.TryGetValue(task.Id, out var retry) && task.Version > retry.Command.ExpectedVersion)
                    _planningRetries.Remove(task.Id);
            _tasksNeedingReload.ExceptWith(tasks.Keys);
            foreach (var task in tasks.Values)
                if (_unconfirmedCommands.TryGetValue(task.Id, out var command) && task.Version > command.ExpectedVersion)
                    _unconfirmedCommands.Remove(task.Id);
            RebuildTaskSummaries();
            TasksMessage = null;
            RebuildPlannerItems();
            return projection is not null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_active && generation == _generation && CanReadTasks)
                TasksMessage = "Сервер задач недоступен. Повторите обновление.";
        }
        return false;
    }

    private void RebuildTaskSummaries()
    {
        var currentUserId = _currentUserId.GetValueOrDefault();
        var active = _taskSnapshots.Values.Where(t => t.Status is not DesktopTaskStatus.Completed and not DesktopTaskStatus.Cancelled)
            .Where(t => _currentUserId.HasValue)
            .Where(t => t.AuthorUserId == currentUserId || t.AssigneeIds.Contains(currentUserId) || t.WatcherIds.Contains(currentUserId))
            .OrderBy(t => t.DeadlineAtUtc ?? DateTimeOffset.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCulture)
            .Select(t => new TaskItemViewModel(t)).ToArray();
        _activeTaskItems = active;
        _overdueTasks = active.Where(t => Task.Domain.TaskRules.IsOverdue(t.Source.DeadlineAtUtc, t.Source.Card, _clock())).ToArray();
        _reviewTasks = active.Where(t => t.Source.Status == DesktopTaskStatus.Review).ToArray();
        _waitingTasks = active.Where(t => t.Source.Status != DesktopTaskStatus.Review
            && !t.Source.AssigneeIds.Contains(currentUserId)
            && t.Source.AssigneeIds.Count > 0).ToArray();
        NotifyTaskItems();
    }
}
