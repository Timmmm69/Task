using System.Globalization;
using System.ComponentModel;
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
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly HashSet<string> _capabilities;

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
    private TaskEditorViewModel? _editor;
    private object? _pendingEditorCommand;
    private long _pendingEditorRevision = -1;
    private CancellationTokenSource? _mutationCancellation;
    private long _mutationGeneration;
    private bool _isMutationBusy;
    private bool _writePermissionChanged;
    private bool _sessionAllowsWrites = true;
    private DesktopTaskStatus? _pendingTransition;
    private DesktopTransitionTaskCommand? _pendingTransitionCommand;
    private bool _transitionRetryAvailable = true;
    private string _transitionReason = string.Empty;
    private string? _announcement;

    public TasksViewModel(
        IDesktopTasksApiClient client,
        IEnumerable<string>? capabilities = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _capabilities = new HashSet<string>(capabilities ?? [], StringComparer.Ordinal);
        RefreshCommand = new AsyncCommand(
            async (_, token) => await RefreshAsync(token).ConfigureAwait(true),
            _ => IsActive && !IsBusy);
        LoadMoreCommand = new AsyncCommand(
            async (_, token) => await LoadNextPageAsync(token).ConfigureAwait(true),
            _ => IsActive && !IsBusy && HasNextPage);
        NewTaskCommand = new AsyncCommand(
            (_, _) => { OpenCreateEditor(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => CanCreate);
        EditTaskCommand = new AsyncCommand(
            (_, _) => { OpenEditEditor(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => CanEdit);
        SaveEditorCommand = new AsyncCommand(
            async (_, token) => await SaveEditorAsync(token).ConfigureAwait(true),
            _ => CanSaveEditor);
        CancelEditorCommand = new AsyncCommand(
            (_, _) => { RequestCloseEditor(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => Editor is not null && !IsMutationBusy);
        DiscardEditorCommand = new AsyncCommand(
            (_, _) => { CloseEditor(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => Editor is not null && !IsMutationBusy);
        ContinueEditingCommand = new AsyncCommand(
            (_, _) => { Editor?.ShowDiscardConfirmation(false); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => Editor is not null && !IsMutationBusy);
        ReloadConflictCommand = new AsyncCommand(
            async (_, token) => await ReloadConflictAsync(token).ConfigureAwait(true),
            _ => Editor?.HasConflict == true && !IsMutationBusy);
        TransitionCommand = new AsyncCommand(
            (parameter, _) => { BeginTransition(parameter); return global::System.Threading.Tasks.Task.CompletedTask; },
            CanBeginTransition);
        ConfirmTransitionCommand = new AsyncCommand(
            async (_, token) => await ConfirmTransitionAsync(token).ConfigureAwait(true),
            _ => CanChangeStatus && PendingTransition.HasValue && _transitionRetryAvailable
                && !IsMutationBusy && TransitionReason.Length <= 2000);
        CancelTransitionCommand = new AsyncCommand(
            (_, _) => { ClearTransition(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => PendingTransition.HasValue && !IsMutationBusy);
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

            ClearTransition();
            BeginLoadDetails(value);
            NotifyMutationState();
        }
    }

    public TaskEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (_editor is not null)
            {
                _editor.PropertyChanged -= OnEditorPropertyChanged;
            }

            if (SetProperty(ref _editor, value) && _editor is not null)
            {
                _editor.PropertyChanged += OnEditorPropertyChanged;
            }

            OnPropertyChanged(nameof(HasEditor));
            NotifyMutationState();
        }
    }

    public bool HasEditor => Editor is not null;

    public bool IsMutationBusy
    {
        get => _isMutationBusy;
        private set
        {
            if (SetProperty(ref _isMutationBusy, value))
            {
                if (Editor is not null) Editor.IsBusy = value;
                NotifyMutationState();
            }
        }
    }

    public bool CanCreate => CanWrite("Task.Create");
    public bool CanUpdate => CanWrite("Task.Update");
    public bool CanChangeStatus => CanWrite("Task.ChangeStatus");
    public bool IsReadOnly => !CanCreate && !CanUpdate && !CanChangeStatus;
    public string WriteAccessText => IsReadOnly
        ? "Доступен только просмотр задач. Для изменений нужны соответствующие права."
        : "Изменения сохраняются на сервере компании.";
    public bool CanEdit => IsActive && CanUpdate && SelectedItem is { Source.Status: not DesktopTaskStatus.Completed and not DesktopTaskStatus.Cancelled } && !IsMutationBusy;
    public bool CanStart => CanTransitionTo(DesktopTaskStatus.InProgress);
    public bool CanSubmitForReview => CanTransitionTo(DesktopTaskStatus.Review);
    public bool CanComplete => CanTransitionTo(DesktopTaskStatus.Completed);
    public bool CanCancel => CanTransitionTo(DesktopTaskStatus.Cancelled);

    public DesktopTaskStatus? PendingTransition
    {
        get => _pendingTransition;
        private set
        {
            if (SetProperty(ref _pendingTransition, value))
            {
                OnPropertyChanged(nameof(IsTransitionConfirmationVisible));
                OnPropertyChanged(nameof(PendingTransitionText));
                NotifyMutationState();
            }
        }
    }

    public bool IsTransitionConfirmationVisible => PendingTransition.HasValue;
    public string PendingTransitionText => PendingTransition switch
    {
        DesktopTaskStatus.InProgress => "Начать задачу?",
        DesktopTaskStatus.Review => "Отправить задачу на проверку?",
        DesktopTaskStatus.Completed => "Завершить задачу? Это терминальное действие.",
        DesktopTaskStatus.Cancelled => "Отменить задачу? Это терминальное действие.",
        _ => string.Empty,
    };

    public string TransitionReason
    {
        get => _transitionReason;
        set
        {
            if (_pendingTransitionCommand is not null)
            {
                return;
            }
            if (SetProperty(ref _transitionReason, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(TransitionReasonError));
                ConfirmTransitionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? TransitionReasonError => TransitionReason.Length > 2000
        ? "Причина не должна превышать 2000 символов."
        : null;

    public string? Announcement
    {
        get => _announcement;
        private set => SetProperty(ref _announcement, value);
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
                NotifyMutationState();
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
                NotifyMutationState();
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
    public AsyncCommand NewTaskCommand { get; }
    public AsyncCommand EditTaskCommand { get; }
    public AsyncCommand SaveEditorCommand { get; }
    public AsyncCommand CancelEditorCommand { get; }
    public AsyncCommand DiscardEditorCommand { get; }
    public AsyncCommand ContinueEditingCommand { get; }
    public AsyncCommand ReloadConflictCommand { get; }
    public AsyncCommand TransitionCommand { get; }
    public AsyncCommand ConfirmTransitionCommand { get; }
    public AsyncCommand CancelTransitionCommand { get; }

    internal void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        ThrowIfDisposed();
        _capabilities.Clear();
        foreach (var capability in capabilities ?? [])
        {
            _capabilities.Add(capability);
        }

        _writePermissionChanged = false;
        if (Editor is not null && !HasCapabilityForEditor(Editor))
        {
            Editor.SetStatus("Права изменились. Данные формы сохранены, но запись сейчас недоступна.");
        }

        if (PendingTransition.HasValue && !_capabilities.Contains("Task.ChangeStatus"))
        {
            ClearTransition();
            ScreenMessage = "Право на изменение статуса было отозвано. Подтверждение отменено.";
        }

        NotifyMutationState();
    }

    internal void UpdateSessionState(bool allowsWrites)
    {
        ThrowIfDisposed();
        if (_sessionAllowsWrites == allowsWrites)
        {
            return;
        }

        _sessionAllowsWrites = allowsWrites;
        if (!allowsWrites)
        {
            Interlocked.Increment(ref _mutationGeneration);
            _mutationCancellation?.Cancel();
            if (Editor is not null)
            {
                Editor.SetStatus("Сессия не допускает запись. Данные формы сохранены.");
            }

            if (PendingTransition.HasValue)
            {
                ClearTransition();
                ScreenMessage = "Сессия завершена. Подтверждение изменения статуса отменено.";
            }
        }

        NotifyMutationState();
    }

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
        Interlocked.Increment(ref _mutationGeneration);
        _mutationCancellation?.Cancel();
        State = TasksScreenState.Inactive;
        ScreenMessage = null;
        NotifyMutationState();
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
        NewTaskCommand.Dispose();
        EditTaskCommand.Dispose();
        SaveEditorCommand.Dispose();
        CancelEditorCommand.Dispose();
        DiscardEditorCommand.Dispose();
        ContinueEditingCommand.Dispose();
        ReloadConflictCommand.Dispose();
        TransitionCommand.Dispose();
        ConfirmTransitionCommand.Dispose();
        CancelTransitionCommand.Dispose();
        _mutationCancellation?.Dispose();
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

    private void OpenCreateEditor()
    {
        if (!CanCreate) return;
        CloseEditor();
        Editor = new TaskEditorViewModel(TaskEditorMode.Create);
        Announcement = "Открыта форма создания задачи.";
    }

    private void OpenEditEditor()
    {
        if (!CanEdit || SelectedItem is null) return;
        CloseEditor();
        Editor = new TaskEditorViewModel(TaskEditorMode.Edit, SelectedItem.Source);
        Announcement = "Открыта форма изменения задачи.";
    }

    private void RequestCloseEditor()
    {
        if (Editor is null || IsMutationBusy) return;
        if (Editor.HasUnsavedChanges)
        {
            Editor.ShowDiscardConfirmation(true);
            Announcement = "Есть несохранённые изменения. Подтвердите закрытие формы.";
            return;
        }

        CloseEditor();
    }

    private void CloseEditor()
    {
        Interlocked.Increment(ref _mutationGeneration);
        _mutationCancellation?.Cancel();
        _mutationCancellation?.Dispose();
        _mutationCancellation = null;
        _pendingEditorCommand = null;
        _pendingEditorRevision = -1;
        Editor = null;
    }

    private async global::System.Threading.Tasks.Task SaveEditorAsync(CancellationToken cancellationToken)
    {
        var editor = Editor;
        if (!CanSaveEditor || editor is null
            || !await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            if (editor is not null && !HasCapabilityForEditor(editor))
            {
                editor.SetStatus("Недостаточно прав для сохранения. Данные формы сохранены.");
            }
            return;
        }

        object? command;
        try
        {
            command = GetOrCreateEditorCommand(editor);
        }
        catch (ArgumentException)
        {
            editor.SetStatus("Проверьте заполненные поля.");
            _mutationGate.Release();
            return;
        }

        if (command is null)
        {
            _mutationGate.Release();
            return;
        }

        var generation = Interlocked.Increment(ref _mutationGeneration);
        _mutationCancellation?.Dispose();
        _mutationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activationCancellation?.Token ?? CancellationToken.None);
        IsMutationBusy = true;
        try
        {
            DesktopTaskWriteResult<DesktopTaskDto> result = command switch
            {
                DesktopCreateTaskCommand create => await _client.CreateTaskAsync(
                    create, _mutationCancellation.Token).ConfigureAwait(true),
                DesktopPatchTaskCommand patch => await _client.PatchTaskAsync(
                    patch, _mutationCancellation.Token).ConfigureAwait(true),
                _ => throw new InvalidOperationException("Unsupported editor command."),
            };

            if (!IsCurrentMutation(generation, editor)) return;
            HandleEditorResult(editor, result, generation);
        }
        catch (OperationCanceledException) when (_mutationCancellation.IsCancellationRequested)
        {
            if (IsCurrentMutation(generation, editor))
            {
                editor.SetStatus("Сохранение отменено. Данные формы сохранены.");
            }
        }
        catch (Exception)
        {
            if (IsCurrentMutation(generation, editor))
            {
                editor.SetStatus("Сервер недоступен. Данные формы сохранены; повторите попытку.");
            }
        }
        finally
        {
            IsMutationBusy = false;
            _mutationGate.Release();
        }
    }

    private object? GetOrCreateEditorCommand(TaskEditorViewModel editor)
    {
        if (_pendingEditorCommand is not null && _pendingEditorRevision == editor.Revision)
        {
            return _pendingEditorCommand;
        }

        _pendingEditorCommand = editor.Mode == TaskEditorMode.Create
            ? editor.BuildCreateCommand()
            : editor.BuildPatchCommand();
        _pendingEditorRevision = editor.Revision;
        return _pendingEditorCommand;
    }

    private void HandleEditorResult(
        TaskEditorViewModel editor,
        DesktopTaskWriteResult<DesktopTaskDto> result,
        long generation)
    {
        switch (result)
        {
            case DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success:
                ApplyServerTask(success.Value);
                _pendingEditorCommand = null;
                Announcement = editor.Mode == TaskEditorMode.Create
                    ? "Задача создана и выбрана."
                    : "Изменения задачи сохранены.";
                Editor = null;
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation:
                editor.ApplyServerValidation(validation.Message, validation.FieldErrors);
                _pendingEditorCommand = null;
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict:
                editor.SetConflict();
                _pendingEditorCommand = null;
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress:
                editor.SetStatus("Сервер ещё обрабатывает эту команду. Повтор станет доступен через несколько секунд.");
                editor.SetRetryAvailable(false);
                _ = EnableEditorRetryAsync(editor, generation);
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict:
                editor.SetStatus("Ключ предыдущей команды уже использован. Нажмите «Сохранить» ещё раз для новой попытки.");
                _pendingEditorCommand = null;
                _pendingEditorRevision = -1;
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.Forbidden:
                _writePermissionChanged = true;
                editor.SetStatus("Права изменились. Данные формы сохранены, но запись сейчас недоступна.");
                ScreenMessage = "Права на изменение задач были отозваны. Доступен только просмотр.";
                NotifyMutationState();
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure:
                State = TasksScreenState.SessionEnded;
                ScreenMessage = "Сессия завершена. Выполните вход снова.";
                editor.SetStatus("Сессия завершена. Данные формы не отправлены повторно.");
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable:
                editor.SetStatus("Сервер недоступен. Данные формы сохранены; повторите попытку.");
                break;
            case DesktopTaskWriteResult<DesktopTaskDto>.NotFound:
                editor.SetStatus("Задача больше не существует или недоступна.");
                _pendingEditorCommand = null;
                break;
            default:
                editor.SetStatus("Сервер отклонил команду. Данные формы сохранены.");
                _pendingEditorCommand = null;
                break;
        }
    }

    private async global::System.Threading.Tasks.Task EnableEditorRetryAsync(
        TaskEditorViewModel editor,
        long generation)
    {
        try
        {
            await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            if (IsCurrentMutation(generation, editor))
            {
                editor.SetRetryAvailable(true);
                editor.SetStatus("Команду можно безопасно повторить с тем же ключом.");
            }
        }
        catch (Exception)
        {
        }
    }

    private async global::System.Threading.Tasks.Task ReloadConflictAsync(CancellationToken cancellationToken)
    {
        var editor = Editor;
        if (editor?.SourceId is not Guid id || !editor.HasConflict) return;
        var result = await _client.GetTaskByIdAsync(id, cancellationToken).ConfigureAwait(true);
        if (!ReferenceEquals(Editor, editor)) return;
        if (result is DesktopTasksApiResult<DesktopTaskDto>.Succeeded success)
        {
            ApplyServerTask(success.Value);
            Editor = new TaskEditorViewModel(TaskEditorMode.Edit, success.Value);
            Editor.SetStatus("Загружена актуальная версия. Повторите изменение вручную.");
            Announcement = "Актуальная версия задачи загружена.";
        }
        else
        {
            editor.SetStatus("Не удалось загрузить актуальную версию. Повторите позже или закройте форму.");
        }
    }

    private void BeginTransition(object? parameter)
    {
        if (!TryReadStatus(parameter, out var status) || !CanTransitionTo(status)) return;
        PendingTransition = status;
        _pendingTransitionCommand = null;
        _transitionRetryAvailable = true;
        TransitionReason = string.Empty;
        Announcement = PendingTransitionText;
    }

    private async global::System.Threading.Tasks.Task ConfirmTransitionAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedItem;
        var target = PendingTransition;
        if (selected is null || !target.HasValue || !CanChangeStatus
            || !IsAllowedTransition(selected.Source.Status, target.Value)
            || !await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        var command = _pendingTransitionCommand ??= new DesktopTransitionTaskCommand(
            selected.Id,
            selected.Source.Version,
            target.Value,
            string.IsNullOrWhiteSpace(TransitionReason) ? null : TransitionReason.Trim());
        var generation = Interlocked.Increment(ref _mutationGeneration);
        _mutationCancellation?.Dispose();
        _mutationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activationCancellation?.Token ?? CancellationToken.None);
        IsMutationBusy = true;
        try
        {
            var result = await _client.TransitionTaskAsync(command, _mutationCancellation.Token).ConfigureAwait(true);
            if (generation != Volatile.Read(ref _mutationGeneration) || SelectedItem?.Id != selected.Id) return;
            switch (result)
            {
                case DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success:
                    ApplyServerTask(success.Value);
                    Announcement = $"Статус задачи изменён: {TaskItemViewModel.LocalizeStatus(success.Value.Status)}.";
                    ClearTransition();
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict:
                    ClearTransition();
                    Editor = new TaskEditorViewModel(TaskEditorMode.Edit, selected.Source);
                    Editor.SetConflict();
                    ScreenMessage = "Задача уже изменена другим пользователем. Загрузите актуальную версию или закройте форму.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress:
                    ScreenMessage = "Сервер ещё обрабатывает смену статуса. Повторите подтверждение позже.";
                    _transitionRetryAvailable = false;
                    ConfirmTransitionCommand.RaiseCanExecuteChanged();
                    _ = EnableTransitionRetryAsync(generation, selected.Id);
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict:
                    ScreenMessage = "Ключ смены статуса уже использован. Подтвердите действие ещё раз для новой команды.";
                    _pendingTransitionCommand = null;
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.Forbidden:
                    _writePermissionChanged = true;
                    ScreenMessage = "Права на изменение статуса были отозваны. Доступен только просмотр.";
                    ClearTransition();
                    NotifyMutationState();
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure:
                    State = TasksScreenState.SessionEnded;
                    ScreenMessage = "Сессия завершена. Выполните вход снова.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable:
                    ScreenMessage = "Сервер недоступен. Смена статуса не подтверждена; повторите позже.";
                    break;
                default:
                    ScreenMessage = "Не удалось изменить статус задачи.";
                    ClearTransition();
                    break;
            }
        }
        catch (OperationCanceledException) when (_mutationCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            IsMutationBusy = false;
            _mutationGate.Release();
        }
    }

    private bool CanBeginTransition(object? parameter) =>
        TryReadStatus(parameter, out var status) && CanTransitionTo(status);

    private bool CanTransitionTo(DesktopTaskStatus target)
    {
        if (!IsActive || !CanChangeStatus || IsMutationBusy || PendingTransition.HasValue || SelectedItem is null)
        {
            return false;
        }

        var current = SelectedItem.Source.Status;
        if (current is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled) return false;
        return IsAllowedTransition(current, target);
    }

    private static bool IsAllowedTransition(DesktopTaskStatus current, DesktopTaskStatus target)
    {
        if (current is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled) return false;
        return target switch
        {
            DesktopTaskStatus.InProgress => current == DesktopTaskStatus.New,
            DesktopTaskStatus.Review => current == DesktopTaskStatus.InProgress,
            DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled => true,
            _ => false,
        };
    }

    private static bool TryReadStatus(object? parameter, out DesktopTaskStatus status)
    {
        if (parameter is DesktopTaskStatus typed)
        {
            status = typed;
            return true;
        }

        return Enum.TryParse(parameter?.ToString(), ignoreCase: true, out status);
    }

    private void ClearTransition()
    {
        _pendingTransitionCommand = null;
        PendingTransition = null;
        TransitionReason = string.Empty;
        _transitionRetryAvailable = true;
    }

    private async global::System.Threading.Tasks.Task EnableTransitionRetryAsync(long generation, Guid taskId)
    {
        await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        if (generation == Volatile.Read(ref _mutationGeneration) && SelectedItem?.Id == taskId
            && PendingTransition.HasValue)
        {
            _transitionRetryAvailable = true;
            ScreenMessage = "Смену статуса можно безопасно повторить с тем же ключом.";
            ConfirmTransitionCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyServerTask(DesktopTaskDto task)
    {
        CancelDetailsLoad();
        var item = new TaskItemViewModel(task);
        var replacement = Items.Select(existing => existing.Id == task.Id ? item : existing).ToList();
        if (replacement.All(existing => existing.Id != task.Id)) replacement.Insert(0, item);
        Items = replacement;
        _selectedItem = item;
        OnPropertyChanged(nameof(SelectedItem));
        SelectedDetails = new TaskDetailsViewModel(task);
        DetailsState = TaskDetailsState.Loaded;
        DetailMessage = string.Empty;
        SetLoadedState();
        NotifyMutationState();
    }

    private bool IsCurrentMutation(long generation, TaskEditorViewModel editor) =>
        generation == Volatile.Read(ref _mutationGeneration) && ReferenceEquals(Editor, editor) && IsActive;

    private bool CanSaveEditor => Editor is { CanSubmit: true } editor
        && !IsMutationBusy && HasCapabilityForEditor(editor);

    private bool HasCapabilityForEditor(TaskEditorViewModel editor) => editor.Mode switch
    {
        TaskEditorMode.Create => CanCreate,
        TaskEditorMode.Edit => CanUpdate,
        _ => false,
    };

    private bool CanWrite(string capability) => IsActive
        && _sessionAllowsWrites
        && !_writePermissionChanged
        && _capabilities.Contains(capability)
        && State is not TasksScreenState.SessionEnded and not TasksScreenState.Forbidden;

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskEditorViewModel.CanSubmit)
            or nameof(TaskEditorViewModel.HasConflict)
            or nameof(TaskEditorViewModel.IsDiscardConfirmationVisible))
        {
            NotifyMutationState();
        }
    }

    private void NotifyMutationState()
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanChangeStatus));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(WriteAccessText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSubmitForReview));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanCancel));
        NewTaskCommand?.RaiseCanExecuteChanged();
        EditTaskCommand?.RaiseCanExecuteChanged();
        SaveEditorCommand?.RaiseCanExecuteChanged();
        CancelEditorCommand?.RaiseCanExecuteChanged();
        DiscardEditorCommand?.RaiseCanExecuteChanged();
        ContinueEditingCommand?.RaiseCanExecuteChanged();
        ReloadConflictCommand?.RaiseCanExecuteChanged();
        TransitionCommand?.RaiseCanExecuteChanged();
        ConfirmTransitionCommand?.RaiseCanExecuteChanged();
        CancelTransitionCommand?.RaiseCanExecuteChanged();
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
