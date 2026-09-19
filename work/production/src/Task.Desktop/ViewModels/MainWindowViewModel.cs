using System.Collections.ObjectModel;
using System.ComponentModel;
using Task.Desktop.Security;

namespace Task.Desktop.ViewModels;

/// <summary>
/// View model for the main window shell: navigation sections,
/// the selected section and the connection status.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Func<CancellationToken, global::System.Threading.Tasks.Task>? _logout;
    private readonly DesktopConnectivityService? _connectivity;
    private NavigationSection? _selectedSection;
    private string? _sessionMessage;
    private bool _disposed;

    public MainWindowViewModel()
        : this(null, null, null, null, null, null, null, null)
    {
    }

    public MainWindowViewModel(
        Uri? serverEndpoint,
        Func<CancellationToken, global::System.Threading.Tasks.Task>? logout,
        TasksViewModel? tasks = null,
        CalendarViewModel? calendar = null,
        TodayViewModel? today = null,
        ProjectsViewModel? projects = null,
        WorkHubViewModel? workHub = null,
        DesktopConnectivityService? connectivity = null,
        InboxViewModel? inbox = null,
        AdministrationViewModel? administration = null)
    {
        ServerAddress = serverEndpoint?.GetLeftPart(UriPartial.Authority);
        _logout = logout;
        Tasks = tasks;
        Calendar = calendar;
        Today = today;
        Projects = projects;
        WorkHub = workHub;
        Inbox = inbox;
        Administration = administration;
        _connectivity = connectivity;
        NewTaskCommand = new AsyncCommand(OpenNewTaskAsync,
            _ => Tasks?.CanCreateFromShell == true && IsConnected);
        if (Today is not null)
        {
            Today.PropertyChanged += OnTodayPropertyChanged;
            Today.OpenItemRequested += OpenTodayItem;
        }
        if (Tasks is not null)
        {
            Tasks.PropertyChanged += OnTasksPropertyChanged;
        }
        if (Calendar is not null)
        {
            Calendar.PropertyChanged += OnCalendarPropertyChanged;
        }
        if (Projects is not null)
        {
            Projects.PropertyChanged += OnProjectsPropertyChanged;
        }
        if (WorkHub is not null)
        {
            WorkHub.PropertyChanged += OnWorkHubPropertyChanged;
            WorkHub.OpenObjectRequested += OpenWorkObject;
        }
        if (Inbox is not null)
        {
            Inbox.PropertyChanged += OnInboxPropertyChanged;
        }
        if (_connectivity is not null)
        {
            _connectivity.StatusChanged += OnConnectivityChanged;
            ApplyConnectivityState();
        }
        Sections = new ObservableCollection<NavigationSection>
        {
            new("today", "Сегодня", "Раздел «Сегодня»: сводка задач на текущий день появится после подключения к серверу.", "Task.Icon.Today", "Сводка на текущий день"),
            new("inbox", "Входящие", "Раздел «Входящие»: новые и назначенные задачи появятся после подключения к серверу.", "Task.Icon.Inbox", "Новые и назначенные записи"),
            new("calendar", "Календарь", "Раздел «Календарь»: календарная сетка появится после подключения к серверу.", "Task.Icon.Calendar", "Расписание компании"),
            new("tasks", "Задачи", "Раздел «Задачи»: список задач появится после подключения к серверу.", "Task.Icon.Tasks", "Активные задачи компании"),
            new("projects", "Проекты", "Раздел «Проекты»: список проектов появится после подключения к серверу.", "Task.Icon.Projects", "Рабочие проекты компании"),
            new("catalog", "Каталог", "Раздел «Каталог»: файлы и записи каталога появятся после подключения к серверу.", "Task.Icon.Catalog", "Файлы и записи каталога"),
            new("contacts", "Контакты", "Раздел «Контакты»: список контактов появится после подключения к серверу.", "Task.Icon.Contacts", "Контакты компании"),
            new("search", "Поиск", "Единый поиск по доступным рабочим данным.", "Task.Icon.Search", "Поиск по задачам, проектам, контактам и файлам"),
            new("notifications", "Уведомления", "Раздел «Уведомления»: уведомления появятся после подключения к серверу.", "Task.Icon.Notifications", "События и уведомления"),
            new("archive", "Архив", "Раздел «Архив»: архивные задачи появятся после подключения к серверу.", "Task.Icon.Archive", "Архивные записи"),
            new("trash", "Корзина", "Раздел «Корзина»: удалённые записи появятся после подключения к серверу.", "Task.Icon.Trash", "Удалённые записи"),
            new("administration", "Администрирование", "Пользователи, роли и ресурсы доступны в пределах опубликованных прав.", "Task.Icon.Lock", "Пользователи, роли и сетевые ресурсы"),
            new("settings", "Настройки", "Раздел «Настройки»: параметры приложения появятся после подключения к серверу.", "Task.Icon.Settings", "Параметры приложения"),
        };

        SelectedSection = Sections[0];
        LogoutCommand = new AsyncCommand(LogoutAsync, _ => _logout is not null);
        LogoutCommand.ExecutionFailed += OnLogoutFailed;
        LogoutCommand.CanExecuteChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(SessionMessage));
        };
    }

    /// <summary>Navigation sections shown in the left panel.</summary>
    public ObservableCollection<NavigationSection> Sections { get; }

    /// <summary>Currently selected navigation section.</summary>
    public NavigationSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (!SetProperty(ref _selectedSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTasksSectionSelected));
            OnPropertyChanged(nameof(IsCalendarSectionSelected));
            OnPropertyChanged(nameof(IsTodaySectionSelected));
            OnPropertyChanged(nameof(IsInboxSectionSelected));
            OnPropertyChanged(nameof(IsProjectsSectionSelected));
            OnPropertyChanged(nameof(IsWorkHubSectionSelected));
            OnPropertyChanged(nameof(IsAdministrationSectionSelected));
            OnPropertyChanged(nameof(SelectedSectionSupportingText));
            if (IsTodaySectionSelected)
            {
                Today?.Activate();
            }
            else
            {
                Today?.Deactivate();
            }
            if (IsTasksSectionSelected)
            {
                Tasks?.Activate();
            }
            else
            {
                Tasks?.Deactivate();
            }
            if (IsInboxSectionSelected) Inbox?.Activate(); else Inbox?.Deactivate();
            if (IsCalendarSectionSelected) Calendar?.Activate(); else Calendar?.Deactivate();
            if (IsProjectsSectionSelected) Projects?.Activate(); else Projects?.Deactivate();
            if (TryGetWorkArea(SelectedSection?.Route, out var workArea)) WorkHub?.Activate(workArea); else WorkHub?.Deactivate();
            if (IsAdministrationSectionSelected) Administration?.Activate(); else Administration?.Deactivate();
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            OnPropertyChanged(nameof(FooterRefreshCommand));
            NewTaskCommand.RaiseCanExecuteChanged();
        }
    }

    private async global::System.Threading.Tasks.Task OpenNewTaskAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        if (Tasks is null || !IsConnected) return;
        SelectedSection = Sections.First(section => section.Route == "tasks");
        await Tasks.ActivateAsync(cancellationToken).ConfigureAwait(true);
        if (Tasks.NewTaskCommand.CanExecute(null))
            await Tasks.NewTaskCommand.ExecuteAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private void OpenTodayItem(object? item) => _ = OpenTodayItemAsync(item);

    internal async global::System.Threading.Tasks.Task OpenTodayItemAsync(object? item)
    {
        try
        {
            if (_disposed) return;
            if (item is CalendarItemViewModel { IsCalendarEvent: true } calendarItem && Calendar is not null)
            {
                SelectedSection = Sections.First(s => s.Route == "calendar");
                Calendar.SelectedItem = calendarItem;
                return;
            }
            if (Tasks is null) return;
            var id = item switch { TaskItemViewModel task => task.Id, CalendarItemViewModel scheduled => scheduled.Id, _ => Guid.Empty };
            if (id == Guid.Empty) return;
            SelectedSection = Sections.First(s => s.Route == "tasks");
            await Tasks.ActivateAsync();
            await Tasks.OpenByIdAsync(id);
        }
        catch (Exception)
        {
            if (!_disposed)
                SessionMessage = "Не удалось открыть выбранный объект. Повторите попытку.";
        }
    }

    public TasksViewModel? Tasks { get; }

    public InboxViewModel? Inbox { get; }

    public CalendarViewModel? Calendar { get; }

    public TodayViewModel? Today { get; }

    public ProjectsViewModel? Projects { get; }

    public WorkHubViewModel? WorkHub { get; }

    public AdministrationViewModel? Administration { get; }

    public bool IsTodaySectionSelected =>
        string.Equals(SelectedSection?.Route, "today", StringComparison.Ordinal);

    public bool IsTasksSectionSelected =>
        string.Equals(SelectedSection?.Route, "tasks", StringComparison.Ordinal);

    public bool IsInboxSectionSelected =>
        string.Equals(SelectedSection?.Route, "inbox", StringComparison.Ordinal);

    public bool IsCalendarSectionSelected =>
        string.Equals(SelectedSection?.Route, "calendar", StringComparison.Ordinal);

    public bool IsProjectsSectionSelected =>
        string.Equals(SelectedSection?.Route, "projects", StringComparison.Ordinal);

    public bool IsWorkHubSectionSelected => TryGetWorkArea(SelectedSection?.Route, out _);

    public bool IsAdministrationSectionSelected => string.Equals(SelectedSection?.Route, "administration", StringComparison.Ordinal);

    public string? SelectedSectionSupportingText => IsTodaySectionSelected
        ? Today?.DateText ?? SelectedSection?.SupportingText
        : SelectedSection?.SupportingText;

    /// <summary>Server whose authentication session was confirmed before opening the shell.</summary>
    public string? ServerAddress { get; }

    /// <summary>Visible authentication and connection status.</summary>
    public string ConnectionStatus => ServerAddress is null
        ? "Нет подтверждённого подключения"
        : _connectivity?.Status switch
        {
            DesktopConnectivityStatus.ServerUnavailable => $"Сервер недоступен · {ServerAddress}",
            DesktopConnectivityStatus.Reconnecting => $"Восстанавливаем подключение · {ServerAddress}",
            _ => $"Подключено к серверу компании · {ServerAddress}",
        };

    public string ConnectionTitle => ServerAddress is null
        ? "Нет подтверждённого подключения"
        : _connectivity?.Status switch
        {
            DesktopConnectivityStatus.ServerUnavailable => "Сервер недоступен",
            DesktopConnectivityStatus.Reconnecting => "Восстанавливаем подключение",
            _ => "Подключено к серверу компании",
        };

    public string ConnectionContext => ServerAddress is null
        ? "Нет рабочей сессии"
        : _connectivity?.Status == DesktopConnectivityStatus.ServerUnavailable
            ? "Офлайн · изменения заблокированы · используйте «Повторить»"
        : _connectivity?.Status == DesktopConnectivityStatus.Reconnecting
            ? "Повторная проверка сервера · изменения временно заблокированы"
        : IsTodaySectionSelected
            ? Today?.CanRead == true ? "Онлайн · сегодняшний план доступен" : "Онлайн · нет доступа к расписанию"
        : IsInboxSectionSelected
            ? Inbox?.IsReadOnly == true ? "Онлайн · входящие только для просмотра" : "Онлайн · разбор входящих доступен"
        : IsCalendarSectionSelected
            ? Calendar?.CanCreate == true ? "Онлайн · запись календаря доступна" : "Онлайн · календарь только для просмотра"
        : IsProjectsSectionSelected
            ? Projects?.CanUpdate == true ? "Онлайн · управление проектами доступно" : "Онлайн · проекты только для просмотра"
        : IsWorkHubSectionSelected
            ? WorkHub?.AccessText ?? "Онлайн · рабочие данные"
        : IsAdministrationSectionSelected
            ? Administration?.AccessText ?? "Онлайн · административные данные"
            : Tasks?.IsReadOnly == true ? "Онлайн · только просмотр" : "Онлайн · запись доступна";

    public string ConnectionIconKey => ServerAddress is null || _connectivity?.Status != DesktopConnectivityStatus.Online
        ? "Task.Icon.Warning"
        : "Task.Icon.Connected";

    public bool IsConnected => ServerAddress is not null
        && (_connectivity is null || _connectivity.Status == DesktopConnectivityStatus.Online);

    /// <summary>
    /// True while the shell has no network client: the interface is view-only.
    /// </summary>
    public bool IsReadOnlyMode => !IsConnected || (Tasks?.IsReadOnly ?? true);

    /// <summary>
    /// Notice shown in read-only mode: the server is not connected,
    /// no synchronization runs and data changes are unavailable.
    /// </summary>
    public string ReadOnlyNotice => ServerAddress is null
        ? "Сервер не подключён: синхронизация не выполняется, изменение данных недоступно."
        : !IsConnected
            ? "Сервер временно недоступен: подтверждённые данные сохранены, изменения не отправляются. Нажмите «Повторить» для проверки связи."
        : Tasks?.WriteAccessText ?? "Доступен только просмотр задач.";

    public string DataSourceStatus => ServerAddress is null
        ? "Источник данных недоступен"
        : !IsConnected
            ? $"Показаны последние подтверждённые данные · {LastSuccessfulRefreshText}"
        : IsTodaySectionSelected
            ? "Сегодняшнее расписание предоставляется сервером компании"
        : IsInboxSectionSelected
            ? Inbox?.IsReadOnly == true
                ? "Входящие предоставляются сервером компании · только просмотр"
                : "Входящие и преобразование синхронизируются с сервером компании"
        : IsCalendarSectionSelected
            ? Calendar?.WriteAccessText ?? "Календарь сервера компании"
        : IsProjectsSectionSelected
            ? Projects?.AccessText ?? "Проекты сервера компании"
        : IsWorkHubSectionSelected
            ? WorkHub?.AccessText ?? "Рабочие данные сервера компании"
        : IsAdministrationSectionSelected
            ? Administration?.AccessText ?? "Административные данные сервера компании"
            : Tasks?.IsReadOnly == true
            ? "Данные предоставляются сервером компании · только просмотр"
            : "Данные и изменения синхронизируются с сервером компании";

    public string LastSuccessfulRefreshText => IsTodaySectionSelected
        ? Today?.LastSuccessfulRefreshText ?? "Сегодняшний план ещё не обновлялся"
        : IsInboxSectionSelected
        ? Inbox?.LastSuccessfulRefreshText ?? "Входящие ещё не обновлялись"
        : IsCalendarSectionSelected
        ? Calendar?.LastSuccessfulRefreshText ?? "Календарь ещё не обновлялся"
        : IsProjectsSectionSelected
        ? Projects?.LastSuccessfulRefreshText ?? "Проекты ещё не обновлялись"
        : IsWorkHubSectionSelected
        ? WorkHub?.LastSuccessfulRefreshText ?? "Раздел ещё не обновлялся"
        : IsAdministrationSectionSelected
        ? Administration?.LastSuccessfulRefreshText ?? "Раздел ещё не обновлялся"
        : Tasks?.LastSuccessfulRefreshText ?? "Данные ещё не обновлялись";

    public AsyncCommand? FooterRefreshCommand => IsTodaySectionSelected
        ? Today?.RefreshCommand
        : IsInboxSectionSelected
        ? Inbox?.RefreshCommand
        : IsCalendarSectionSelected
        ? Calendar?.RefreshCommand
        : IsProjectsSectionSelected
        ? Projects?.RefreshCommand
        : IsWorkHubSectionSelected
        ? WorkHub?.RefreshCommand
        : IsAdministrationSectionSelected
        ? Administration?.RefreshCommand
        : Tasks?.RefreshCommand;

    public string ReadOnlyActionReason =>
        !IsConnected
            ? "Создание недоступно до восстановления подключения к серверу."
        : Tasks?.CanCreate == true
            ? "Создать задачу"
            : "Для создания задачи требуется право Task.Create и рабочая сессия.";

    public string? SessionMessage
    {
        get => _sessionMessage;
        private set => SetProperty(ref _sessionMessage, value);
    }

    public bool IsBusy => LogoutCommand.IsExecuting;

    public AsyncCommand LogoutCommand { get; }
    public AsyncCommand NewTaskCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Tasks is not null)
        {
            Tasks.PropertyChanged -= OnTasksPropertyChanged;
        }
        if (Calendar is not null)
        {
            Calendar.PropertyChanged -= OnCalendarPropertyChanged;
        }

        if (Today is not null)
        {
            Today.PropertyChanged -= OnTodayPropertyChanged;
            Today.OpenItemRequested -= OpenTodayItem;
        }
        if (Projects is not null)
        {
            Projects.PropertyChanged -= OnProjectsPropertyChanged;
        }
        if (WorkHub is not null)
        {
            WorkHub.PropertyChanged -= OnWorkHubPropertyChanged;
            WorkHub.OpenObjectRequested -= OpenWorkObject;
        }
        if (Inbox is not null)
        {
            Inbox.PropertyChanged -= OnInboxPropertyChanged;
        }
        if (_connectivity is not null)
        {
            _connectivity.StatusChanged -= OnConnectivityChanged;
        }

        LogoutCommand.Dispose();
        NewTaskCommand.Dispose();
        Tasks?.Dispose();
        Inbox?.Dispose();
        Calendar?.Dispose();
        Today?.Dispose();
        Projects?.Dispose();
        WorkHub?.Dispose();
        Administration?.Dispose();
    }

    private void OnTasksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksViewModel.IsReadOnly)
            or nameof(TasksViewModel.WriteAccessText)
            or nameof(TasksViewModel.CanCreate)
            or nameof(TasksViewModel.CanCreateFromShell))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(IsReadOnlyMode));
            OnPropertyChanged(nameof(ReadOnlyNotice));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(ReadOnlyActionReason));
            NewTaskCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnTodayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TodayViewModel.CanRead)
            or nameof(TodayViewModel.LastSuccessfulRefreshText)
            or nameof(TodayViewModel.DateText))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
        }
    }

    private void OnCalendarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalendarViewModel.CanCreate)
            or nameof(CalendarViewModel.WriteAccessText)
            or nameof(CalendarViewModel.LastSuccessfulRefreshText))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
        }
    }

    private void OnProjectsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectsViewModel.CanUpdate)
            or nameof(ProjectsViewModel.AccessText)
            or nameof(ProjectsViewModel.LastSuccessfulRefreshText))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            OnPropertyChanged(nameof(SelectedSectionSupportingText));
        }
    }

    private void OnWorkHubPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkHubViewModel.AccessText) or nameof(WorkHubViewModel.LastSuccessfulRefreshText))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
        }
    }

    private void OnInboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InboxViewModel.IsReadOnly)
            or nameof(InboxViewModel.WriteAccessText)
            or nameof(InboxViewModel.LastSuccessfulRefreshText))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            OnPropertyChanged(nameof(FooterRefreshCommand));
        }
    }

    private void OnConnectivityChanged(object? sender, EventArgs e) => ApplyConnectivityState();

    private void ApplyConnectivityState()
    {
        if (_disposed) return;
        var available = _connectivity?.Status == DesktopConnectivityStatus.Online;
        Tasks?.UpdateConnectivity(available);
        Inbox?.UpdateConnectivity(available);
        Calendar?.UpdateConnectivity(available);
        Projects?.UpdateConnectivity(available);
        WorkHub?.UpdateConnectivity(available);
        Administration?.UpdateConnectivity(available);
        Today?.UpdateConnectivity(available);
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionTitle));
        OnPropertyChanged(nameof(ConnectionContext));
        OnPropertyChanged(nameof(ConnectionIconKey));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsReadOnlyMode));
        OnPropertyChanged(nameof(ReadOnlyNotice));
        OnPropertyChanged(nameof(DataSourceStatus));
        OnPropertyChanged(nameof(ReadOnlyActionReason));
        NewTaskCommand.RaiseCanExecuteChanged();
    }

    private void OpenWorkObject(string objectType, Guid id) => _ = OpenWorkObjectAsync(objectType, id);

    internal async global::System.Threading.Tasks.Task OpenWorkObjectAsync(string objectType, Guid id)
    {
        try
        {
            if (_disposed) return;
            var route = objectType switch
            {
                "task" => "tasks",
                "project" => "projects",
                "contact" or "company" => "contacts",
                "catalog_item" or "file_location" => "catalog",
                "calendar_event" => "calendar",
                "notification" => "notifications",
                _ => null,
            };
            if (route is null) return;
            SelectedSection = Sections.First(section => section.Route == route);
            if (route == "tasks" && Tasks is not null) { await Tasks.ActivateAsync(); await Tasks.OpenByIdAsync(id); }
        }
        catch (Exception)
        {
            if (!_disposed)
                SessionMessage = "Не удалось открыть выбранный объект. Повторите попытку.";
        }
    }

    private static bool TryGetWorkArea(string? route, out WorkHubArea area)
    {
        area = route switch
        {
            "catalog" => WorkHubArea.Catalog,
            "contacts" => WorkHubArea.Contacts,
            "search" => WorkHubArea.Search,
            "notifications" => WorkHubArea.Notifications,
            "archive" => WorkHubArea.Archive,
            "trash" => WorkHubArea.Trash,
            "settings" => WorkHubArea.Settings,
            _ => default,
        };
        return route is "catalog" or "contacts" or "search" or "notifications" or "archive" or "trash" or "settings";
    }

    private async global::System.Threading.Tasks.Task LogoutAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        if (_logout is null)
        {
            return;
        }

        SessionMessage = "Завершаем сессию…";
        OnPropertyChanged(nameof(IsBusy));
        await _logout(cancellationToken).ConfigureAwait(true);
    }

    private void OnLogoutFailed(Exception exception)
    {
        SessionMessage = "Не удалось завершить сессию. Повторите попытку.";
        OnPropertyChanged(nameof(IsBusy));
    }
}
