using System.Configuration;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using Task.Desktop.Security;
using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

[assembly: InternalsVisibleTo("Task.Desktop.Tests")]

namespace Task.Desktop;

/// <summary>Desktop composition root and authentication-aware window coordinator.</summary>
public partial class App : global::System.Windows.Application
{
    private readonly List<HttpClient> _ownedHttpClients = [];
    private readonly CancellationTokenSource _startupCancellation = new();

    private AuthWorkflowViewModel? _workflow;
    private AuthWindow? _authWindow;
    private MainWindow? _mainWindow;
    private SessionService? _mainSessionService;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var vault = new DesktopCredentialVault();
            var workflow = new AuthWorkflowViewModel(
                new DesktopServerSettingsStore(),
                new DesktopServerProbeClient(CreateHttpClient()),
                vault,
                endpoint => CreateSessionService(endpoint, vault),
                SynchronizationContext.Current);

            _workflow = workflow;
            workflow.PropertyChanged += OnWorkflowPropertyChanged;
            ShowAuthenticationWindow();
            await workflow.StartAsync(_startupCancellation.Token);
        }
        catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Task не удалось запустить. Перезапустите приложение или обратитесь к ИТ-администратору.",
                "Ошибка запуска Task",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            BeginShutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;
        _startupCancellation.Cancel();
        if (_workflow is not null)
        {
            _workflow.PropertyChanged -= OnWorkflowPropertyChanged;
            _workflow.Dispose();
        }

        DetachMainSession();

        foreach (var httpClient in _ownedHttpClients)
        {
            httpClient.Dispose();
        }

        _startupCancellation.Dispose();
        base.OnExit(e);
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownedHttpClients.Add(httpClient);
        return httpClient;
    }

    private SessionService CreateSessionService(Uri endpoint, DesktopCredentialVault vault)
    {
        var apiClient = new DesktopAuthApiClient(CreateHttpClient(), endpoint.AbsoluteUri);
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
        return new SessionService(
            apiClient,
            vault,
            Environment.MachineName,
            ClientPlatform.Windows,
            version,
            Environment.OSVersion.VersionString);
    }

    private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AuthWorkflowViewModel.CurrentState) || _workflow is null)
        {
            return;
        }

        if (_workflow.IsReady)
        {
            ShowMainWindow();
        }
        else if (_mainWindow is not null)
        {
            ShowAuthenticationWindow();
        }
    }

    private void ShowAuthenticationWindow()
    {
        var workflow = _workflow;
        if (workflow is null || _isShuttingDown)
        {
            return;
        }

        if (_authWindow is null)
        {
            _authWindow = new AuthWindow(workflow);
            _authWindow.Closed += OnAuthWindowClosed;
            _authWindow.Show();
        }

        MainWindow = _authWindow;

        if (_mainWindow is not null)
        {
            var window = _mainWindow;
            _mainWindow = null;
            DetachMainSession();
            window.CloseForAuthenticationTransition();
        }

        _authWindow.Activate();
    }

    private void ShowMainWindow()
    {
        var workflow = _workflow;
        if (workflow is null || !workflow.IsReady || _isShuttingDown || _mainWindow is not null)
        {
            return;
        }

        var sessionService = workflow.ReadySessionService;
        var serverEndpoint = workflow.ServerEndpoint;
        if (sessionService is null || serverEndpoint is null)
        {
            ShowAuthenticationWindow();
            return;
        }

        var tasksClient = new DesktopTasksApiClient(
            CreateHttpClient(),
            serverEndpoint,
            sessionService);
        var tasks = new TasksViewModel(
            tasksClient,
            sessionService.CurrentSessionMetadata?.Capabilities ?? Array.Empty<string>());
        var calendarClient = new DesktopCalendarApiClient(CreateHttpClient(), serverEndpoint, sessionService);
        var calendar = new CalendarViewModel(
            calendarClient,
            sessionService.CurrentSessionMetadata?.Capabilities ?? Array.Empty<string>(),
            recurrence: new RecurrencePaneViewModel(new DesktopRecurrenceApiClient(CreateHttpClient(), serverEndpoint, sessionService),
                sessionService.CurrentSessionMetadata!.UserId));
        var today = new TodayViewModel(
            calendarClient,
            sessionService.CurrentSessionMetadata?.Capabilities ?? Array.Empty<string>(),
            tasksClient: tasksClient,
            currentUserId: sessionService.CurrentSessionMetadata!.UserId);
        var viewModel = new MainWindowViewModel(serverEndpoint, workflow.LogoutAsync, tasks, calendar, today);
        var window = new MainWindow(viewModel);
        _mainWindow = window;
        _mainSessionService = sessionService;
        sessionService.StateChanged += OnMainSessionStateChanged;
        MainWindow = window;
        window.Closed += OnMainWindowClosed;
        window.Show();
        ApplyMainSessionState(sessionService);

        if (_authWindow is not null)
        {
            var authWindow = _authWindow;
            _authWindow = null;
            authWindow.Closed -= OnAuthWindowClosed;
            authWindow.Close();
        }
    }

    private void OnAuthWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _authWindow))
        {
            return;
        }

        _authWindow = null;
        if (_workflow?.IsReady != true)
        {
            BeginShutdown();
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            if (window.DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            window.DataContext = null;
        }

        if (!ReferenceEquals(sender, _mainWindow))
        {
            return;
        }

        DetachMainSession();
        _mainWindow = null;
        if (_workflow?.IsReady == true)
        {
            BeginShutdown();
        }
    }

    private void OnMainSessionStateChanged(SessionAuthState _)
    {
        var sessionService = _mainSessionService;
        if (sessionService is null || _isShuttingDown)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => ApplyMainSessionState(sessionService));
    }

    private void ApplyMainSessionState(SessionService sessionService)
    {
        if (!ReferenceEquals(sessionService, _mainSessionService)
            || _mainWindow?.DataContext is not MainWindowViewModel { Tasks: not null } viewModel)
        {
            return;
        }

        var signedIn = sessionService.CurrentState == SessionAuthState.SignedIn;
        if (signedIn)
        {
            viewModel.Tasks.UpdateCapabilities(
                sessionService.CurrentSessionMetadata?.Capabilities);
            viewModel.Calendar?.UpdateCapabilities(
                sessionService.CurrentSessionMetadata?.Capabilities);
            viewModel.Today?.UpdateCapabilities(
                sessionService.CurrentSessionMetadata?.Capabilities);
        }

        viewModel.Tasks.UpdateSessionState(signedIn);
        // A token refresh keeps the authenticated session; clearing it here would
        // discard an open calendar/recurrence editor every refresh interval.
        viewModel.Calendar?.UpdateSessionState(signedIn || sessionService.CurrentState == SessionAuthState.Refreshing);
        viewModel.Today?.UpdateSessionState(signedIn || sessionService.CurrentState == SessionAuthState.Refreshing);
    }

    private void DetachMainSession()
    {
        if (_mainSessionService is null)
        {
            return;
        }

        _mainSessionService.StateChanged -= OnMainSessionStateChanged;
        _mainSessionService = null;
    }

    private void BeginShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _startupCancellation.Cancel();
        Shutdown();
    }
}
