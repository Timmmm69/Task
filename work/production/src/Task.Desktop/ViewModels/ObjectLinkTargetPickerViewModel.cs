using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public enum ObjectLinkTargetPickerState { Initial, Loading, Ready, Empty, Selected, Forbidden, Offline, SessionExpired, ServerUnavailable, RequestFailed }

/// <summary>UI-thread owned, Corporate form-local picker. Host forwards every scope/session/network change.</summary>
public sealed class ObjectLinkTargetPickerViewModel : ViewModelBase, IDisposable
{
    public const int MaximumResults = 500;
    public const int MaximumPages = 20;
    private readonly IDesktopObjectLinkTargetSearchClient _client;
    private readonly TimeSpan _debounce;
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);
    private HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _request;
    private long _generation;
    private int _pages;
    private bool _disposed, _online = true, _sessionReady = true;
    private Guid _source;
    private string _sourceType = "", _linkType = "", _searchText = "";
    private string? _cursor;
    private IReadOnlyList<DesktopSearchResult> _results = [];
    private DesktopSearchResult? _candidate, _selected;
    private ObjectLinkTargetPickerState _state;

    public ObjectLinkTargetPickerViewModel(IDesktopObjectLinkTargetSearchClient client,
        IEnumerable<string>? capabilities, TimeSpan? debounce = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        if (_debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        SearchCommand = new((_, ct) => SearchAsync(ct), _ => CanQuery && ValidQuery);
        LoadMoreCommand = new((_, ct) => LoadMoreAsync(ct), _ => CanQuery && HasMore && !IsLoading);
        RetryCommand = new((_, ct) => SearchAsync(ct), _ => CanQuery && ValidQuery && !IsLoading);
        ConfirmCommand = new((_, _) => { ConfirmSelection(); return AsyncTask.CompletedTask; }, _ => CanConfirm);
        ClearCommand = new((_, _) => { ClearSelection(); return AsyncTask.CompletedTask; }, _ => !_disposed);
        UpdateAccess(capabilities);
    }

    public IReadOnlyList<string> AllowedTargetTypes => (_sourceType, _linkType) switch
    {
        ("task", "task_file") or ("project", "project_file") or ("contact" or "company", "contact_file") => ["catalog_item"],
        ("task", "task_contact") => ["contact", "company"],
        ("project", "project_contact") => ["contact", "company"],
        ("task", "task_project") => ["project"],
        ("task" or "project" or "contact" or "company" or "catalog_item" or "calendar_event" or "interaction", "related")
            => new[] { "task", "project", "contact", "company", "catalog_item", "calendar_event", "interaction", "employee_profile", "network_resource", "notification", "inbox_item" }
                .Where(CanReadType).ToArray(),
        _ => [],
    };
    private bool Has(string permission) => _capabilities.Contains(permission);
    private bool CanReadType(string type) => type switch
    {
        "task" => Has("Task.Read"),
        "project" => Has("Project.Read"),
        "contact" or "company" or "interaction" => Has("Contact.Read"),
        "catalog_item" or "network_resource" => Has("FileCatalog.Read"),
        "calendar_event" => Has("Calendar.Read") || Has("Task.Read"),
        "notification" => Has("Notification.ReadOwn"),
        "inbox_item" => Has("Inbox.ReadOwn"),
        "employee_profile" => Has("Employee.Read"),
        _ => false,
    };
    private bool HasAccess => Has("ObjectLink.Create") && _source != Guid.Empty
        && (_sourceType switch
        {
            "task" => Has("Task.Read"),
            "project" => Has("Project.Read") && Has("Project.Update"),
            "contact" or "company" => Has("Contact.Read") && Has("Contact.Update"),
            "catalog_item" => Has("FileCatalog.Read") && Has("FileCatalog.Update"),
            "calendar_event" => CanReadType("calendar_event") && Has("CalendarEvent.Update"),
            "interaction" => Has("Contact.Read") && Has("Interaction.Update"),
            _ => false,
        })
        && AllowedTargetTypes.Count > 0
        && AllowedTargetTypes.All(CanReadType);
    private bool ValidQuery => SearchText.Trim().Length is >= 2 and <= 200;
    public bool CanQuery => !_disposed && HasAccess && _sessionReady && _online;
    public bool CanConfirm => CanQuery && State is ObjectLinkTargetPickerState.Ready or ObjectLinkTargetPickerState.Selected
        && Candidate is { } hit && Results.Any(row => ReferenceEquals(row, hit)) && Eligible(hit);
    public bool HasMore => _cursor is not null && _pages < MaximumPages && Results.Count < MaximumResults;
    public bool IsLoading => State == ObjectLinkTargetPickerState.Loading;
    public bool LimitReached => _pages >= MaximumPages || Results.Count >= MaximumResults;
    public ObjectLinkTargetPickerState State
    {
        get => _state;
        private set { SetProperty(ref _state, value); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsLoading)); NotifyCommands(); }
    }
    public string StatusText => State switch
    {
        ObjectLinkTargetPickerState.Loading => "Поиск объектов…",
        ObjectLinkTargetPickerState.Ready or ObjectLinkTargetPickerState.Empty when LimitReached => "Достигнут предел результатов. Уточните название.",
        ObjectLinkTargetPickerState.Ready => "Выберите объект и подтвердите выбор.",
        ObjectLinkTargetPickerState.Empty => "Объекты не найдены.",
        ObjectLinkTargetPickerState.Selected => "Объект выбран.",
        ObjectLinkTargetPickerState.Forbidden => "Выбор недоступен: нужны права создания связей, чтения объектов и изменения источника.",
        ObjectLinkTargetPickerState.Offline => "Нет подключения к сети. Подключитесь и повторите поиск.",
        ObjectLinkTargetPickerState.SessionExpired => "Сессия завершена. Войдите в систему.",
        ObjectLinkTargetPickerState.ServerUnavailable => "Сервер недоступен. Повторите попытку.",
        ObjectLinkTargetPickerState.RequestFailed => "Не удалось получить результаты. Повторите поиск.",
        _ => "Введите от 2 до 200 символов названия объекта.",
    };
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) CurrentOperation = RunAsync(false, true, default); }
    }
    public IReadOnlyList<DesktopSearchResult> Results { get => _results; private set => SetProperty(ref _results, value); }
    public DesktopSearchResult? Candidate
    {
        get => _candidate;
        set { if (ReferenceEquals(_candidate, value)) return; _candidate = value; OnPropertyChanged(); NotifyCommands(); }
    }
    public DesktopSearchResult? SelectedTarget
    {
        get => _selected;
        private set { SetProperty(ref _selected, value); OnPropertyChanged(nameof(SelectedObjectId)); NotifyCommands(); }
    }
    public Guid? SelectedObjectId => SelectedTarget?.ObjectId;
    public AsyncTask CurrentOperation { get; private set; } = AsyncTask.CompletedTask;
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand LoadMoreCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand ConfirmCommand { get; }
    public AsyncCommand ClearCommand { get; }

    /// <summary>Source/type changes invalidate both confirmed selection and in-flight search.</summary>
    public void SetContext(Guid source, string sourceType, string linkType)
    {
        if (_disposed || (_source, _sourceType, _linkType) == (source, sourceType, linkType)) return;
        Invalidate(); Reset(); _source = source; _sourceType = sourceType; _linkType = linkType;
        OnPropertyChanged(nameof(AllowedTargetTypes)); OnPropertyChanged(nameof(CanQuery));
        State = AccessState() ?? ObjectLinkTargetPickerState.Initial;
    }
    /// <summary>Call even when capability names are unchanged (object-level scope may have changed).</summary>
    public void UpdateAccess(IEnumerable<string>? capabilities, bool sessionReady = true, bool online = true)
    {
        if (_disposed) return;
        Invalidate(); Reset(); _capabilities = new(capabilities ?? [], StringComparer.OrdinalIgnoreCase);
        _sessionReady = sessionReady; _online = online;
        OnPropertyChanged(nameof(CanQuery)); State = AccessState() ?? ObjectLinkTargetPickerState.Initial;
    }
    public bool ConfirmSelection()
    {
        if (!CanConfirm) return false;
        SelectedTarget = Candidate; State = ObjectLinkTargetPickerState.Selected; return true;
    }
    public void ClearSelection() => Cancel();
    public void Cancel()
    {
        if (_disposed) return;
        Invalidate(); Reset(); State = AccessState() ?? ObjectLinkTargetPickerState.Initial;
    }
    public AsyncTask SearchAsync(CancellationToken ct = default) => CurrentOperation = RunAsync(false, false, ct);
    public AsyncTask LoadMoreAsync(CancellationToken ct = default) => !CanQuery || !HasMore || IsLoading
        ? AsyncTask.CompletedTask : CurrentOperation = RunAsync(true, false, ct);

    private ObjectLinkTargetPickerState? AccessState() => !_sessionReady ? ObjectLinkTargetPickerState.SessionExpired
        : !HasAccess ? ObjectLinkTargetPickerState.Forbidden : !_online ? ObjectLinkTargetPickerState.Offline : null;
    private bool Eligible(DesktopSearchResult hit) => hit.ObjectId != Guid.Empty && hit.ObjectId != _source
        && AllowedTargetTypes.Contains(hit.ObjectType) && !string.IsNullOrWhiteSpace(hit.Title)
        && hit.Title.Length <= 500 && hit.Version > 0 && hit.UpdatedAt != default;

    private async AsyncTask RunAsync(bool more, bool delay, CancellationToken ct)
    {
        if (_disposed) return;
        Invalidate(); var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _request = request;
        if (!more) Reset();
        else { Candidate = null; SelectedTarget = null; }
        var query = SearchText.Trim(); var cursor = more ? _cursor : null;
        var types = AllowedTargetTypes;
        try
        {
            if (AccessState() is { } denied) { State = denied; return; }
            if (!ValidQuery) { State = ObjectLinkTargetPickerState.Initial; return; }
            State = ObjectLinkTargetPickerState.Loading;
            if (delay) await AsyncTask.Delay(_debounce, request.Token);
            request.Token.ThrowIfCancellationRequested();
            var result = await _client.SearchTargetsAsync(query, types, cursor, request.Token);
            if (!Current()) return;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded page)
            {
                // Fail the entire page closed; never publish a partially malformed response.
                if (page.Value is null || page.Value.Count > 50
                    || page.Value.Any(hit => hit is null || hit.ObjectId == Guid.Empty || !types.Contains(hit.ObjectType)
                        || string.IsNullOrWhiteSpace(hit.Title) || hit.Title.Length > 500 || hit.Version < 1 || hit.UpdatedAt == default)
                    || page.NextCursor is { } next && (string.IsNullOrWhiteSpace(next) || next.Length > 256 || next == cursor || _cursors.Contains(next)))
                { Fail(ObjectLinkTargetPickerState.RequestFailed); return; }
                _pages++;
                if (cursor is not null) _cursors.Add(cursor);
                Results = Results.Concat(page.Value.Where(Eligible)).DistinctBy(hit => hit.ObjectId).Take(MaximumResults).ToArray();
                _cursor = page.NextCursor;
                OnPropertyChanged(nameof(HasMore)); OnPropertyChanged(nameof(LimitReached));
                State = Results.Count == 0 ? ObjectLinkTargetPickerState.Empty : ObjectLinkTargetPickerState.Ready;
            }
            else Fail(result switch
            {
                DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Forbidden => ObjectLinkTargetPickerState.Forbidden,
                DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.AuthenticationFailure => ObjectLinkTargetPickerState.SessionExpired,
                DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ServerUnavailable => ObjectLinkTargetPickerState.ServerUnavailable,
                _ => ObjectLinkTargetPickerState.RequestFailed,
            });
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current()) Fail(ObjectLinkTargetPickerState.RequestFailed); }
        finally
        {
            if (generation == _generation)
            {
                _request = null;
                if (request.IsCancellationRequested) { Reset(); State = AccessState() ?? ObjectLinkTargetPickerState.Initial; }
                NotifyCommands();
            }
        }
        bool Current() => !_disposed && generation == _generation && !request.IsCancellationRequested;
    }
    private void Fail(ObjectLinkTargetPickerState state)
    {
        Reset();
        if (state == ObjectLinkTargetPickerState.SessionExpired) _sessionReady = false;
        OnPropertyChanged(nameof(CanQuery)); State = state;
    }
    private void Invalidate() { _generation++; _request?.Cancel(); _request = null; }
    private void Reset()
    {
        Results = []; Candidate = null; SelectedTarget = null; _cursor = null; _cursors.Clear(); _pages = 0;
        OnPropertyChanged(nameof(HasMore)); OnPropertyChanged(nameof(LimitReached));
    }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanConfirm));
        foreach (var command in new[] { SearchCommand, LoadMoreCommand, RetryCommand, ConfirmCommand, ClearCommand }) command.RaiseCanExecuteChanged();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Invalidate(); Reset(); OnPropertyChanged(nameof(CanQuery));
        foreach (var command in new[] { SearchCommand, LoadMoreCommand, RetryCommand, ConfirmCommand, ClearCommand }) command.Dispose();
    }
}
