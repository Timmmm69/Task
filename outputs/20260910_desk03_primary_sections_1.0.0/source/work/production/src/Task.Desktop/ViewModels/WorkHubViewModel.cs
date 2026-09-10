using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public enum WorkHubArea { Catalog, Contacts, Search, Notifications, Archive, Trash, Settings }

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
    private IReadOnlyList<DesktopLifecycleItem> _lifecycleItems = [];
    private DesktopCatalogItem? _selectedCatalogItem;
    private DesktopNotification? _selectedNotification;
    private DesktopLifecycleItem? _selectedLifecycleItem;
    private DesktopUserSettings? _userSettings;
    private DesktopNotificationPreferences? _notificationPreferences;
    private DesktopOrganizationSettings? _organizationSettings;
    private string _searchQuery = string.Empty;
    private string _newItemName = string.Empty;
    private string _newItemPath = string.Empty;
    private string _newContactFirstName = string.Empty;
    private string _newContactLastName = string.Empty;
    private string _newContactDisplayName = string.Empty;
    private string _userLanguage = "ru-RU";
    private string _userTimeFormat = "24h";
    private string _userWorkdayStart = "09:00:00";
    private string _userWorkdayEnd = "18:00:00";
    private int _defaultTaskDurationMinutes = 60;
    private int _defaultReminderOffsetMinutes = 15;
    private bool _autostartEnabled = true;
    private bool _allowLocalPaths = true;
    private bool _confirmCatalogDelete = true;
    private string _missingFileBehavior = "show_actions";
    private bool _notificationsEnabled = true;
    private bool _desktopNotificationsEnabled = true;
    private bool _notificationSoundEnabled = true;
    private int _defaultSnoozeMinutes = 15;
    private int _trashRetentionDays = 30;
    private int _historyRetentionDays = 1095;
    private int _changeFeedRetentionDays = 90;
    private int _recurrenceHorizonDays = 90;
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
        RestoreLifecycleItemCommand = new(RestoreLifecycleItemAsync, p => _active && _sessionAvailable && (p as DesktopLifecycleItem ?? SelectedLifecycleItem) is not null && (IsArchive ? CanRestoreArchive : IsTrash && CanRestoreTrash));
        SaveUserSettingsCommand = new(SaveUserSettingsAsync, _ => _active && _sessionAvailable && IsSettings && CanUpdateSettings && _userSettings is not null);
        SaveNotificationPreferencesCommand = new(SaveNotificationPreferencesAsync, _ => _active && _sessionAvailable && IsSettings && CanUpdateSettings && _notificationPreferences is not null);
        SaveOrganizationSettingsCommand = new(SaveOrganizationSettingsAsync, _ => _active && _sessionAvailable && IsSettings && CanUpdateOrganization && _organizationSettings is not null);
        foreach (var command in Commands) command.ExecutionFailed += OnUnexpectedFailure;
    }

    public event Action<string, Guid>? OpenObjectRequested;
    public IReadOnlyList<DesktopCatalogItem> Catalog { get => _catalog; private set => SetProperty(ref _catalog, value); }
    public IReadOnlyList<DesktopContact> Contacts { get => _contacts; private set => SetProperty(ref _contacts, value); }
    public IReadOnlyList<DesktopSearchResult> SearchResults { get => _searchResults; private set => SetProperty(ref _searchResults, value); }
    public IReadOnlyList<DesktopNotification> Notifications { get => _notifications; private set { if (SetProperty(ref _notifications, value)) { OnPropertyChanged(nameof(UnreadCount)); MarkAllReadCommand.RaiseCanExecuteChanged(); } } }
    public IReadOnlyList<DesktopLifecycleItem> LifecycleItems { get => _lifecycleItems; private set => SetProperty(ref _lifecycleItems, value); }
    public int UnreadCount => Notifications.Count(n => n.Status is not ("read" or "dismissed"));
    public DesktopCatalogItem? SelectedCatalogItem { get => _selectedCatalogItem; set { if (SetProperty(ref _selectedCatalogItem, value)) NotifyCommands(); } }
    public DesktopNotification? SelectedNotification { get => _selectedNotification; set { if (SetProperty(ref _selectedNotification, value)) MarkReadCommand.RaiseCanExecuteChanged(); } }
    public DesktopLifecycleItem? SelectedLifecycleItem { get => _selectedLifecycleItem; set { if (SetProperty(ref _selectedLifecycleItem, value)) RestoreLifecycleItemCommand.RaiseCanExecuteChanged(); } }
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) SearchCommand.RaiseCanExecuteChanged(); } }
    public string NewItemName { get => _newItemName; set { if (SetProperty(ref _newItemName, value)) CreateCatalogItemCommand.RaiseCanExecuteChanged(); } }
    public string NewItemPath { get => _newItemPath; set { if (SetProperty(ref _newItemPath, value)) AddLocationCommand.RaiseCanExecuteChanged(); } }
    public string NewContactFirstName { get => _newContactFirstName; set { if (SetProperty(ref _newContactFirstName, value)) CreateContactCommand.RaiseCanExecuteChanged(); } }
    public string NewContactLastName { get => _newContactLastName; set => SetProperty(ref _newContactLastName, value); }
    public string NewContactDisplayName { get => _newContactDisplayName; set { if (SetProperty(ref _newContactDisplayName, value)) CreateContactCommand.RaiseCanExecuteChanged(); } }
    public string UserLanguage { get => _userLanguage; set => SetProperty(ref _userLanguage, value); }
    public string UserTimeFormat { get => _userTimeFormat; set => SetProperty(ref _userTimeFormat, value); }
    public string UserWorkdayStart { get => _userWorkdayStart; set => SetProperty(ref _userWorkdayStart, value); }
    public string UserWorkdayEnd { get => _userWorkdayEnd; set => SetProperty(ref _userWorkdayEnd, value); }
    public int DefaultTaskDurationMinutes { get => _defaultTaskDurationMinutes; set => SetProperty(ref _defaultTaskDurationMinutes, value); }
    public int DefaultReminderOffsetMinutes { get => _defaultReminderOffsetMinutes; set => SetProperty(ref _defaultReminderOffsetMinutes, value); }
    public bool AutostartEnabled { get => _autostartEnabled; set => SetProperty(ref _autostartEnabled, value); }
    public bool AllowLocalPaths { get => _allowLocalPaths; set => SetProperty(ref _allowLocalPaths, value); }
    public bool ConfirmCatalogDelete { get => _confirmCatalogDelete; set => SetProperty(ref _confirmCatalogDelete, value); }
    public string MissingFileBehavior { get => _missingFileBehavior; set => SetProperty(ref _missingFileBehavior, value); }
    public bool NotificationsEnabled { get => _notificationsEnabled; set => SetProperty(ref _notificationsEnabled, value); }
    public bool DesktopNotificationsEnabled { get => _desktopNotificationsEnabled; set => SetProperty(ref _desktopNotificationsEnabled, value); }
    public bool NotificationSoundEnabled { get => _notificationSoundEnabled; set => SetProperty(ref _notificationSoundEnabled, value); }
    public int DefaultSnoozeMinutes { get => _defaultSnoozeMinutes; set => SetProperty(ref _defaultSnoozeMinutes, value); }
    public int TrashRetentionDays { get => _trashRetentionDays; set => SetProperty(ref _trashRetentionDays, value); }
    public int HistoryRetentionDays { get => _historyRetentionDays; set => SetProperty(ref _historyRetentionDays, value); }
    public int ChangeFeedRetentionDays { get => _changeFeedRetentionDays; set => SetProperty(ref _changeFeedRetentionDays, value); }
    public int RecurrenceHorizonDays { get => _recurrenceHorizonDays; set => SetProperty(ref _recurrenceHorizonDays, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public WorkHubArea Area { get => _area; private set { if (!SetProperty(ref _area, value)) return; OnPropertyChanged(nameof(IsCatalog)); OnPropertyChanged(nameof(IsContacts)); OnPropertyChanged(nameof(IsSearch)); OnPropertyChanged(nameof(IsNotifications)); OnPropertyChanged(nameof(IsArchive)); OnPropertyChanged(nameof(IsTrash)); OnPropertyChanged(nameof(IsLifecycle)); OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(AreaTitle)); OnPropertyChanged(nameof(AccessText)); NotifyCommands(); } }
    public bool IsCatalog => Area == WorkHubArea.Catalog;
    public bool IsContacts => Area == WorkHubArea.Contacts;
    public bool IsSearch => Area == WorkHubArea.Search;
    public bool IsNotifications => Area == WorkHubArea.Notifications;
    public bool IsArchive => Area == WorkHubArea.Archive;
    public bool IsTrash => Area == WorkHubArea.Trash;
    public bool IsLifecycle => IsArchive || IsTrash;
    public bool IsSettings => Area == WorkHubArea.Settings;
    public string AreaTitle => Area switch { WorkHubArea.Catalog => "Каталог", WorkHubArea.Contacts => "Контакты", WorkHubArea.Search => "Поиск", WorkHubArea.Notifications => "Уведомления", WorkHubArea.Archive => "Архив", WorkHubArea.Trash => "Корзина", WorkHubArea.Settings => "Настройки", _ => "Рабочие данные" };
    public bool CanReadCurrentArea => Area switch { WorkHubArea.Catalog => Has("FileCatalog.Read"), WorkHubArea.Contacts => Has("Contact.Read"), WorkHubArea.Search => CanSearch, WorkHubArea.Notifications => CanReadNotifications, WorkHubArea.Archive => CanReadArchive, WorkHubArea.Trash => CanReadTrash, WorkHubArea.Settings => CanReadSettings || CanReadOrganization, _ => false };
    public bool CanSearch => Has("Search.Use");
    public bool CanCreateCatalog => Has("FileCatalog.Create");
    public bool CanUpdateLocation => Has("FileLocation.Update");
    public bool CanOpenFile => Has("FileReference.Open");
    public bool CanCreateContact => Has("Contact.Create");
    public bool CanReadNotifications => Has("Notification.ReadOwn");
    public bool CanReadArchive => Has("History.Read");
    public bool CanRestoreArchive => Has("Archive.Restore");
    public bool CanReadTrash => Has("Trash.Read");
    public bool CanRestoreTrash => Has("Trash.Restore");
    public bool CanReadSettings => Has("Settings.ReadOwn");
    public bool CanUpdateSettings => Has("Settings.UpdateOwn");
    public bool CanReadOrganization => Has("Organization.Read");
    public bool CanUpdateOrganization => Has("Organization.Update");
    public string AccessText => CanReadCurrentArea ? IsArchive ? "Архив доступен только для просмотра; возврат проверяет сервер" : IsTrash ? "Удалены только записи Task; физические файлы не удаляются" : IsSettings ? "Настройки синхронизируются с сервером компании" : "Данные сервера компании · доступ по текущим правам" : "Нет права на просмотр этого раздела";
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
    public AsyncCommand RestoreLifecycleItemCommand { get; }
    public AsyncCommand SaveUserSettingsCommand { get; }
    public AsyncCommand SaveNotificationPreferencesCommand { get; }
    public AsyncCommand SaveOrganizationSettingsCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, SearchCommand, CreateCatalogItemCommand, AddLocationCommand, OpenFileCommand, CreateContactCommand, MarkReadCommand, MarkAllReadCommand, OpenNotificationSourceCommand, OpenSearchResultCommand, RestoreLifecycleItemCommand, SaveUserSettingsCommand, SaveNotificationPreferencesCommand, SaveOrganizationSettingsCommand];

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
            case WorkHubArea.Archive: Apply(await _client.GetArchiveAsync(ct), value => { LifecycleItems = value; SelectedLifecycleItem = value.FirstOrDefault(); }); break;
            case WorkHubArea.Trash: Apply(await _client.GetTrashAsync(ct), value => { LifecycleItems = value; SelectedLifecycleItem = value.FirstOrDefault(); }); break;
            case WorkHubArea.Settings: await RefreshSettingsAsync(ct); break;
        }
    }

    private async System.Threading.Tasks.Task RefreshSettingsAsync(CancellationToken ct)
    {
        if (CanReadSettings)
        {
            Apply(await _client.GetUserSettingsAsync(ct), LoadUserSettings);
            Apply(await _client.GetNotificationPreferencesAsync(ct), LoadNotificationPreferences);
        }
        if (CanReadOrganization) Apply(await _client.GetOrganizationSettingsAsync(ct), LoadOrganizationSettings);
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
    private async System.Threading.Tasks.Task RestoreLifecycleItemAsync(object? parameter, CancellationToken ct)
    {
        var item = parameter as DesktopLifecycleItem ?? SelectedLifecycleItem; if (item is null) return;
        var result = IsArchive
            ? await _client.RestoreArchiveAsync(item.ObjectId, item.Version, ct)
            : await _client.RestoreTrashAsync(item.ObjectId, item.Version, ct);
        Apply(result, _ =>
        {
            LifecycleItems = LifecycleItems.Where(candidate => candidate.ObjectId != item.ObjectId).ToArray();
            SelectedLifecycleItem = LifecycleItems.FirstOrDefault();
            Message = IsArchive ? "Объект возвращён из архива." : "Объект восстановлен из корзины.";
        });
    }
    private async System.Threading.Tasks.Task SaveUserSettingsAsync(object? parameter, CancellationToken ct)
    {
        if (_userSettings is null) return;
        if (UserLanguage.Trim().Length is < 2 or > 16 || UserTimeFormat is not ("12h" or "24h") ||
            !TimeOnly.TryParse(UserWorkdayStart, out _) || !TimeOnly.TryParse(UserWorkdayEnd, out _) ||
            DefaultTaskDurationMinutes is < 5 or > 1440 || DefaultReminderOffsetMinutes is < 0 or > 525600 ||
            MissingFileBehavior is not ("show_actions" or "keep_inactive" or "prompt_relink"))
        { Message = "Проверьте формат времени и допустимые значения настроек."; return; }
        var draft = _userSettings with { Language = UserLanguage.Trim(), TimeFormat = UserTimeFormat, WorkdayStart = UserWorkdayStart, WorkdayEnd = UserWorkdayEnd, DefaultTaskDurationMinutes = DefaultTaskDurationMinutes, DefaultReminderOffsetMinutes = DefaultReminderOffsetMinutes, AutostartEnabled = AutostartEnabled, AllowLocalPaths = AllowLocalPaths, ConfirmCatalogDelete = ConfirmCatalogDelete, MissingFileBehavior = MissingFileBehavior };
        Apply(await _client.UpdateUserSettingsAsync(draft, ct), value => { LoadUserSettings(value); Message = "Личные настройки сохранены на сервере."; });
    }
    private async System.Threading.Tasks.Task SaveNotificationPreferencesAsync(object? _, CancellationToken ct)
    {
        if (_notificationPreferences is null) return;
        if (DefaultSnoozeMinutes is < 1 or > 10080) { Message = "Отсрочка должна быть от 1 до 10080 минут."; return; }
        var draft = _notificationPreferences with { Enabled = NotificationsEnabled, DesktopEnabled = DesktopNotificationsEnabled, SoundEnabled = NotificationSoundEnabled, DefaultSnoozeMinutes = DefaultSnoozeMinutes };
        Apply(await _client.UpdateNotificationPreferencesAsync(draft, ct), value => { LoadNotificationPreferences(value); Message = "Настройки уведомлений сохранены."; });
    }
    private async System.Threading.Tasks.Task SaveOrganizationSettingsAsync(object? _, CancellationToken ct)
    {
        if (_organizationSettings is null) return;
        if (TrashRetentionDays < 1 || HistoryRetentionDays < 1 || ChangeFeedRetentionDays < 1 || RecurrenceHorizonDays < 1)
        { Message = "Сроки хранения и горизонт повторений должны быть положительными."; return; }
        var draft = _organizationSettings with { TrashRetentionDays = TrashRetentionDays, HistoryRetentionDays = HistoryRetentionDays, ChangeFeedRetentionDays = ChangeFeedRetentionDays, RecurrenceHorizonDays = RecurrenceHorizonDays };
        Apply(await _client.UpdateOrganizationSettingsAsync(draft, ct), value => { LoadOrganizationSettings(value); Message = "Настройки организации сохранены."; });
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

    private void LoadUserSettings(DesktopUserSettings value)
    {
        _userSettings = value; UserLanguage = value.Language; UserTimeFormat = value.TimeFormat;
        UserWorkdayStart = value.WorkdayStart; UserWorkdayEnd = value.WorkdayEnd;
        DefaultTaskDurationMinutes = value.DefaultTaskDurationMinutes; DefaultReminderOffsetMinutes = value.DefaultReminderOffsetMinutes;
        AutostartEnabled = value.AutostartEnabled; AllowLocalPaths = value.AllowLocalPaths;
        ConfirmCatalogDelete = value.ConfirmCatalogDelete; MissingFileBehavior = value.MissingFileBehavior;
        SaveUserSettingsCommand.RaiseCanExecuteChanged();
    }
    private void LoadNotificationPreferences(DesktopNotificationPreferences value)
    {
        _notificationPreferences = value; NotificationsEnabled = value.Enabled; DesktopNotificationsEnabled = value.DesktopEnabled;
        NotificationSoundEnabled = value.SoundEnabled; DefaultSnoozeMinutes = value.DefaultSnoozeMinutes;
        SaveNotificationPreferencesCommand.RaiseCanExecuteChanged();
    }
    private void LoadOrganizationSettings(DesktopOrganizationSettings value)
    {
        _organizationSettings = value; TrashRetentionDays = value.TrashRetentionDays; HistoryRetentionDays = value.HistoryRetentionDays;
        ChangeFeedRetentionDays = value.ChangeFeedRetentionDays; RecurrenceHorizonDays = value.RecurrenceHorizonDays;
        SaveOrganizationSettingsCommand.RaiseCanExecuteChanged();
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
    private void NotifyCommands() { OnPropertyChanged(nameof(CanReadCurrentArea)); OnPropertyChanged(nameof(CanReadArchive)); OnPropertyChanged(nameof(CanRestoreArchive)); OnPropertyChanged(nameof(CanReadTrash)); OnPropertyChanged(nameof(CanRestoreTrash)); OnPropertyChanged(nameof(CanReadSettings)); OnPropertyChanged(nameof(CanUpdateSettings)); OnPropertyChanged(nameof(CanReadOrganization)); OnPropertyChanged(nameof(CanUpdateOrganization)); OnPropertyChanged(nameof(AccessText)); foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
    private void OnUnexpectedFailure(Exception _) => Message = "Не удалось завершить действие.";
    public void Dispose() { if (_disposed) return; _disposed = true; _activation?.Cancel(); _activation?.Dispose(); foreach (var command in Commands) command.Dispose(); }
}
