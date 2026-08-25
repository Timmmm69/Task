using System.Configuration;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using Task.Desktop.Security;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop;

/// <summary>Desktop composition root and authentication-aware window coordinator.</summary>
public partial class App : global::System.Windows.Application
{
    private readonly List<HttpClient> _ownedHttpClients = [];
    private readonly CancellationTokenSource _startupCancellation = new();

    private AuthWorkflowViewModel? _workflow;
    private AuthWindow? _authWindow;
    private MainWindow? _mainWindow;
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

        if (_mainWindow is not null)
        {
            var window = _mainWindow;
            _mainWindow = null;
            window.Close();
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
        var tasks = new TasksViewModel(tasksClient);
        var viewModel = new MainWindowViewModel(serverEndpoint, workflow.LogoutAsync, tasks);
        var window = new MainWindow(viewModel);
        _mainWindow = window;
        MainWindow = window;
        window.Closed += OnMainWindowClosed;
        window.Show();

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
        if (sender is MainWindow window && window.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (!ReferenceEquals(sender, _mainWindow))
        {
            return;
        }

        _mainWindow = null;
        if (_workflow?.IsReady == true)
        {
            BeginShutdown();
        }
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
