using Task.Desktop.Administration;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public enum EmployeePickerState { Idle, Loading, Ready, Empty, Forbidden, Offline, Unavailable, AuthenticationRequired, NotFound, InvalidSelection, Error }

/// <summary>UI-thread owned, form-local picker. Dispose with its form; never persist directory data.</summary>
public sealed class EmployeePickerViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopEmployeeDirectory _directory;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _request;
    private long _generation;
    private bool _disposed, _canRead, _online = true, _sessionReady = true;
    private string _searchText = "";
    private string? _cursor;
    private readonly HashSet<string> _visitedCursors = [];
    private IReadOnlyList<DesktopAdminUser> _results = [];
    private DesktopAdminUser? _candidate, _selected;
    private EmployeePickerState _state;
    private Guid? _retryId;
    private bool _retryMore;

    public EmployeePickerViewModel(IDesktopEmployeeDirectory directory, IEnumerable<string>? capabilities,
        bool activeOnly = true, bool allowClear = true, TimeSpan? debounce = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        if (_debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        ActiveOnly = activeOnly; AllowClear = allowClear;
        SearchCommand = new((_, ct) => SearchAsync(ct), _ => CanQuery);
        LoadMoreCommand = new((_, ct) => LoadMoreAsync(ct), _ => CanQuery && HasMore && !IsLoading);
        RetryCommand = new((_, ct) => _retryId is { } id ? PreselectAsync(id, ct) : _retryMore ? LoadMoreAsync(ct) : SearchAsync(ct),
            _ => CanQuery && !IsLoading);
        ConfirmCommand = new((_, _) => { ConfirmSelection(); return AsyncTask.CompletedTask; }, _ => CanConfirm);
        ClearCommand = new((_, _) => { ClearSelection(); return AsyncTask.CompletedTask; }, _ => AllowClear && SelectedEmployee is not null);
        CancelCommand = new((_, _) => { Cancel(); return AsyncTask.CompletedTask; });
        UpdateAccess(capabilities);
    }

    public bool ActiveOnly { get; }
    public bool AllowClear { get; }
    public bool CanQuery => !_disposed && _canRead && _sessionReady && _online;
    public bool CanConfirm => CanQuery && State == EmployeePickerState.Ready && Candidate is { } user && Results.Contains(user) && IsEligible(user);
    public bool HasMore => _cursor is not null;
    public bool IsLoading => State == EmployeePickerState.Loading;
    public EmployeePickerState State { get => _state; private set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsLoading)); NotifyCommands(); } } }
    public string StatusText => State switch
    {
        EmployeePickerState.Loading => "Поиск сотрудников…",
        EmployeePickerState.Ready => "Выберите сотрудника и подтвердите выбор.",
        EmployeePickerState.Empty => "Сотрудники не найдены.",
        EmployeePickerState.Forbidden => "Нет права на просмотр сотрудников (User.Read).",
        EmployeePickerState.Offline => "Нет подключения к сети. Подключитесь и повторите поиск.",
        EmployeePickerState.Unavailable => "Сервер недоступен. Повторите попытку.",
        EmployeePickerState.AuthenticationRequired => "Требуется вход в систему.",
        EmployeePickerState.NotFound => "Учётная запись не найдена или недоступна.",
        EmployeePickerState.InvalidSelection => "Эту учётную запись нельзя выбрать: требуется активный сотрудник.",
        EmployeePickerState.Error => "Не удалось получить сотрудников. Повторите попытку.",
        _ => "Введите имя сотрудника.",
    };
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) CurrentOperation = RunAsync(false, null, true, default); }
    }
    public IReadOnlyList<DesktopAdminUser> Results { get => _results; private set => SetProperty(ref _results, value); }
    public DesktopAdminUser? Candidate { get => _candidate; set { if (SetProperty(ref _candidate, value)) NotifyCommands(); } }
    public DesktopAdminUser? SelectedEmployee { get => _selected; private set { if (SetProperty(ref _selected, value)) { OnPropertyChanged(nameof(SelectedUserAccountId)); NotifyCommands(); } } }
    public Guid? SelectedUserAccountId => SelectedEmployee?.Id;
    public AsyncTask CurrentOperation { get; private set; } = AsyncTask.CompletedTask;
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand LoadMoreCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand ConfirmCommand { get; }
    public AsyncCommand ClearCommand { get; }
    public AsyncCommand CancelCommand { get; }

    public AsyncTask SearchAsync(CancellationToken cancellationToken = default) => CurrentOperation = RunAsync(false, null, false, cancellationToken);
    public AsyncTask LoadMoreAsync(CancellationToken cancellationToken = default) =>
        !HasMore || IsLoading ? AsyncTask.CompletedTask : CurrentOperation = RunAsync(true, null, false, cancellationToken);
    public AsyncTask PreselectAsync(Guid userAccountId, CancellationToken cancellationToken = default) =>
        CurrentOperation = RunAsync(false, userAccountId, false, cancellationToken);

    public bool ConfirmSelection()
    {
        if (!CanConfirm) return false;
        SelectedEmployee = Candidate;
        return true;
    }

    public void ClearSelection()
    {
        if (!AllowClear || _disposed) return;
        Cancel(); SelectedEmployee = null;
    }

    public void Cancel()
    {
        Invalidate(); Candidate = null;
        if (!_disposed) State = AccessState() ?? EmployeePickerState.Idle;
    }

    /// <summary>Host must forward capability/session/connectivity changes, including logout.</summary>
    public void UpdateAccess(IEnumerable<string>? capabilities, bool sessionReady = true, bool online = true)
    {
        if (_disposed) return;
        Invalidate();
        _canRead = capabilities?.Contains("User.Read", StringComparer.OrdinalIgnoreCase) == true;
        _sessionReady = sessionReady; _online = online;
        ResetResults(); SelectedEmployee = null;
        State = AccessState() ?? EmployeePickerState.Idle;
        OnPropertyChanged(nameof(CanQuery)); NotifyCommands();
    }

    private EmployeePickerState? AccessState() => !_sessionReady ? EmployeePickerState.AuthenticationRequired
        : !_canRead ? EmployeePickerState.Forbidden : !_online ? EmployeePickerState.Offline : null;
    private bool IsEligible(DesktopAdminUser user) => user.Id != Guid.Empty && (user.AccountStatus == "active"
        || !ActiveOnly && user.AccountStatus is "pending_activation" or "blocked" or "deactivated");

    private async AsyncTask RunAsync(bool more, Guid? id, bool delay, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        Invalidate();
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _request = request;
        _retryId = id; _retryMore = more;
        if (!more) ResetResults();
        if (id is not null) SelectedEmployee = null;
        if (AccessState() is { } denied) { State = denied; _request = null; return; }
        State = EmployeePickerState.Loading;
        var filter = SearchText;
        var cursor = more ? _cursor : null;
        try
        {
            if (delay) await AsyncTask.Delay(_debounce, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (id is { } userId)
            {
                var result = await _directory.GetUserAsync(userId, request.Token);
                if (!IsCurrent()) return;
                if (result is DesktopWorkResult<DesktopAdminUser>.Succeeded found)
                {
                    if (found.Value.Id != userId) State = EmployeePickerState.Error;
                    else if (!IsEligible(found.Value)) State = EmployeePickerState.InvalidSelection;
                    else { SelectedEmployee = found.Value; State = EmployeePickerState.Ready; }
                }
                else HandleFailure(result);
            }
            else
            {
                var result = await _directory.SearchUsersAsync(filter, cursor, request.Token);
                if (!IsCurrent()) return;
                if (result is DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded page)
                {
                    if (page.NextCursor is { } next && (next == cursor || _visitedCursors.Contains(next)))
                    { State = EmployeePickerState.Error; return; }
                    if (cursor is not null) _visitedCursors.Add(cursor);
                    Results = (more ? Results : []).Concat(page.Value.Where(IsEligible)).DistinctBy(u => u.Id).ToArray();
                    _cursor = page.NextCursor; OnPropertyChanged(nameof(HasMore));
                    State = Results.Count == 0 ? EmployeePickerState.Empty : EmployeePickerState.Ready;
                }
                else HandleFailure(result);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (generation == _generation && !_disposed) State = EmployeePickerState.Idle;
        }
        catch (Exception)
        {
            // Never expose server messages, query text or personal details through errors/logs.
            if (IsCurrent()) State = EmployeePickerState.Error;
        }
        finally
        {
            if (generation == _generation)
            {
                _request = null;
                if (request.IsCancellationRequested && State == EmployeePickerState.Loading) State = EmployeePickerState.Idle;
                NotifyCommands();
            }
        }
        bool IsCurrent() => !_disposed && generation == _generation && !request.IsCancellationRequested;
    }

    private void HandleFailure<T>(DesktopWorkResult<T> result)
    {
        State = result switch
        {
            DesktopWorkResult<T>.Forbidden => EmployeePickerState.Forbidden,
            DesktopWorkResult<T>.AuthenticationFailure => EmployeePickerState.AuthenticationRequired,
            DesktopWorkResult<T>.NotFound => EmployeePickerState.NotFound,
            DesktopWorkResult<T>.ServerUnavailable => EmployeePickerState.Unavailable,
            _ => EmployeePickerState.Error,
        };
        Candidate = null;
        if (State is EmployeePickerState.Forbidden or EmployeePickerState.AuthenticationRequired)
        { ResetResults(); SelectedEmployee = null; }
    }

    private void ResetResults()
    {
        Results = []; Candidate = null; _cursor = null; _visitedCursors.Clear(); OnPropertyChanged(nameof(HasMore));
    }
    private void Invalidate() { _generation++; _request?.Cancel(); _request = null; }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanConfirm));
        foreach (var command in new[] { SearchCommand, LoadMoreCommand, RetryCommand, ConfirmCommand, ClearCommand, CancelCommand }) command.RaiseCanExecuteChanged();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Invalidate(); ResetResults(); SelectedEmployee = null;
        foreach (var command in new[] { SearchCommand, LoadMoreCommand, RetryCommand, ConfirmCommand, ClearCommand, CancelCommand }) command.Dispose();
    }
}
