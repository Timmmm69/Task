using System.Globalization;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum TasksScreenState
{
    Inactive,
    InitialLoading,
    Loaded,
    Empty,
    Refreshing,
    LoadingNextPage,
    Error,
    Forbidden,
    InvalidCursor,
    SessionEnded,
    Cancelled,
}

public enum TaskDetailsState
{
    None,
    Loading,
    Loaded,
    NotFound,
    Error,
}

public enum TaskVisualTone
{
    Neutral,
    Brand,
    Success,
    Warning,
    Critical,
}

/// <summary>Localized, display-only projection of a validated Task API DTO.</summary>
public sealed class TaskItemViewModel
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public TaskItemViewModel(DesktopTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);
        Source = task;
        StatusText = LocalizeStatus(task.Status);
        PriorityText = LocalizePriority(task.Priority);
        DeadlineText = FormatDate(task.DeadlineAtUtc, "Без срока");
        UpdatedText = FormatDate(task.UpdatedAtUtc, "Не указано");
        StatusIconKey = StatusIcon(task.Status);
        StatusTone = StatusVisualTone(task.Status);
        PriorityIconKey = PriorityIcon(task.Priority);
        PriorityTone = PriorityVisualTone(task.Priority);
        AutomationName = $"{task.Title}. Статус: {StatusText}. Приоритет: {PriorityText}. Срок: {DeadlineText}.";
    }

    public DesktopTaskDto Source { get; }

    public Guid Id => Source.Id;

    public string Title => Source.Title;

    public string StatusText { get; }

    public string PriorityText { get; }

    public string DeadlineText { get; }

    public string UpdatedText { get; }

    public string StatusIconKey { get; }

    public TaskVisualTone StatusTone { get; }

    public string PriorityIconKey { get; }

    public TaskVisualTone PriorityTone { get; }

    public string AutomationName { get; }

    internal static string LocalizeStatus(DesktopTaskStatus status) => status switch
    {
        DesktopTaskStatus.New => "Новая",
        DesktopTaskStatus.InProgress => "В работе",
        DesktopTaskStatus.Review => "На проверке",
        DesktopTaskStatus.Completed => "Завершена",
        DesktopTaskStatus.Cancelled => "Отменена",
        _ => "Неизвестно",
    };

    internal static string LocalizePriority(DesktopTaskPriority priority) => priority switch
    {
        DesktopTaskPriority.Low => "Низкий",
        DesktopTaskPriority.Normal => "Обычный",
        DesktopTaskPriority.High => "Высокий",
        DesktopTaskPriority.Critical => "Критический",
        _ => "Неизвестно",
    };

    internal static string StatusIcon(DesktopTaskStatus status) => status switch
    {
        DesktopTaskStatus.New => "Task.Icon.Status.New",
        DesktopTaskStatus.InProgress => "Task.Icon.Status.InProgress",
        DesktopTaskStatus.Review => "Task.Icon.Status.Review",
        DesktopTaskStatus.Completed => "Task.Icon.Status.Completed",
        DesktopTaskStatus.Cancelled => "Task.Icon.Status.Cancelled",
        _ => "Task.Icon.Info",
    };

    internal static TaskVisualTone StatusVisualTone(DesktopTaskStatus status) => status switch
    {
        DesktopTaskStatus.InProgress => TaskVisualTone.Brand,
        DesktopTaskStatus.Review => TaskVisualTone.Warning,
        DesktopTaskStatus.Completed => TaskVisualTone.Success,
        _ => TaskVisualTone.Neutral,
    };

    internal static string PriorityIcon(DesktopTaskPriority priority) => priority switch
    {
        DesktopTaskPriority.Low => "Task.Icon.Priority.Low",
        DesktopTaskPriority.Normal => "Task.Icon.Priority.Normal",
        DesktopTaskPriority.High => "Task.Icon.Priority.High",
        DesktopTaskPriority.Critical => "Task.Icon.Priority.Critical",
        _ => "Task.Icon.Info",
    };

    internal static TaskVisualTone PriorityVisualTone(DesktopTaskPriority priority) => priority switch
    {
        DesktopTaskPriority.Low => TaskVisualTone.Success,
        DesktopTaskPriority.Normal => TaskVisualTone.Warning,
        DesktopTaskPriority.High or DesktopTaskPriority.Critical => TaskVisualTone.Critical,
        _ => TaskVisualTone.Neutral,
    };

    internal static string FormatDate(DateTimeOffset? value, string emptyText) => value.HasValue
        ? value.Value.ToLocalTime().ToString("g", RussianCulture)
        : emptyText;
}

