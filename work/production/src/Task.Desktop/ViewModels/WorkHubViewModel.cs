using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public enum WorkHubArea { Catalog, Contacts, Search, Notifications }

public sealed class WorkHubViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopWorkApiClient _client;
    private readonly IFileAccessAdapter _files;
    private readonly HashSet<string> _capabilities;
    private CancellationTokenSource? _activation;
    private WorkHubArea _area;
    private IReadOnlyList<DesktopCatalogItem> _catalog = [];
    private IReadOnlyList<DesktopContact> _contacts = [];
    private IReadOnlyList<DesktopSearchResult> _searchResults = [];
    private IReadOnlyList<DesktopNotification> _notifications = [];
    private DesktopCatalogItem? _selectedCatalogItem;
    private DesktopNotification? _selectedNotification;
    private string _searchQuery = string.Empty;
    private string _newItemName = string.Empty;
    private string _newItemPath = string.Empty;
    private string _newContactFirstName = string.Empty;
    private string _newContactLastName = string.Empty;
    private string _newContactDisplayName = string.Empty;
    private string? _message;
    private DateTimeOffset? _lastRefresh;
    private bool _sessionAvailable = true;
    private bool _active;
    private bool _disposed;

    public WorkHubViewModel(IDesktopWorkApiClient client, IEnumerable<string>? capabilities, IFileAccessAdapter? files = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _files = files ?? new WindowsFileAccessAdapter();
        _capabilities = new(capabilities ?? [], StringComparer.OrdinalIgnoreCase);
        RefreshCommand = new(RefreshAsync, _ => _active && _sessionAvailable && CanReadCurrentArea);
        SearchCommand = new(SearchAsync, _ => _active && _sessionAvailable && CanSearch && SearchQuery.Trim().Length is >= 2 and <= 200);
        CreateCatalogItemCommand = new(CreateCatalogItemAsync, _ => _active && _sessionAvailable && CanCreateCatalog && !string.IsNullOrWhiteSpace(NewItemName));
        AddLocationCommand = new(AddLocationAsync, _ => _active && _sessionAvailable && CanUpdateLocation && SelectedCatalogItem is not null && !string.IsNullOrWhiteSpace(NewItemPath));
        OpenFileCommand = new(OpenFileAsync, _ => _active && _sessionAvailable && CanOpenFile && SelectedCatalogItem?.ItemType is "file_reference" or "folder_reference");
        CreateContactCommand = new(CreateContactAsync, _ => _active && _sessionAvailable && CanCreateContact && !string.IsNullOrWhiteSpace(NewContactFirstName) && !string.IsNullOrWhiteSpace(NewContactDisplayName));
        MarkReadCommand = new(MarkReadAsync, p => _active && _sessionAvailable && CanReadNotifications && (p as DesktopNotification ?? SelectedNotification) is { Status: not "read" and not "dismissed" });
        MarkAllReadCommand = new(MarkAllReadAsync, _ => _active && _sessionAvailable && CanReadNotifications && Notifications.Any(n => n.Status is not ("read" or "dismissed")));
        OpenNotificationSourceCommand = new(OpenNotificationSourceAsync, p => p is DesktopNotification { SourceObjectId: not null });
        OpenSearchResultCommand = new(OpenSearchResultAsync, p => p is DesktopSearchResult);
        foreach (var command in Commands) command.ExecutionFailed += OnUnexpectedFailure;
    }

    public event Action<string, Guid>? OpenObjectRequested;
    public IReadOnlyList<DesktopCatalogItem> Catalog { get => _catalog; private set => SetProperty(ref _catalog, value); }
    public IReadOnlyList<DesktopContact> Contacts { get => _contacts; private set => SetProperty(ref _contacts, value); }
    public IReadOnlyList<DesktopSearchResult> SearchResults { get => _searchResults; private set => SetProperty(ref _searchResults, value); }
    public IReadOnlyList<DesktopNotification> Notifications { get => _notifications; private set { if (SetProperty(ref _notifications, value)) { OnPropertyChanged(nameof(UnreadCount)); MarkAllReadCommand.RaiseCanExecuteChanged(); } } }
    public int UnreadCount => Notifications.Count(n => n.Status is not ("read" or "dismissed"));
    public DesktopCatalogItem? SelectedCatalogItem { get => _selectedCatalogItem; set { if (SetProperty(ref _selectedCatalogItem, value)) NotifyCommands(); } }
    public DesktopNotification? SelectedNotification { get => _selectedNotification; set { if (SetProperty(ref _selectedNotification, value)) MarkReadCommand.RaiseCanExecuteChanged(); } }
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) SearchCommand.RaiseCanExecuteChanged(); } }
    public string NewItemName { get => _newItemName; set { if (SetProperty(ref _newItemName, value)) CreateCatalogItemCommand.RaiseCanExecuteChanged(); } }
    public string NewItemPath { get => _newItemPath; set { if (SetProperty(ref _newItemPath, value)) AddLocationCommand.RaiseCanExecuteChanged(); } }
    public string NewContactFirstName { get => _newContactFirstName; set { if (SetProperty(ref _newContactFirstName, value)) CreateContactCommand.RaiseCanExecuteChanged(); } }
    public string NewContactLastName { get => _newContactLastName; set => SetProperty(ref _newContactLastName, value); }
    public string NewContactDisplayName { get => _newContactDisplayName; set { if (SetProperty(ref _newContactDisplayName, value)) CreateContactCommand.RaiseCanExecuteChanged(); } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public WorkHubArea Area { get => _area; private set { if (!SetProperty(ref _area, value)) return; OnPropertyChanged(nameof(IsCatalog)); OnPropertyChanged(nameof(IsContacts)); OnPropertyChanged(nameof(IsSearch)); OnPropertyChanged(nameof(IsNotifications)); OnPropertyChanged(nameof(AccessText)); NotifyCommands(); } }
    public bool IsCatalog => Area == WorkHubArea.Catalog;
    public bool IsContacts => Area == WorkHubArea.Contacts;
    public bool IsSearch => Area == WorkHubArea.Search;
    public bool IsNotifications => Area == WorkHubArea.Notifications;
    public bool CanReadCurrentArea => Area switch { WorkHubArea.Catalog => Has("FileCatalog.Read"), WorkHubArea.Contacts => Has("Contact.Read"), WorkHubArea.Search => CanSearch, _ => CanReadNotifications };
    public bool CanSearch => Has("Search.Use");
    public bool CanCreateCatalog => Has("FileCatalog.Create");
    public bool CanUpdateLocation => Has("FileLocation.Update");
    public bool CanOpenFile => Has("FileReference.Open");
    public bool CanCreateContact => Has("Contact.Create");
    public bool CanReadNotifications => Has("Notification.ReadOwn");
    public string AccessText => CanReadCurrentArea ? "Данные сервера компании · доступ по текущим правам" : "Нет права на просмотр этого раздела";
    public string LastSuccessfulRefreshText => _lastRefresh is null ? "Раздел ещё не обновлялся" : $"Обновлено {_lastRefresh.Value.ToLocalTime():HH:mm}";
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand CreateCatalogItemCommand { get; }
    public AsyncCommand AddLocationCommand { get; }
    public AsyncCommand OpenFileCommand { get; }
    public AsyncCommand CreateContactCommand { get; }
    public AsyncCommand MarkReadCommand { get; }
    public AsyncCommand MarkAllReadCommand { get; }
    public AsyncCommand OpenNotificationSourceCommand { get; }
    public AsyncCommand OpenSearchResultCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, SearchCommand, CreateCatalogItemCommand, AddLocationCommand, OpenFileCommand, CreateContactCommand, MarkReadCommand, MarkAllReadCommand, OpenNotificationSourceCommand, OpenSearchResultCommand];

    public void Activate(WorkHubArea area)
    {
        Area = area; _active = true; NotifyCommands();
        _activation?.Cancel(); _activation?.Dispose(); _activation = new();
        _ = RefreshCommand.ExecuteAsync(null, _activation.Token);
    }

    public void Deactivate() { _active = false; _activation?.Cancel(); NotifyCommands(); }
    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        _capabilities.Clear();
        foreach (var capability in capabilities ?? []) _capabilities.Add(capability);
        NotifyCommands();
    }
    public void UpdateSessionState(bool available) { _sessionAvailable = available; if (!available) { Message = "Сессия завершена. Выполните вход снова."; _activation?.Cancel(); } NotifyCommands(); }

    private async System.Threading.Tasks.Task RefreshAsync(object? _, CancellationToken ct)
    {
        Message = null;
        switch (Area)
        {
            case WorkHubArea.Catalog: Apply(await _client.GetCatalogAsync(ct), value => { Catalog = value; SelectedCatalogItem = value.FirstOrDefault(); }); break;
            case WorkHubArea.Contacts: Apply(await _client.GetContactsAsync(ct), value => Contacts = value); break;
            case WorkHubArea.Notifications: Apply(await _client.GetNotificationsAsync(ct), value => { Notifications = value; SelectedNotification = value.FirstOrDefault(); }); break;
            case WorkHubArea.Search when SearchQuery.Trim().Length >= 2: await SearchAsync(null, ct); return;
            case WorkHubArea.Search: SearchResults = []; Message = "Введите запрос длиной не менее двух символов."; return;
        }
    }

    private async System.Threading.Tasks.Task SearchAsync(object? _, CancellationToken ct) => Apply(await _client.SearchAsync(SearchQuery, ct), value => SearchResults = value);
    private async System.Threading.Tasks.Task CreateCatalogItemAsync(object? _, CancellationToken ct)
    {
        var result = await _client.CreateCatalogItemAsync(NewItemName, "file_reference", null, ct);
        Apply(result, value => { Catalog = [value, .. Catalog]; SelectedCatalogItem = value; NewItemName = string.Empty; Message = "Запись файла создана. Теперь добавьте путь."; });
    }
    private async System.Threading.Tasks.Task AddLocationAsync(object? _, CancellationToken ct)
    {
        var item = SelectedCatalogItem; if (item is null) return;
        Apply(await _client.AddLocationAsync(item.Id, item.Version, NewItemPath, ct), _ => { NewItemPath = string.Empty; Message = "Расположение файла сохранено."; });
    }
    private async System.Threading.Tasks.Task OpenFileAsync(object? _, CancellationToken ct)
    {
        var item = SelectedCatalogItem; if (item is null) return;
        Apply(await _client.ResolveLocationAsync(item.Id, ct), location =>
        {
            if (location is null || !location.CanOpenOnDevice) { Message = "На этом компьютере нет доступного расположения файла."; return; }
            Message = _files.Open(location.RawPath).Message;
        });
    }
    private async System.Threading.Tasks.Task CreateContactAsync(object? _, CancellationToken ct)
    {
        Apply(await _client.CreateContactAsync(NewContactFirstName, NewContactLastName, NewContactDisplayName, ct), value =>
        { Contacts = [value, .. Contacts]; NewContactFirstName = NewContactLastName = NewContactDisplayName = string.Empty; Message = "Контакт создан и доступен для связей с работой."; });
    }
    private async System.Threading.Tasks.Task MarkReadAsync(object? parameter, CancellationToken ct)
    {
        var item = parameter as DesktopNotification ?? SelectedNotification; if (item is null) return;
        Apply(await _client.MarkNotificationReadAsync(item.Id, ct), _ => { Notifications = Notifications.Select(n => n.Id == item.Id ? n with { Status = "read" } : n).ToArray(); SelectedNotification = Notifications.First(n => n.Id == item.Id); Message = "Уведомление отмечено прочитанным."; });
    }
    private async System.Threading.Tasks.Task MarkAllReadAsync(object? _, CancellationToken ct)
    {
        var ids = Notifications.Where(n => n.Status is not ("read" or "dismissed")).Take(500).Select(n => n.Id).ToArray();
        Apply(await _client.MarkAllNotificationsReadAsync(ids, ct), _ => { var selected = ids.ToHashSet(); Notifications = Notifications.Select(n => selected.Contains(n.Id) ? n with { Status = "read" } : n).ToArray(); Message = ids.Length == 500 && UnreadCount > 0 ? "Отмечены первые 500 уведомлений. Повторите для оставшихся." : "Все уведомления отмечены прочитанными."; });
    }
    private System.Threading.Tasks.Task OpenSearchResultAsync(object? parameter, CancellationToken _)
    {
        if (parameter is DesktopSearchResult result) OpenObjectRequested?.Invoke(result.ObjectType, result.ParentObjectId ?? result.ObjectId);
        return System.Threading.Tasks.Task.CompletedTask;
    }
    private System.Threading.Tasks.Task OpenNotificationSourceAsync(object? parameter, CancellationToken _)
    {
        if (parameter is DesktopNotification { SourceObjectId: { } id } notification)
        {
            var type = notification.NotificationType.Split('.', 2)[0] switch
            {
                "file" or "catalog" => "catalog_item",
                "calendar" => "calendar_event",
                var value => value,
            };
            OpenObjectRequested?.Invoke(type, id);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void Apply<T>(DesktopWorkResult<T> result, Action<T> success)
    {
        switch (result)
        {
            case DesktopWorkResult<T>.Succeeded ok: success(ok.Value); _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText)); break;
            case DesktopWorkResult<T>.Forbidden: Message = "Недостаточно прав или доступ к объекту изменился."; break;
            case DesktopWorkResult<T>.AuthenticationFailure: Message = "Сессия завершена. Выполните вход снова."; break;
            case DesktopWorkResult<T>.ValidationFailure invalid: Message = invalid.Message; break;
            case DesktopWorkResult<T>.NotFound: Message = "Объект больше не доступен."; break;
            case DesktopWorkResult<T>.Conflict: Message = "Данные изменились. Обновите раздел и повторите действие."; break;
            case DesktopWorkResult<T>.ServerUnavailable: Message = "Сервер временно недоступен."; break;
            default: Message = "Сервер вернул неподтверждённые данные."; break;
        }
    }

    private bool Has(string capability) => _capabilities.Contains(capability);
    private void NotifyCommands() { OnPropertyChanged(nameof(CanReadCurrentArea)); OnPropertyChanged(nameof(AccessText)); foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
    private void OnUnexpectedFailure(Exception _) => Message = "Не удалось завершить действие.";
    public void Dispose() { if (_disposed) return; _disposed = true; _activation?.Cancel(); _activation?.Dispose(); foreach (var command in Commands) command.Dispose(); }
}
