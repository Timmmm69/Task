using System.Collections.ObjectModel;

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

    public MainWindowViewModel()
        : this(null, null)
    {
    }

    public MainWindowViewModel(
        Uri? serverEndpoint,
        Func<CancellationToken, global::System.Threading.Tasks.Task>? logout)
    {
        ServerAddress = serverEndpoint?.GetLeftPart(UriPartial.Authority);
        _logout = logout;
        Sections = new ObservableCollection<NavigationSection>
        {
            new("today", "Сегодня", "Раздел «Сегодня»: сводка задач на текущий день появится после подключения к серверу."),
            new("inbox", "Входящие", "Раздел «Входящие»: новые и назначенные задачи появятся после подключения к серверу."),
            new("calendar", "Календарь", "Раздел «Календарь»: календарная сетка появится после подключения к серверу."),
            new("tasks", "Задачи", "Раздел «Задачи»: список задач появится после подключения к серверу."),
            new("projects", "Проекты", "Раздел «Проекты»: список проектов появится после подключения к серверу."),
            new("catalog", "Каталог", "Раздел «Каталог»: файлы и записи каталога появятся после подключения к серверу."),
            new("contacts", "Контакты", "Раздел «Контакты»: список контактов появится после подключения к серверу."),
            new("notifications", "Уведомления", "Раздел «Уведомления»: уведомления появятся после подключения к серверу."),
            new("archive", "Архив", "Раздел «Архив»: архивные задачи появятся после подключения к серверу."),
            new("trash", "Корзина", "Раздел «Корзина»: удалённые записи появятся после подключения к серверу."),
            new("settings", "Настройки", "Раздел «Настройки»: параметры приложения появятся после подключения к серверу."),
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
        set => SetProperty(ref _selectedSection, value);
    }

    /// <summary>Server whose authentication session was confirmed before opening the shell.</summary>
    public string? ServerAddress { get; }

    /// <summary>Visible authentication and connection status.</summary>
    public string ConnectionStatus => ServerAddress is null
        ? "Нет подключения — только просмотр"
        : $"Сессия подтверждена · {ServerAddress}";

    /// <summary>
    /// True while the shell has no network client: the interface is view-only.
    /// </summary>
    public bool IsReadOnlyMode => true;

    /// <summary>
    /// Notice shown in read-only mode: the server is not connected,
    /// no synchronization runs and data changes are unavailable.
    /// </summary>
    public string ReadOnlyNotice => ServerAddress is null
        ? "Сервер не подключён: синхронизация не выполняется, изменение данных недоступно."
        : "Предметная синхронизация пока не подключена: изменение данных недоступно.";

    public string? SessionMessage
    {
        get => _sessionMessage;
        private set => SetProperty(ref _sessionMessage, value);
    }

    public bool IsBusy => LogoutCommand.IsExecuting;

    public AsyncCommand LogoutCommand { get; }

    public void Dispose() => LogoutCommand.Dispose();

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
