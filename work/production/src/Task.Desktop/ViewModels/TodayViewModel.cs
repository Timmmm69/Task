using System.Globalization;
using Task.Desktop.Calendar;

namespace Task.Desktop.ViewModels;

public enum TodayScreenState
{
    Inactive,
    Loading,
    Loaded,
    Empty,
    Refreshing,
    Error,
    Forbidden,
    SessionEnded,
}

public sealed partial class TodayViewModel : ViewModelBase, IDisposable
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    private readonly IDesktopCalendarApiClient _client;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _capabilities;

    private CancellationTokenSource? _requestCancellation;
    private long _generation;
    private bool _active;
    private bool _sessionAvailable = true;
    private bool _disposed;

    private TodayScreenState _state = TodayScreenState.Inactive;
    private DateOnly _today;
    private IReadOnlyList<CalendarItemViewModel> _timedItems =
        Array.Empty<CalendarItemViewModel>();
    private IReadOnlyList<CalendarItemViewModel> _untimedItems =
        Array.Empty<CalendarItemViewModel>();
    private string? _message;
    private DateTimeOffset? _lastSuccessfulRefresh;

    public TodayViewModel(
        IDesktopCalendarApiClient client,
        IEnumerable<string>? capabilities = null,
        TimeZoneInfo? timeZone = null,
        Func<DateTimeOffset>? clock = null,
        Task.Desktop.TaskApi.IDesktopTasksApiClient? tasksClient = null,
        Guid? currentUserId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _capabilities = new HashSet<string>(
            capabilities ?? [],
            StringComparer.Ordinal);

        _today = ResolveToday();
        _tasksClient = tasksClient;
        _currentUserId = currentUserId;
        OpenItemCommand = new AsyncCommand((item, _) =>
        {
            OpenItemRequested?.Invoke(item);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, item => IsActive && _sessionAvailable && (item is CalendarItemViewModel { IsCalendarEvent: true } ? CanRead : CanReadTasks));

        RefreshCommand = new AsyncCommand(
            async (_, token) =>
                await LoadAsync(refresh: true, token).ConfigureAwait(true),
            _ => IsActive && _sessionAvailable && CanAccess && !IsBusy);
    }

    public TodayScreenState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowMessage));
            RefreshCommand.RaiseCanExecuteChanged();
            OpenItemCommand.RaiseCanExecuteChanged();
        }
    }

    public DateOnly Today => _today;

    public string DateText =>
        _today.ToDateTime(TimeOnly.MinValue)
            .ToString("dddd, d MMMM yyyy", RussianCulture);

    public IReadOnlyList<CalendarItemViewModel> TimedItems
    {
        get => _timedItems;
        private set
        {
            if (!SetProperty(ref _timedItems, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasTimedItems));
            OnPropertyChanged(nameof(HasItems));
        }
    }

    public IReadOnlyList<CalendarItemViewModel> UntimedItems
    {
        get => _untimedItems;
        private set
        {
            if (!SetProperty(ref _untimedItems, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasUntimedItems));
            OnPropertyChanged(nameof(HasItems));
        }
    }

    public bool HasTimedItems => TimedItems.Count > 0;

    public bool HasUntimedItems => UntimedItems.Count > 0;

    public bool HasItems => HasTimedItems || HasUntimedItems || OverdueTasks.Count > 0 || ReviewTasks.Count > 0 || WaitingTasks.Count > 0;

    public bool ShowEmptyState => State == TodayScreenState.Empty;

    public bool IsBusy =>
        State is TodayScreenState.Loading or TodayScreenState.Refreshing;

    public bool IsActive => _active;

    private bool CanAccess => CanRead || (_tasksClient is not null && CanReadTasks);

    public bool CanRead => _capabilities.Contains("Calendar.Read");

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(ShowMessage));
            }
        }
    }

    public bool ShowMessage => Message is not null;

    public string LastSuccessfulRefreshText =>
        _lastSuccessfulRefresh.HasValue
            ? $"Обновлено {TimeZoneInfo.ConvertTime(_lastSuccessfulRefresh.Value, _timeZone):HH:mm}"
            : "Ещё не обновлялось";

    public AsyncCommand RefreshCommand { get; }

    public void Activate() => _ = ActivateAsync();

    public async global::System.Threading.Tasks.Task ActivateAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _active)
        {
            return;
        }

        _active = true;
        OnPropertyChanged(nameof(IsActive));
        RefreshCommand.RaiseCanExecuteChanged();
        OpenItemCommand.RaiseCanExecuteChanged();

        if (!_sessionAvailable)
        {
            ClearProtectedData();
            State = TodayScreenState.SessionEnded;
            Message = "Сессия завершена. Войдите снова.";
            return;
        }

        if (!CanAccess)
        {
            ClearProtectedData();
            State = TodayScreenState.Forbidden;
            Message = "Нет права Calendar.Read для просмотра расписания.";
            return;
        }

        await LoadAsync(refresh: false, cancellationToken).ConfigureAwait(true);
    }

    public void Deactivate()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Interlocked.Increment(ref _generation);
        _requestCancellation?.Cancel();

        State = TodayScreenState.Inactive;
        OnPropertyChanged(nameof(IsActive));
        RefreshCommand.RaiseCanExecuteChanged();
        OpenItemCommand.RaiseCanExecuteChanged();
    }

    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        if (_disposed)
        {
            return;
        }

        var updatedCapabilities = capabilities?.ToArray() ?? [];
        var taskAccessChanged = CanReadTasks != updatedCapabilities.Contains("Task.Read");
        var calendarAccessChanged = CanRead != updatedCapabilities.Contains("Calendar.Read");
        _capabilities.Clear();
        foreach (var capability in updatedCapabilities)
        {
            _capabilities.Add(capability);
        }

        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanReadTasks));
        if (!CanReadTasks) ClearTaskItems();
        if (!CanRead) { TimedItems = []; UntimedItems = []; }
        RefreshCommand.RaiseCanExecuteChanged();
        OpenItemCommand.RaiseCanExecuteChanged();

        if (!CanAccess)
        {
            Interlocked.Increment(ref _generation);
            _requestCancellation?.Cancel();
            ClearProtectedData();

            State = TodayScreenState.Forbidden;
            Message = "Право Calendar.Read отозвано. Данные экрана «Сегодня» очищены.";
            return;
        }

        if (taskAccessChanged || calendarAccessChanged)
        {
            Interlocked.Increment(ref _generation);
            _requestCancellation?.Cancel();
        }

        if (_active
            && _sessionAvailable
            && (taskAccessChanged || calendarAccessChanged || State == TodayScreenState.Forbidden))
        {
            _ = LoadAsync(refresh: false, CancellationToken.None);
        }
    }

    public void UpdateSessionState(bool sessionAvailable)
    {
        if (_disposed)
        {
            return;
        }

        var changed = _sessionAvailable != sessionAvailable;
        _sessionAvailable = sessionAvailable;
        RefreshCommand.RaiseCanExecuteChanged();
        OpenItemCommand.RaiseCanExecuteChanged();

        if (!sessionAvailable)
        {
            Interlocked.Increment(ref _generation);
            _requestCancellation?.Cancel();
            ClearProtectedData();

            State = TodayScreenState.SessionEnded;
            Message = "Сессия завершена. Войдите снова.";
            return;
        }

        if (changed
            && _active
            && CanAccess
            && State == TodayScreenState.SessionEnded)
        {
            _ = LoadAsync(refresh: false, CancellationToken.None);
        }
    }

    private async global::System.Threading.Tasks.Task LoadAsync(
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (_disposed || !_active || !_sessionAvailable || !CanAccess)
        {
            return;
        }

        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _requestCancellation.Token;
        var generation = Interlocked.Increment(ref _generation);

        var requestedToday = ResolveToday();
        var dateChanged = requestedToday != _today;

        if (dateChanged)
        {
            _today = requestedToday;
            OnPropertyChanged(nameof(Today));
            OnPropertyChanged(nameof(DateText));
        }

        var hadConfirmedData = !dateChanged && HasItems;

        if (!refresh || dateChanged)
        {
            ClearItems();
        }

        State = refresh && hadConfirmedData
            ? TodayScreenState.Refreshing
            : TodayScreenState.Loading;

        Message = refresh && hadConfirmedData
            ? "Обновляем сегодняшний план. Последние подтверждённые данные остаются видимыми."
            : "Загрузка сегодняшнего расписания…";

        try
        {
            await LoadTaskItemsAsync(generation, token).ConfigureAwait(true);
            if (!_active || token.IsCancellationRequested || generation != _generation) return;

            var (fromUtc, toUtc) = CalendarViewModel.GetUtcRange(
                _today,
                _today.AddDays(1),
                _timeZone);

            var result = CanRead
                ? await _client.GetScheduleAsync(fromUtc, toUtc, _timeZone.Id, token).ConfigureAwait(true)
                : new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new DesktopSchedulePage([], fromUtc, toUtc));

            if (!_active
                || token.IsCancellationRequested
                || generation != _generation)
            {
                return;
            }

            if (result is DesktopCalendarResult<DesktopSchedulePage>.Succeeded succeeded)
            {
                ApplySchedule(succeeded.Value);

                if (TasksMessage is null) _lastSuccessfulRefresh = _clock();
                OnPropertyChanged(nameof(LastSuccessfulRefreshText));

                if (TasksMessage is not null)
                {
                    State = TodayScreenState.Error;
                    Message = null;
                }
                else if (!HasItems)
                {
                    State = TodayScreenState.Empty;
                    Message = "На сегодня в расписании нет записей.";
                }
                else
                {
                    State = TodayScreenState.Loaded;
                    Message = null;
                }

                return;
            }

            HandleFailure(result, hadConfirmedData);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (InvalidTimeZoneException)
        {
            if (generation == _generation && _active)
            {
                State = TodayScreenState.Error;
                Message = "Не удалось определить корректные границы текущего дня.";
            }
        }
        catch (ArgumentException)
        {
            if (generation == _generation && _active)
            {
                State = TodayScreenState.Error;
                Message = "Не удалось определить корректные границы текущего дня.";
            }
        }
    }

    private void ApplySchedule(DesktopSchedulePage page)
    {
        var items = page.Items
            .Select(item => new CalendarItemViewModel(item, null, _timeZone))
            .Where(item => item.AppearsOn(_today, _timeZone))
            .ToArray();

        TimedItems = items
            .Where(item => !item.Source.IsAllDay
                && item.Source.StartAtUtc.HasValue)
            .OrderBy(item => item.Source.StartAtUtc)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();

        UntimedItems = items
            .Where(item => item.Source.IsAllDay
                || !item.Source.StartAtUtc.HasValue)
            .OrderBy(item => item.Source.IsAllDay ? 0 : 1)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();
    }

    private void HandleFailure(
        DesktopCalendarResult<DesktopSchedulePage> result,
        bool hadConfirmedData)
    {
        switch (result)
        {
            case DesktopCalendarResult<DesktopSchedulePage>.Forbidden:
                TimedItems = [];
                UntimedItems = [];
                State = TodayScreenState.Forbidden;
                Message = "Нет права Calendar.Read для просмотра расписания.";
                break;

            case DesktopCalendarResult<DesktopSchedulePage>.AuthenticationFailure:
                UpdateSessionState(false);
                State = TodayScreenState.SessionEnded;
                Message = "Сессия завершена. Войдите снова.";
                break;

            case DesktopCalendarResult<DesktopSchedulePage>.ValidationFailure validation:
                State = TodayScreenState.Error;
                Message = validation.Message;
                break;

            case DesktopCalendarResult<DesktopSchedulePage>.MalformedResponse:
                State = TodayScreenState.Error;
                Message = hadConfirmedData
                    ? "Не удалось обновить сегодняшний план. Показаны последние подтверждённые данные."
                    : "Сервер вернул некорректное расписание.";
                break;

            default:
                State = TodayScreenState.Error;
                Message = hadConfirmedData
                    ? "Не удалось обновить сегодняшний план. Показаны последние подтверждённые данные."
                    : "Сервер расписания временно недоступен.";
                break;
        }
    }

    private DateOnly ResolveToday()
    {
        var localNow = TimeZoneInfo.ConvertTime(_clock(), _timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private void ClearProtectedData() => ClearItems();

    private void ClearItems()
    {
        TimedItems = Array.Empty<CalendarItemViewModel>();
        UntimedItems = Array.Empty<CalendarItemViewModel>();
        ClearTaskItems();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Deactivate();
        _disposed = true;

        _requestCancellation?.Dispose();
        RefreshCommand.Dispose();
        OpenItemCommand.Dispose();
    }
}
