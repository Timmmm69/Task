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

    public IReadOnlyList<TaskItemViewModel> OverdueTasks => _overdueTasks;
    public IReadOnlyList<TaskItemViewModel> ReviewTasks => _reviewTasks;
    public IReadOnlyList<TaskItemViewModel> WaitingTasks => _waitingTasks;
    public bool CanReadTasks => _capabilities.Contains("Task.Read");
    public string? TasksMessage { get => _tasksMessage; private set => SetProperty(ref _tasksMessage, value); }
    public AsyncCommand OpenItemCommand { get; }
    public event Action<object?>? OpenItemRequested;

    private void ClearTaskItems()
    {
        _overdueTasks = [];
        _reviewTasks = [];
        _waitingTasks = [];
        TasksMessage = null;
        NotifyTaskItems();
    }

    private void NotifyTaskItems()
    {
        OnPropertyChanged(nameof(OverdueTasks));
        OnPropertyChanged(nameof(ReviewTasks));
        OnPropertyChanged(nameof(WaitingTasks));
        OnPropertyChanged(nameof(HasItems));
    }

    private async global::System.Threading.Tasks.Task LoadTaskItemsAsync(long generation, CancellationToken token)
    {
        if (_tasksClient is null) return;
        if (!CanReadTasks)
        {
            ClearTaskItems();
            TasksMessage = "Нет права Task.Read для просмотра задач.";
            return;
        }

        var tasks = new Dictionary<Guid, DesktopTaskDto>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        try
        {
            do
            {
                var result = await _tasksClient.GetTasksAsync(cursor, token).ConfigureAwait(true);
                if (!_active || !_sessionAvailable || !CanReadTasks || token.IsCancellationRequested || generation != _generation) return;
                if (result is not DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
                {
                    if (result is DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure)
                    {
                        UpdateSessionState(false);
                        return;
                    }
                    if (result is DesktopTasksApiResult<DesktopTaskPage>.Forbidden) ClearTaskItems();
                    TasksMessage = result is DesktopTasksApiResult<DesktopTaskPage>.Forbidden
                        ? "Нет права Task.Read для просмотра задач."
                        : "Не удалось обновить задачи. Повторите обновление; ранее загруженные задачи могут быть неактуальны.";
                    return;
                }
                foreach (var task in success.Value.Items) tasks[task.Id] = task;
                cursor = success.Value.NextCursor;
                if (!string.IsNullOrEmpty(cursor) && !cursors.Add(cursor))
                {
                    TasksMessage = "Сервер повторил страницу задач. Повторите обновление.";
                    return;
                }
            } while (!string.IsNullOrEmpty(cursor));

            var active = tasks.Values.Where(t => t.Status is not DesktopTaskStatus.Completed and not DesktopTaskStatus.Cancelled)
                .Where(t => _currentUserId.HasValue && (t.AuthorUserId == _currentUserId || t.AssigneeIds.Contains(_currentUserId.Value) || t.WatcherIds.Contains(_currentUserId.Value)))
                .OrderBy(t => t.DeadlineAtUtc ?? DateTimeOffset.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCulture)
                .Select(t => new TaskItemViewModel(t)).ToArray();
            _overdueTasks = active.Where(t => t.Source.DeadlineAtUtc < _clock()).ToArray();
            _reviewTasks = active.Where(t => t.Source.Status == DesktopTaskStatus.Review).ToArray();
            _waitingTasks = active.Where(t => t.Source.Status != DesktopTaskStatus.Review
                && !t.Source.AssigneeIds.Contains(_currentUserId!.Value)
                && t.Source.AssigneeIds.Count > 0).ToArray();
            TasksMessage = null;
            NotifyTaskItems();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_active && generation == _generation && CanReadTasks)
                TasksMessage = "Сервер задач недоступен. Повторите обновление.";
        }
    }
}
