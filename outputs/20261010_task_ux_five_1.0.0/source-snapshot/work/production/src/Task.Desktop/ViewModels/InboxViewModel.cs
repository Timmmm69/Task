using System.Collections.ObjectModel;
using System.ComponentModel;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum InboxScreenState
{
    Idle,
    InitialLoading,
    Loaded,
    Empty,
    Offline,
    Forbidden,
    SessionEnded,
    Error,
}

public sealed class InboxItemViewModel
{
    public InboxItemViewModel(DesktopTaskDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        CreatedText = TaskItemViewModel.FormatDate(source.CreatedAtUtc, "Время не указано");
        AutomationName = $"{source.Title}. Без классификации. Создано: {CreatedText}.";
    }

    public DesktopTaskDto Source { get; }
    public Guid Id => Source.Id;
    public string Title => Source.Title;
    public string CreatedText { get; }
    public string StatusText => "Новая";
    public string AutomationName { get; }

    public static bool IsCaptureOnly(DesktopTaskDto task)
    {
        var card = task.Card;
        return task.Status == DesktopTaskStatus.New
            && task.StartAtUtc is null
            && task.DeadlineAtUtc is null
            && task.AssigneeIds.Count == 0
            && task.WatcherIds.Count == 0
            && (card is null ||
                (string.IsNullOrWhiteSpace(card.Description)
                 && card.ProjectId is null
                 && card.ParentTaskId is null
                 && card.RequesterUserId is null
                 && card.PrimaryCounterpartyObjectId is null
                 && card.ScheduledDate is null
                 && card.StartTimeLocal is null
                 && string.IsNullOrWhiteSpace(card.ScheduleTimeZone)
                 && card.PlannedDurationMinutes is null
                 && card.AssigneeIds.Count == 0
                 && card.WatcherIds.Count == 0));
    }
}

