using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum ProjectTasksState { Idle, Loading, Partial, Complete, Empty, Forbidden, Offline, ServerUnavailable, Cancelled, Failed, SessionEnded }

public sealed partial class ProjectsViewModel
{
    private CancellationTokenSource? _relatedRequest;
    private long _relatedGeneration;
    private string? _relatedCursor;
    private readonly HashSet<string> _relatedCursors = new(StringComparer.Ordinal);
    private bool _restartRelated;
    private ProjectTasksState _relatedState;
    public ProjectTasksState RelatedTasksState
    {
        get => _relatedState;
        private set
        {
            if (!SetProperty(ref _relatedState, value)) return;
            NotifyRelatedTasks();
        }
    }
    public bool IsRelatedTasksLoading => RelatedTasksState == ProjectTasksState.Loading;
    public string RelatedTasksActionText => RelatedTasksState == ProjectTasksState.Idle ? "Загрузить задачи"
        : _restartRelated ? "Начать поиск заново"
        : RelatedTasksState is ProjectTasksState.ServerUnavailable or ProjectTasksState.Failed ? "Повторить поиск"
        : "Продолжить поиск";
    public string RelatedTasksStatusText => RelatedTasksState switch
    {
        ProjectTasksState.Loading => $"Поиск задач… Найдено пока {RelatedTasks.Count}. Список ещё не полный.",
        ProjectTasksState.Partial => $"Найдено пока {RelatedTasks.Count} задач. Для полного списка продолжите поиск.",
        ProjectTasksState.Complete => $"Все доступные задачи проекта загружены: {RelatedTasks.Count}.",
        ProjectTasksState.Empty => "В проекте нет доступных задач.",
        ProjectTasksState.Forbidden => "Нет доступа к связанным задачам (Task.Read / Project.Read).",
        ProjectTasksState.SessionEnded => "Сессия завершена. Выполните вход снова.",
        ProjectTasksState.Offline => $"Нет соединения. Найдено пока {RelatedTasks.Count}; полнота списка не подтверждена.",
        ProjectTasksState.ServerUnavailable => $"Сервер недоступен. Найдено пока {RelatedTasks.Count}; повторите поиск.",
        ProjectTasksState.Cancelled => $"Поиск остановлен. Найдено пока {RelatedTasks.Count}; можно продолжить.",
        ProjectTasksState.Failed => $"Не удалось завершить поиск. Найдено пока {RelatedTasks.Count}; полнота списка не подтверждена.",
        _ => "Загрузите связанные задачи при необходимости. Поиск выполняется по страницам доступных задач."
    };
    public AsyncCommand LoadRelatedTasksCommand { get; private set; } = null!;
    private bool CanLoadRelatedTasks => !_disposed && IsActive && _sessionAvailable && _networkAvailable
        && CanRead && _capabilities.Contains("Task.Read") && _tasks is not null
        && SelectedItem is not null && _projectConfirmed && !IsDetailLoading
        && RelatedTasksState is not (ProjectTasksState.Complete or ProjectTasksState.Empty or ProjectTasksState.Forbidden or ProjectTasksState.Loading);

    private void NotifyRelatedTasks()
    {
        OnPropertyChanged(nameof(IsRelatedTasksLoading));
        OnPropertyChanged(nameof(RelatedTasksStatusText));
        OnPropertyChanged(nameof(RelatedTasksActionText));
        LoadRelatedTasksCommand?.RaiseCanExecuteChanged();
    }

    public void CancelRelatedTasks()
    {
        _relatedGeneration++;
        _relatedRequest?.Cancel();
        if (IsRelatedTasksLoading) RelatedTasksState = ProjectTasksState.Cancelled;
    }

    private void ResetRelatedTasks()
    {
        CancelRelatedTasks();
        RelatedTasks = []; _relatedCursor = null; _relatedCursors.Clear(); _restartRelated = false;
        RelatedTasksState = !_sessionAvailable ? ProjectTasksState.SessionEnded
            : !CanRead || !_capabilities.Contains("Task.Read") || _tasks is null ? ProjectTasksState.Forbidden
            : !_networkAvailable ? ProjectTasksState.Offline : ProjectTasksState.Idle;
        NotifyRelatedTasks();
    }

    private async System.Threading.Tasks.Task LoadRelatedTasksPageAsync(object? _, CancellationToken cancellationToken)
    {
        if (!CanLoadRelatedTasks) return;
        if (_restartRelated) ResetRelatedTasks();
        var item = SelectedItem!;
        var generation = ++_relatedGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activation!.Token);
        _relatedRequest = request;
        bool Current() => !request.IsCancellationRequested && generation == _relatedGeneration
            && ReferenceEquals(SelectedItem, item) && IsActive && _sessionAvailable && _networkAvailable
            && CanRead && _capabilities.Contains("Task.Read") && !_disposed;
        RelatedTasksState = ProjectTasksState.Loading;
        try
        {
            var page = await _tasks!.GetTasksAsync(_relatedCursor, request.Token).ConfigureAwait(true);
            if (!Current()) return;
            switch (page)
            {
                case DesktopTasksApiResult<DesktopTaskPage>.Succeeded success:
                    var next = success.Value.NextCursor;
                    // Keep the request cursor unchanged on failure so an explicit retry repeats only that page.
                    if (!string.IsNullOrWhiteSpace(next) && (_relatedCursors.Contains(next) || next == _relatedCursor))
                    { _restartRelated = true; RelatedTasksState = ProjectTasksState.Failed; return; }
                    RelatedTasks = RelatedTasks.Concat(success.Value.Items.Where(task => task.Card?.ProjectId == item.Id)
                        .Select(task => new TaskItemViewModel(task))).DistinctBy(task => task.Id).ToArray();
                    if (_relatedCursor is not null) _relatedCursors.Add(_relatedCursor);
                    _relatedCursor = next;
                    RelatedTasksState = string.IsNullOrWhiteSpace(next)
                        ? RelatedTasks.Count == 0 ? ProjectTasksState.Empty : ProjectTasksState.Complete
                        : ProjectTasksState.Partial;
                    break;
                case DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure:
                    UpdateSessionState(false); break;
                case DesktopTasksApiResult<DesktopTaskPage>.Forbidden:
                case DesktopTasksApiResult<DesktopTaskPage>.NotFound:
                    ResetRelatedTasks(); RelatedTasksState = ProjectTasksState.Forbidden; break;
                case DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable:
                    RelatedTasksState = ProjectTasksState.ServerUnavailable; break;
                case DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor:
                    _restartRelated = true; RelatedTasksState = ProjectTasksState.Failed; break;
                default: RelatedTasksState = ProjectTasksState.Failed; break;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { if (generation == _relatedGeneration) RelatedTasksState = ProjectTasksState.Cancelled; }
        catch (Exception)
        { if (Current()) RelatedTasksState = ProjectTasksState.Failed; }
        finally
        {
            if (ReferenceEquals(_relatedRequest, request)) _relatedRequest = null;
            // A transport may ignore cancellation and return normally; never leave a loading spinner behind.
            if (generation == _relatedGeneration && IsRelatedTasksLoading) RelatedTasksState = ProjectTasksState.Cancelled;
        }
    }
}