/// <summary>Localized fields shown in the selected-task detail area.</summary>
public sealed class TaskDetailsViewModel
{
    public TaskDetailsViewModel(DesktopTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);
        Id = task.Id;
        Title = task.Title;
        StatusText = TaskItemViewModel.LocalizeStatus(task.Status);
        PriorityText = TaskItemViewModel.LocalizePriority(task.Priority);
        StartText = TaskItemViewModel.FormatDate(task.StartAtUtc, "Не указано");
        DeadlineText = TaskItemViewModel.FormatDate(task.DeadlineAtUtc, "Без срока");
        CreatedText = TaskItemViewModel.FormatDate(task.CreatedAtUtc, "Не указано");
        UpdatedText = TaskItemViewModel.FormatDate(task.UpdatedAtUtc, "Не указано");
        StatusIconKey = TaskItemViewModel.StatusIcon(task.Status);
        StatusTone = TaskItemViewModel.StatusVisualTone(task.Status);
        PriorityIconKey = TaskItemViewModel.PriorityIcon(task.Priority);
        PriorityTone = TaskItemViewModel.PriorityVisualTone(task.Priority);
        AutomationName = $"{Title}. Статус: {StatusText}. Приоритет: {PriorityText}. Срок: {DeadlineText}.";
    }

    public Guid Id { get; }

    public string Title { get; }

    public string StatusText { get; }

    public string PriorityText { get; }

    public string StartText { get; }

    public string DeadlineText { get; }

    public string CreatedText { get; }

    public string UpdatedText { get; }

    public string StatusIconKey { get; }

    public TaskVisualTone StatusTone { get; }

    public string PriorityIconKey { get; }

    public TaskVisualTone PriorityTone { get; }

    public string AutomationName { get; }
}