public sealed class InboxViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopTasksApiClient _client;
    private readonly HashSet<string> _capabilities;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private IReadOnlyList<InboxItemViewModel> _items = [];
    private InboxItemViewModel? _selectedItem;
    private TaskEditorViewModel? _conversion;
    private string _captureText = string.Empty;
    private string _screenMessage = string.Empty;
    private InboxScreenState _state;
    private bool _isActive;
    private bool _isBusy;
    private bool _networkAvailable = true;
    private bool _sessionAllowsWrites = true;
    private bool _writePermissionChanged;
    private bool _hasLoaded;
    private bool _hasCompleteSnapshot;
    private bool _isRefreshing;
    private long _offlineGeneration;
    private DateTimeOffset? _lastSuccessfulRefreshAt;
    private bool _disposed;
    private CancellationTokenSource? _activationCancellation;
    private long _activationGeneration;

    public InboxViewModel(IDesktopTasksApiClient client, IEnumerable<string>? capabilities = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _capabilities = new HashSet<string>(capabilities ?? [], StringComparer.Ordinal);
        RefreshCommand = new AsyncCommand(
            async (_, token) => await RefreshAsync(token).ConfigureAwait(true),
            _ => IsActive && !IsBusy);
        CaptureCommand = new AsyncCommand(
            async (_, token) => await CaptureAsync(token).ConfigureAwait(true),
            _ => CanCapture);
        ConvertCommand = new AsyncCommand(
            async (parameter, token) => await OpenConversionAsync(parameter as InboxItemViewModel ?? SelectedItem, token).ConfigureAwait(true),
            parameter => CanConvertItem(parameter as InboxItemViewModel ?? SelectedItem));
        SaveConversionCommand = new AsyncCommand(
            async (_, token) => await SaveConversionAsync(token).ConfigureAwait(true),
            _ => CanSaveConversion);
        CancelConversionCommand = new AsyncCommand(
            (_, _) => { CloseConversion(); return global::System.Threading.Tasks.Task.CompletedTask; },
            _ => Conversion is not null && !IsBusy);
        ReloadConflictCommand = new AsyncCommand(
            async (_, token) => await ReloadConflictAsync(token).ConfigureAwait(true),
            _ => Conversion?.HasConflict == true && !IsBusy);
    }

    public IReadOnlyList<InboxItemViewModel> Items
    {
        get => _items;
        private set
        {
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(DisplayedCountText));
            OnPropertyChanged(nameof(ShowBlockingState));
            OnPropertyChanged(nameof(ShowInboxZero));
        }
    }

    public InboxItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            OnPropertyChanged(nameof(HasSelectedItem));
            OnPropertyChanged(nameof(CanConvert));
            ConvertCommand.RaiseCanExecuteChanged();
        }
    }

    public TaskEditorViewModel? Conversion
    {
        get => _conversion;
        private set
        {
            if (ReferenceEquals(_conversion, value)) return;
            if (_conversion is not null) _conversion.PropertyChanged -= OnConversionPropertyChanged;
            _conversion = value;
            if (_conversion is not null) _conversion.PropertyChanged += OnConversionPropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConversion));
            OnPropertyChanged(nameof(CanSaveConversion));
            SaveConversionCommand.RaiseCanExecuteChanged();
            CancelConversionCommand.RaiseCanExecuteChanged();
            ReloadConflictCommand.RaiseCanExecuteChanged();
        }
    }

    public string CaptureText
    {
        get => _captureText;
        set
        {
            if (!SetProperty(ref _captureText, value)) return;
            OnPropertyChanged(nameof(CanCapture));
            CaptureCommand.RaiseCanExecuteChanged();
        }
    }

    public InboxScreenState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsInitialLoading));
            OnPropertyChanged(nameof(ShowBlockingState));
            OnPropertyChanged(nameof(StateTitle));
            OnPropertyChanged(nameof(StateIconKey));
            OnPropertyChanged(nameof(ShowInboxZero));
            NotifyAccessState();
        }
    }

    public string ScreenMessage
    {
        get => _screenMessage;
        private set => SetProperty(ref _screenMessage, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (!SetProperty(ref _isActive, value)) return;
            NotifyCommandState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanCapture));
            OnPropertyChanged(nameof(CanConvert));
            OnPropertyChanged(nameof(CanSaveConversion));
            OnPropertyChanged(nameof(ShowInboxZero));
            OnPropertyChanged(nameof(DisplayedCountText));
            NotifyCommandState();
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool HasSelectedItem => SelectedItem is not null;
    public bool HasConversion => Conversion is not null;
    public bool IsRefreshing => _isRefreshing;
    public bool IsInitialLoading => !HasItems && IsRefreshing;
    public bool ShowInboxZero => State == InboxScreenState.Empty && _hasCompleteSnapshot
        && !HasItems && !IsRefreshing && !IsBusy && _networkAvailable && _sessionAllowsWrites;
    public bool ShowBlockingState => !HasItems && !IsRefreshing && State is InboxScreenState.Offline
        or InboxScreenState.Forbidden or InboxScreenState.SessionEnded or InboxScreenState.Error;
    public bool IsReadOnly => !_networkAvailable || !_sessionAllowsWrites || _writePermissionChanged
        || State is InboxScreenState.Offline or InboxScreenState.Forbidden or InboxScreenState.SessionEnded;
    public string WriteAccessText => !_networkAvailable || State == InboxScreenState.Offline
        ? "Входящие доступны только для чтения: сервер недоступен."
        : !_sessionAllowsWrites || State == InboxScreenState.SessionEnded
            ? "Сессия не подтверждена. Войдите снова, чтобы изменять входящие."
            : _writePermissionChanged || State == InboxScreenState.Forbidden
                ? "Права на изменение входящих были отозваны."
                : "Новые записи и преобразование сохраняются на сервере компании.";
    public bool CanCaptureFromShell => !IsReadOnly && _capabilities.Contains("Task.Create");
    public bool CanCapture => IsActive && !IsBusy && !IsRefreshing && !IsReadOnly
        && _capabilities.Contains("Task.Create") && !string.IsNullOrWhiteSpace(CaptureText);
    public bool CanConvert => CanConvertItem(SelectedItem);
    public bool CanSaveConversion => Conversion is { CanSubmit: true }
        && !string.IsNullOrWhiteSpace(Conversion.DeadlineText) && !IsBusy && !IsRefreshing && !IsReadOnly
        && _capabilities.Contains("Task.Update");
    public string DisplayedCountText => Items.Count switch
    {
        0 => ShowInboxZero ? "Нет необработанных записей" : "Нет отображаемых записей",
        1 => "1 необработанная запись",
        >= 2 and <= 4 => $"{Items.Count} необработанные записи",
        _ => $"{Items.Count} необработанных записей",
    };
    public string LastSuccessfulRefreshText => _lastSuccessfulRefreshAt.HasValue
        ? $"Последнее обновление: {_lastSuccessfulRefreshAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
        : "Входящие ещё не обновлялись";
    public string StateTitle => State switch
    {
        InboxScreenState.Empty => "Входящие разобраны",
        InboxScreenState.Offline => "Нет подключения к серверу",
        InboxScreenState.Forbidden => "Нет доступа к входящим",
        InboxScreenState.SessionEnded => "Сессия завершена",
        _ => "Не удалось загрузить входящие",
    };
    public string StateIconKey => State switch
    {
        InboxScreenState.Empty => "Task.Icon.Inbox",
        InboxScreenState.Offline => "Task.Icon.Offline",
        InboxScreenState.Forbidden or InboxScreenState.SessionEnded => "Task.Icon.Lock",
        _ => "Task.Icon.Info",
    };

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CaptureCommand { get; }
    public AsyncCommand ConvertCommand { get; }
    public AsyncCommand SaveConversionCommand { get; }
    public AsyncCommand CancelConversionCommand { get; }
    public AsyncCommand ReloadConflictCommand { get; }

    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        ThrowIfDisposed();
        _capabilities.Clear();
        foreach (var capability in capabilities ?? []) _capabilities.Add(capability);
        _writePermissionChanged = false;
        NotifyAccessState();
    }

    public void UpdateSessionState(bool signedIn)
    {
        ThrowIfDisposed();
        _sessionAllowsWrites = signedIn;
        if (!signedIn)
        {
            CloseConversion();
            Items = [];
            SelectedItem = null;
            State = InboxScreenState.SessionEnded;
            ScreenMessage = "Сессия завершена. Выполните вход снова.";
        }
        NotifyAccessState();
    }

    public void UpdateConnectivity(bool available)
    {
        ThrowIfDisposed();
        _networkAvailable = available;
        if (!available)
        {
            _offlineGeneration++;
            _hasCompleteSnapshot = false;
        }
        if (!available && IsActive)
        {
            State = InboxScreenState.Offline;
            ScreenMessage = HasItems
                ? "Сервер недоступен. Показаны ранее загруженные записи; изменения отключены."
                : "Сервер недоступен. Обновить входящие и сохранить изменения сейчас нельзя.";
        }
        NotifyAccessState();
    }

    public void Activate() => _ = ActivateAsync();

    public async global::System.Threading.Tasks.Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IsActive = true;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var generation = Interlocked.Increment(ref _activationGeneration);
        await FetchAsync(!_hasLoaded, generation, _activationCancellation.Token).ConfigureAwait(true);
    }

    public void Deactivate()
    {
        if (_disposed) return;
        IsActive = false;
        Interlocked.Increment(ref _activationGeneration);
        _activationCancellation?.Cancel();
        CloseConversion();
    }

    private async global::System.Threading.Tasks.Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsActive) return;
        var generation = Volatile.Read(ref _activationGeneration);
        await FetchAsync(false, generation, cancellationToken).ConfigureAwait(true);
    }

    private async global::System.Threading.Tasks.Task FetchAsync(bool initial, long generation, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!IsActive || generation != Volatile.Read(ref _activationGeneration)) return;
            _hasCompleteSnapshot = false;
            SetRefreshing(true);
            if (!_sessionAllowsWrites) { State = InboxScreenState.SessionEnded; return; }
            var offlineGeneration = _offlineGeneration;
            State = initial ? InboxScreenState.InitialLoading : State;
            if (initial) ScreenMessage = "Загружаем входящие…";
            var tasks = new Dictionary<Guid, DesktopTaskDto>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            long? total = null;
            do
            {
                var result = await _client.GetTasksAsync(cursor, cancellationToken).ConfigureAwait(true);
                if (!IsActive || generation != Volatile.Read(ref _activationGeneration)
                    || cancellationToken.IsCancellationRequested || offlineGeneration != _offlineGeneration || !_sessionAllowsWrites) return;
                if (result is not DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
                {
                    ApplyReadFailure(result);
                    return;
                }
                if (total.HasValue && success.Value.Total.HasValue && total != success.Value.Total)
                    throw new InvalidOperationException("Inbox snapshot changed during pagination.");
                total = success.Value.Total ?? total;
                foreach (var task in success.Value.Items) tasks[task.Id] = task;
                cursor = success.Value.NextCursor;
                if (!string.IsNullOrEmpty(cursor) && !cursors.Add(cursor))
                    throw new InvalidOperationException("Repeated Inbox cursor.");
            } while (!string.IsNullOrEmpty(cursor));
            if (total.HasValue && total.Value != tasks.Count)
                throw new InvalidOperationException("Incomplete Inbox snapshot.");
            _networkAvailable = true;
            var selectedId = SelectedItem?.Id;
            var replacement = tasks.Values.Where(InboxItemViewModel.IsCaptureOnly)
                .Select(task => new InboxItemViewModel(task)).ToArray();
            Items = replacement;
            SelectedItem = selectedId.HasValue
                ? replacement.FirstOrDefault(item => item.Id == selectedId.Value) ?? replacement.FirstOrDefault()
                : replacement.FirstOrDefault();
            _hasLoaded = true;
            _hasCompleteSnapshot = true;
            _lastSuccessfulRefreshAt = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            State = replacement.Length == 0 ? InboxScreenState.Empty : InboxScreenState.Loaded;
            ScreenMessage = replacement.Length == 0
                ? "Здесь появятся быстрые записи без проекта, срока и исполнителя."
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (generation == Volatile.Read(ref _activationGeneration)) State = InboxScreenState.Idle;
        }
        catch (Exception)
        {
            State = _networkAvailable ? InboxScreenState.Error : InboxScreenState.Offline;
            ScreenMessage = HasItems
                ? "Не удалось обновить данные. Показаны ранее загруженные входящие."
                : "Сервер входящих недоступен или вернул некорректный ответ.";
        }
        finally
        {
            SetRefreshing(false);
            _requestGate.Release();
            NotifyCommandState();
        }
    }

    private void SetRefreshing(bool value)
    {
        _isRefreshing = value;
        OnPropertyChanged(nameof(IsRefreshing));
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(ShowBlockingState));
        OnPropertyChanged(nameof(ShowInboxZero));
        OnPropertyChanged(nameof(DisplayedCountText));
        NotifyAccessState();
    }

    private void ApplyReadFailure(DesktopTasksApiResult<DesktopTaskPage> result)
    {
        State = result switch
        {
            DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure => InboxScreenState.SessionEnded,
            DesktopTasksApiResult<DesktopTaskPage>.Forbidden => InboxScreenState.Forbidden,
            DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable => InboxScreenState.Offline,
            _ => InboxScreenState.Error,
        };
        ScreenMessage = State switch
        {
            InboxScreenState.SessionEnded => "Сессия завершена. Выполните вход снова.",
            InboxScreenState.Forbidden => "У вашей учётной записи нет права просматривать задачи.",
            InboxScreenState.Offline when HasItems => "Сервер недоступен. Показаны ранее загруженные записи; изменения отключены.",
            InboxScreenState.Offline => "Сервер недоступен. Повторите попытку после восстановления подключения.",
            _ when HasItems => "Не удалось обновить данные. Показаны ранее загруженные входящие.",
            _ => "Не удалось загрузить входящие. Повторите попытку.",
        };
    }

    private async global::System.Threading.Tasks.Task CaptureAsync(CancellationToken cancellationToken)
    {
        var capturedText = CaptureText;
        var title = capturedText.Trim();
        if (!CanCapture || !await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(true)) return;
        IsBusy = true;
        try
        {
            DesktopCreateTaskCommand command;
            try { command = new DesktopCreateTaskCommand(title, DesktopTaskPriority.Normal); }
            catch (ArgumentException)
            {
                ScreenMessage = "Название должно содержать от 1 до 500 символов.";
                if (!HasItems) State = InboxScreenState.Error;
                return;
            }
            var result = await _client.CreateTaskAsync(command, cancellationToken).ConfigureAwait(true);
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success)
            {
                var item = new InboxItemViewModel(success.Value);
                Items = [item, .. Items.Where(existing => existing.Id != item.Id)];
                SelectedItem = item;
                if (CaptureText == capturedText) CaptureText = string.Empty;
                State = InboxScreenState.Loaded;
                ScreenMessage = "Запись добавлена во входящие.";
            }
            else
            {
                ApplyWriteFailure(result, "Не удалось добавить запись во входящие.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            ScreenMessage = "Сервер недоступен. Запись не добавлена; текст сохранён.";
            if (!HasItems) State = InboxScreenState.Offline;
        }
        finally
        {
            IsBusy = false;
            _mutationGate.Release();
        }
    }

    private async global::System.Threading.Tasks.Task OpenConversionAsync(InboxItemViewModel? item, CancellationToken cancellationToken)
    {
        if (!CanConvertItem(item)) return;
        SelectedItem = item;
        Conversion = new TaskEditorViewModel(TaskEditorMode.Edit, item!.Source);
        await LoadConversionOptionsAsync(Conversion, cancellationToken).ConfigureAwait(true);
    }

    private async global::System.Threading.Tasks.Task LoadConversionOptionsAsync(TaskEditorViewModel editor, CancellationToken cancellationToken)
    {
        editor.Card.CanAssign = false;
        editor.Card.CanWatch = false;
        if (_client is not IDesktopTaskWorkspaceClient workspace)
        {
            editor.Card.Message = "Список проектов недоступен.";
            return;
        }
        var result = await workspace.GetOptionsAsync(editor.Card.Search, cancellationToken).ConfigureAwait(true);
        if (!ReferenceEquals(Conversion, editor)) return;
        if (result.Succeeded) editor.Card.SetOptions(result.Body!);
        else editor.Card.Message = result.Error;
        OnPropertyChanged(nameof(CanSaveConversion));
        SaveConversionCommand.RaiseCanExecuteChanged();
    }

    private async global::System.Threading.Tasks.Task SaveConversionAsync(CancellationToken cancellationToken)
    {
        var editor = Conversion;
        if (editor is null) return;
        if (string.IsNullOrWhiteSpace(editor.DeadlineText))
        {
            editor.SetStatus("Укажите срок задачи.");
            return;
        }
        if (!CanSaveConversion || !await _mutationGate.WaitAsync(0, cancellationToken).ConfigureAwait(true)) return;
        var command = editor.BuildPatchCommand();
        if (command is null) return;
        IsBusy = true;
        editor.IsBusy = true;
        try
        {
            var result = await _client.PatchTaskAsync(command, cancellationToken).ConfigureAwait(true);
            if (!ReferenceEquals(Conversion, editor)) return;
            switch (result)
            {
                case DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success:
                    Items = Items.Where(item => item.Id != success.Value.Id).ToArray();
                    SelectedItem = Items.FirstOrDefault();
                    CloseConversion();
                    State = InboxScreenState.Loaded;
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    if (State is InboxScreenState.Empty or InboxScreenState.Loaded)
                        ScreenMessage = "Задача создана в разделе «Задачи», исходная запись закрыта. Связь сохранена в истории задачи.";
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation:
                    editor.ApplyServerValidation(validation.Message, validation.FieldErrors);
                    break;
                case DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict:
                    editor.SetConflict();
                    ScreenMessage = "Запись изменилась на сервере. Ваш черновик сохранён; загрузите актуальную версию или отмените преобразование.";
                    break;
                default:
                    ApplyWriteFailure(result, "Не удалось преобразовать запись. Черновик сохранён.");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            editor.SetStatus("Сервер недоступен. Черновик сохранён; повторите попытку.");
        }
        finally
        {
            editor.IsBusy = false;
            IsBusy = false;
            _mutationGate.Release();
        }
    }

    private async global::System.Threading.Tasks.Task ReloadConflictAsync(CancellationToken cancellationToken)
    {
        var editor = Conversion;
        if (editor?.SourceId is not Guid id || !editor.HasConflict) return;
        var result = await _client.GetTaskByIdAsync(id, cancellationToken).ConfigureAwait(true);
        if (!ReferenceEquals(Conversion, editor)) return;
        if (result is DesktopTasksApiResult<DesktopTaskDto>.Succeeded success)
        {
            var replacement = new TaskEditorViewModel(TaskEditorMode.Edit, success.Value);
            Conversion = replacement;
            replacement.SetStatus("Загружена актуальная версия. Проверьте поля и повторите преобразование.");
            await LoadConversionOptionsAsync(replacement, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            editor.SetStatus("Не удалось загрузить актуальную версию. Черновик сохранён.");
        }
    }

    private void ApplyWriteFailure(DesktopTaskWriteResult<DesktopTaskDto> result, string fallback)
    {
        ScreenMessage = result switch
        {
            DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopTaskWriteResult<DesktopTaskDto>.Forbidden => "Недостаточно прав для этого действия.",
            DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable => "Сервер недоступен. Изменения не сохранены.",
            DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress => "Сервер ещё обрабатывает запрос. Повторите позже.",
            _ => fallback,
        };
        if (result is DesktopTaskWriteResult<DesktopTaskDto>.Forbidden)
        {
            _writePermissionChanged = true;
            NotifyAccessState();
        }
        Conversion?.SetStatus(ScreenMessage);
        if (State == InboxScreenState.Empty)
        {
            State = result is DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable
                ? InboxScreenState.Offline : InboxScreenState.Error;
            NotifyAccessState();
        }
    }

    private bool CanConvertItem(InboxItemViewModel? item) => item is not null && IsActive && !IsBusy && !IsRefreshing && !IsReadOnly
        && _capabilities.Contains("Task.Update") && InboxItemViewModel.IsCaptureOnly(item.Source);

    private void CloseConversion()
    {
        Conversion = null;
    }

    private void OnConversionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanSaveConversion));
        SaveConversionCommand.RaiseCanExecuteChanged();
        ReloadConflictCommand.RaiseCanExecuteChanged();
    }

    private void NotifyAccessState()
    {
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(ShowInboxZero));
        OnPropertyChanged(nameof(CanCaptureFromShell));
        OnPropertyChanged(nameof(DisplayedCountText));
        OnPropertyChanged(nameof(WriteAccessText));
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanSaveConversion));
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        CaptureCommand.RaiseCanExecuteChanged();
        ConvertCommand.RaiseCanExecuteChanged();
        SaveConversionCommand.RaiseCanExecuteChanged();
        CancelConversionCommand.RaiseCanExecuteChanged();
        ReloadConflictCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        if (_conversion is not null) _conversion.PropertyChanged -= OnConversionPropertyChanged;
        _requestGate.Dispose();
        _mutationGate.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
