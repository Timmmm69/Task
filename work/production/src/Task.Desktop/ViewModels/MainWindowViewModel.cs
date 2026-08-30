using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Task.Desktop.ViewModels;

/// <summary>
/// View model for the main window shell: navigation sections,
/// the selected section and the connection status.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Func<CancellationToken, global::System.Threading.Tasks.Task>? _logout;
    private NavigationSection? _selectedSection;
    private string? _sessionMessage;
    private bool _disposed;

    public MainWindowViewModel()
        : this(null, null, null)
    {
    }

    public MainWindowViewModel(
        Uri? serverEndpoint,
        Func<CancellationToken, global::System.Threading.Tasks.Task>? logout,
        TasksViewModel? tasks = null)
    {
        ServerAddress = serverEndpoint?.GetLeftPart(UriPartial.Authority);
        _logout = logout;
        Tasks = tasks;
        if (Tasks is not null)
        {
            Tasks.PropertyChanged += OnTasksPropertyChanged;
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
            new("notifications", "Уведомления", "Раздел «Уведомления»: уведомления появятся после подключения к серверу.", "Task.Icon.Notifications", "События и уведомления"),
            new("archive", "Архив", "Раздел «Архив»: архивные задачи появятся после подключения к серверу.", "Task.Icon.Archive", "Архивные записи"),
            new("trash", "Корзина", "Раздел «Корзина»: удалённые записи появятся после подключения к серверу.", "Task.Icon.Trash", "Удалённые записи"),
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
            OnPropertyChanged(nameof(SelectedSectionSupportingText));
            if (IsTasksSectionSelected)
            {
                Tasks?.Activate();
            }
            else
            {
                Tasks?.Deactivate();
            }
        }
    }

    public TasksViewModel? Tasks { get; }

    public bool IsTasksSectionSelected =>
        string.Equals(SelectedSection?.Route, "tasks", StringComparison.Ordinal);

    public string? SelectedSectionSupportingText => SelectedSection?.SupportingText;

    /// <summary>Server whose authentication session was confirmed before opening the shell.</summary>
    public string? ServerAddress { get; }

    /// <summary>Visible authentication and connection status.</summary>
    public string ConnectionStatus => ServerAddress is null
        ? "Нет подтверждённого подключения"
        : $"Подключено к серверу компании · {ServerAddress}";

    public string ConnectionTitle => ServerAddress is null
        ? "Нет подтверждённого подключения"
        : "Подключено к серверу компании";

    public string ConnectionContext => ServerAddress is null
        ? "Нет рабочей сессии"
        : Tasks?.IsReadOnly == true ? "Онлайн · только просмотр" : "Онлайн · запись доступна";

    public string ConnectionIconKey => ServerAddress is null
        ? "Task.Icon.Warning"
        : "Task.Icon.Connected";

    public bool IsConnected => ServerAddress is not null;

    /// <summary>
    /// True while the shell has no network client: the interface is view-only.
    /// </summary>
    public bool IsReadOnlyMode => Tasks?.IsReadOnly ?? true;

    /// <summary>
    /// Notice shown in read-only mode: the server is not connected,
    /// no synchronization runs and data changes are unavailable.
    /// </summary>
    public string ReadOnlyNotice => ServerAddress is null
        ? "Сервер не подключён: синхронизация не выполняется, изменение данных недоступно."
        : Tasks?.WriteAccessText ?? "Доступен только просмотр задач.";

    public string DataSourceStatus => ServerAddress is null
        ? "Источник данных недоступен"
        : Tasks?.IsReadOnly == true
            ? "Данные предоставляются сервером компании · только просмотр"
            : "Данные и изменения синхронизируются с сервером компании";

    public string ReadOnlyActionReason =>
        Tasks?.CanCreate == true
            ? "Создать задачу"
            : "Для создания задачи требуется право Task.Create и рабочая сессия.";

    public string? SessionMessage
    {
        get => _sessionMessage;
        private set => SetProperty(ref _sessionMessage, value);
    }

    public bool IsBusy => LogoutCommand.IsExecuting;

    public AsyncCommand LogoutCommand { get; }

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

        LogoutCommand.Dispose();
        Tasks?.Dispose();
    }

    private void OnTasksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksViewModel.IsReadOnly)
            or nameof(TasksViewModel.WriteAccessText)
            or nameof(TasksViewModel.CanCreate))
        {
            OnPropertyChanged(nameof(ConnectionContext));
            OnPropertyChanged(nameof(IsReadOnlyMode));
            OnPropertyChanged(nameof(ReadOnlyNotice));
            OnPropertyChanged(nameof(DataSourceStatus));
            OnPropertyChanged(nameof(ReadOnlyActionReason));
        }
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