/// <summary>
/// Read-only task screen state. Network and session access stay behind
/// <see cref="IDesktopTasksApiClient"/>; the view model only coordinates presentation.
/// </summary>
public sealed class TasksViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopTasksApiClient _client;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private IReadOnlyList<TaskItemViewModel> _items = Array.Empty<TaskItemViewModel>();
    private TaskItemViewModel? _selectedItem;
    private TaskDetailsViewModel? _selectedDetails;
    private TasksScreenState _state = TasksScreenState.Inactive;
    private TaskDetailsState _detailsState;
    private string? _screenMessage;
    private string _detailMessage = "Выберите задачу в списке.";
    private string? _nextCursor;
    private CancellationTokenSource? _activationCancellation;
    private CancellationTokenSource? _detailCancellation;
    private long _detailGeneration;
    private bool _isActive;
    private bool _hasLoaded;
    private bool _disposed;
    private DateTimeOffset? _lastSuccessfulRefreshAt;

    public TasksViewModel(IDesktopTasksApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RefreshCommand = new AsyncCommand(
            async (_, token) => await RefreshAsync(token).ConfigureAwait(true),
            _ => IsActive && !IsBusy);
        LoadMoreCommand = new AsyncCommand(
            async (_, token) => await LoadNextPageAsync(token).ConfigureAwait(true),
            _ => IsActive && !IsBusy && HasNextPage);
    }

    public IReadOnlyList<TaskItemViewModel> Items
    {
        get => _items;
        private set
        {
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(DisplayedCountText));
            OnPropertyChanged(nameof(ShowBlockingState));
            OnPropertyChanged(nameof(ShowInlineMessage));
        }
    }

    public TaskItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            BeginLoadDetails(value);
        }
    }

    public TaskDetailsViewModel? SelectedDetails
    {
        get => _selectedDetails;
        private set => SetProperty(ref _selectedDetails, value);
    }

    public TasksScreenState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsInitialLoading));
                OnPropertyChanged(nameof(IsRefreshing));
                OnPropertyChanged(nameof(IsEmptyState));
                OnPropertyChanged(nameof(IsFailureState));
                OnPropertyChanged(nameof(ShowBlockingState));
                OnPropertyChanged(nameof(ShowInlineMessage));
                OnPropertyChanged(nameof(StateTitle));
                OnPropertyChanged(nameof(StateIconKey));
                RaiseCommandStateChanged();
            }
        }
    }

    public TaskDetailsState DetailsState
    {
        get => _detailsState;
        private set => SetProperty(ref _detailsState, value);
    }

    public string? ScreenMessage
    {
        get => _screenMessage;
        private set
        {
            if (SetProperty(ref _screenMessage, value))
            {
                OnPropertyChanged(nameof(ShowInlineMessage));
            }
        }
    }

    public string DetailMessage
    {
        get => _detailMessage;
        private set => SetProperty(ref _detailMessage, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    public bool IsBusy => State is TasksScreenState.InitialLoading
        or TasksScreenState.Refreshing
        or TasksScreenState.LoadingNextPage;

    public bool HasItems => Items.Count > 0;

    public bool HasNextPage => !string.IsNullOrWhiteSpace(_nextCursor);

    public bool IsInitialLoading => State == TasksScreenState.InitialLoading;

    public bool IsRefreshing => State is TasksScreenState.Refreshing or TasksScreenState.LoadingNextPage;

    public bool IsEmptyState => State == TasksScreenState.Empty;

    public bool IsFailureState => State is TasksScreenState.Error
        or TasksScreenState.Forbidden
        or TasksScreenState.InvalidCursor
        or TasksScreenState.SessionEnded
        or TasksScreenState.Cancelled;

    public bool ShowBlockingState => !HasItems && (IsEmptyState || IsFailureState);

    public bool ShowInlineMessage => HasItems && !string.IsNullOrWhiteSpace(ScreenMessage);

    public string StateTitle => State switch
    {
        TasksScreenState.Empty => "Активных задач нет",
        TasksScreenState.Forbidden => "Нет доступа к задачам",
        TasksScreenState.SessionEnded => "Сессия завершена",
        TasksScreenState.InvalidCursor => "Список задач изменился",
        TasksScreenState.Cancelled => "Загрузка отменена",
        _ => "Не удалось загрузить задачи",
    };

    public string StateIconKey => State == TasksScreenState.Empty
        ? "Task.Icon.Tasks"
        : "Task.Icon.Error";

    public string DisplayedCountText => $"Показано задач: {Items.Count}";

    public bool HasSuccessfulRefresh => _lastSuccessfulRefreshAt.HasValue;

    public string LastSuccessfulRefreshText => _lastSuccessfulRefreshAt.HasValue
        ? $"Последнее обновление: {_lastSuccessfulRefreshAt.Value.ToLocalTime():g}"
        : "Данные ещё не обновлялись";

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand LoadMoreCommand { get; }

    public async global::System.Threading.Tasks.Task ActivateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsActive)
        {
            return;
        }

        _activationCancellation = new CancellationTokenSource();
        IsActive = true;
        if (_hasLoaded)
        {
            SetLoadedState();
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activationCancellation.Token);
        await FetchFirstPageAsync(true, linkedCancellation.Token).ConfigureAwait(true);
    }

    public void Activate() => _ = ActivateAsync();

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        CancelDetailsLoad();
        State = TasksScreenState.Inactive;
        ScreenMessage = null;
    }

    public async global::System.Threading.Tasks.Task<bool> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsActive)
        {
            return false;
        }

        return await FetchFirstPageAsync(false, cancellationToken).ConfigureAwait(true);
    }

    public async global::System.Threading.Tasks.Task<bool> LoadNextPageAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsActive || !HasNextPage || !await _requestGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using var linkedCancellation = CreateActivationLink(cancellationToken);
        try
        {
            State = TasksScreenState.LoadingNextPage;
            ScreenMessage = "Загружаем следующую страницу…";
            var result = await _client.GetTasksAsync(_nextCursor, linkedCancellation.Token).ConfigureAwait(true);
            if (!IsActive)
            {
                return true;
            }

            if (result is DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
            {
                var additions = success.Value.Items.Select(item => new TaskItemViewModel(item));
                Items = Items.Concat(additions).ToArray();
                SetNextCursor(success.Value.NextCursor);
                RecordSuccessfulRefresh();
                SetLoadedState();
            }
            else
            {
                ApplyPageFailure(result, preserveItems: true);
            }

            return true;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            ApplyCancellationState();
            return true;
        }
        catch (Exception)
        {
            if (IsActive)
            {
                State = TasksScreenState.Error;
                ScreenMessage = HasItems
                    ? "Не удалось загрузить следующую страницу. Показан ранее загруженный список."
                    : "Сервер задач недоступен или вернул некорректный ответ. Повторите попытку.";
            }

            return true;
        }
        finally
        {
            _requestGate.Release();
            RaiseCommandStateChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Deactivate();
        RefreshCommand.Dispose();
        LoadMoreCommand.Dispose();
    }

    private async global::System.Threading.Tasks.Task<bool> FetchFirstPageAsync(
        bool initial,
        CancellationToken cancellationToken)
    {
        if (!await _requestGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        using var linkedCancellation = CreateActivationLink(cancellationToken);
        try
        {
            State = initial ? TasksScreenState.InitialLoading : TasksScreenState.Refreshing;
            ScreenMessage = initial ? "Загружаем задачи…" : "Обновляем список задач…";
            var result = await _client.GetTasksAsync(null, linkedCancellation.Token).ConfigureAwait(true);
            if (!IsActive)
            {
                return true;
            }

            if (result is DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
            {
                var selectedId = SelectedItem?.Id;
                var replacement = success.Value.Items.Select(item => new TaskItemViewModel(item)).ToArray();
                Items = replacement;
                SetNextCursor(success.Value.NextCursor);
                _hasLoaded = true;
                RecordSuccessfulRefresh();
                SelectedItem = selectedId.HasValue
                    ? replacement.FirstOrDefault(item => item.Id == selectedId.Value) ?? replacement.FirstOrDefault()
                    : replacement.FirstOrDefault();
                SetLoadedState();
            }
            else
            {
                ApplyPageFailure(result, preserveItems: !initial);
            }

            return true;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            ApplyCancellationState();
            return true;
        }
        catch (Exception)
        {
            if (IsActive)
            {
                State = TasksScreenState.Error;
                ScreenMessage = HasItems
                    ? "Не удалось обновить данные. Показан ранее загруженный список."
                    : "Сервер задач недоступен или вернул некорректный ответ. Повторите попытку.";
            }

            return true;
        }
        finally
        {
            _requestGate.Release();
            RaiseCommandStateChanged();
        }
    }

    private void ApplyPageFailure(DesktopTasksApiResult<DesktopTaskPage> result, bool preserveItems)
    {
        if (!preserveItems)
        {
            ClearTaskData();
        }

        switch (result)
        {
            case DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure:
                ClearTaskData();
                State = TasksScreenState.SessionEnded;
                ScreenMessage = "Сессия завершена. Выполните вход снова.";
                break;
            case DesktopTasksApiResult<DesktopTaskPage>.Forbidden:
                ClearTaskData();
                State = TasksScreenState.Forbidden;
                ScreenMessage = "Недостаточно прав для просмотра задач.";
                break;
            case DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor:
                SetNextCursor(null);
                State = TasksScreenState.InvalidCursor;
                ScreenMessage = "Список изменился. Нажмите «Обновить», чтобы загрузить первую страницу.";
                break;
            default:
                State = TasksScreenState.Error;
                ScreenMessage = HasItems
                    ? "Не удалось обновить данные. Показан ранее загруженный список."
                    : "Сервер задач недоступен или вернул некорректный ответ. Повторите попытку.";
                break;
        }
    }

    private void BeginLoadDetails(TaskItemViewModel? item)
    {
        CancelDetailsLoad();
        SelectedDetails = null;
        if (item is null || !IsActive)
        {
            DetailsState = TaskDetailsState.None;
            DetailMessage = "Выберите задачу в списке.";
            return;
        }

        var generation = Interlocked.Increment(ref _detailGeneration);
        _detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _activationCancellation?.Token ?? CancellationToken.None);
        DetailsState = TaskDetailsState.Loading;
        DetailMessage = "Загружаем карточку задачи…";
        _ = LoadDetailsAsync(item.Id, generation, _detailCancellation.Token);
    }

    private async global::System.Threading.Tasks.Task LoadDetailsAsync(
        Guid id,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.GetTaskByIdAsync(id, cancellationToken).ConfigureAwait(true);
            if (!IsActive || generation != Volatile.Read(ref _detailGeneration) || SelectedItem?.Id != id)
            {
                return;
            }

            switch (result)
            {
                case DesktopTasksApiResult<DesktopTaskDto>.Succeeded success:
                    SelectedDetails = new TaskDetailsViewModel(success.Value);
                    DetailsState = TaskDetailsState.Loaded;
                    DetailMessage = string.Empty;
                    break;
                case DesktopTasksApiResult<DesktopTaskDto>.NotFound:
                    SelectedDetails = null;
                    DetailsState = TaskDetailsState.NotFound;
                    DetailMessage = "Задача больше не существует или недоступна. Обновите список.";
                    break;
                case DesktopTasksApiResult<DesktopTaskDto>.AuthenticationFailure:
                    ClearTaskData();
                    State = TasksScreenState.SessionEnded;
                    ScreenMessage = "Сессия завершена. Выполните вход снова.";
                    break;
                case DesktopTasksApiResult<DesktopTaskDto>.Forbidden:
                    ClearTaskData();
                    State = TasksScreenState.Forbidden;
                    ScreenMessage = "Недостаточно прав для просмотра задач.";
                    break;
                default:
                    SelectedDetails = null;
                    DetailsState = TaskDetailsState.Error;
                    DetailMessage = "Не удалось загрузить карточку задачи.";
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsActive && generation == Volatile.Read(ref _detailGeneration))
            {
                DetailsState = TaskDetailsState.None;
                DetailMessage = "Загрузка карточки отменена.";
            }
        }
        catch (Exception)
        {
            if (IsActive && generation == Volatile.Read(ref _detailGeneration))
            {
                SelectedDetails = null;
                DetailsState = TaskDetailsState.Error;
                DetailMessage = "Не удалось загрузить карточку задачи.";
            }
        }
    }

    private void SetLoadedState()
    {
        State = HasItems ? TasksScreenState.Loaded : TasksScreenState.Empty;
        ScreenMessage = HasItems ? null : "Активных задач нет.";
    }

    private void ApplyCancellationState()
    {
        if (!IsActive)
        {
            return;
        }

        State = TasksScreenState.Cancelled;
        ScreenMessage = "Загрузка отменена. Нажмите «Обновить», чтобы повторить.";
    }

    private void ClearTaskData()
    {
        Items = Array.Empty<TaskItemViewModel>();
        SetNextCursor(null);
        SelectedItem = null;
        SelectedDetails = null;
    }

    private void SetNextCursor(string? value)
    {
        _nextCursor = string.IsNullOrWhiteSpace(value) ? null : value;
        OnPropertyChanged(nameof(HasNextPage));
        RaiseCommandStateChanged();
    }

    private CancellationTokenSource CreateActivationLink(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activationCancellation?.Token ?? CancellationToken.None);

    private void CancelDetailsLoad()
    {
        Interlocked.Increment(ref _detailGeneration);
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        _detailCancellation = null;
    }

    private void RaiseCommandStateChanged()
    {
        RefreshCommand?.RaiseCanExecuteChanged();
        LoadMoreCommand?.RaiseCanExecuteChanged();
    }

    private void RecordSuccessfulRefresh()
    {
        _lastSuccessfulRefreshAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(HasSuccessfulRefresh));
        OnPropertyChanged(nameof(LastSuccessfulRefreshText));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
