using System.IO;
using Task.Desktop.Security;

namespace Task.Desktop.ViewModels;

/// <summary>Blocking desktop authentication workflow states.</summary>
public enum AuthWorkflowState
{
    Starting,
    ServerSetup,
    RestoringSession,
    Login,
    PasswordChangeRequired,
    Recovery,
    Ready,
}

/// <summary>
/// Coordinates startup, endpoint selection, login, mandatory password change, recovery,
/// logout and terminal background sign-out. The main shell may be opened only in
/// <see cref="AuthWorkflowState.Ready"/>.
/// </summary>
public sealed class AuthWorkflowViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopServerSettingsStore _settingsStore;
    private readonly DesktopServerProbeClient _probeClient;
    private readonly DesktopCredentialVault _vault;
    private readonly Func<Uri, SessionService> _sessionServiceFactory;
    private readonly SynchronizationContext? _uiContext;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private AuthWorkflowState _currentState = AuthWorkflowState.Starting;
    private ViewModelBase? _currentViewModel;
    private string? _statusMessage = "Проверяем сохранённую сессию…";
    private Uri? _serverEndpoint;
    private SessionService? _sessionService;
    private bool _disposed;

    public AuthWorkflowViewModel(
        DesktopServerSettingsStore settingsStore,
        DesktopServerProbeClient probeClient,
        DesktopCredentialVault vault,
        Func<Uri, SessionService> sessionServiceFactory,
        SynchronizationContext? uiContext = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _probeClient = probeClient ?? throw new ArgumentNullException(nameof(probeClient));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _sessionServiceFactory = sessionServiceFactory ?? throw new ArgumentNullException(nameof(sessionServiceFactory));
        _uiContext = uiContext ?? SynchronizationContext.Current;
    }

    public event Action? Ready;

    public AuthWorkflowState CurrentState
    {
        get => _currentState;
        private set
        {
            if (SetProperty(ref _currentState, value))
            {
                OnPropertyChanged(nameof(IsReady));
            }
        }
    }

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Uri? ServerEndpoint
    {
        get => _serverEndpoint;
        private set => SetProperty(ref _serverEndpoint, value);
    }

    /// <summary>The sole workflow condition that permits creation of the main shell.</summary>
    public bool IsReady => CurrentState == AuthWorkflowState.Ready;

    public ServerSetupViewModel? ServerSetup => CurrentViewModel as ServerSetupViewModel;

    public LoginViewModel? Login => CurrentViewModel as LoginViewModel;

    public PasswordChangeViewModel? PasswordChange => CurrentViewModel as PasswordChangeViewModel;

    public SessionRecoveryViewModel? Recovery => CurrentViewModel as SessionRecoveryViewModel;

    /// <summary>Runs the startup decision tree exactly once at a time.</summary>
    public async global::System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
        try
        {
            StatusMessage = "Проверяем сохранённую сессию…";
            CurrentState = AuthWorkflowState.Starting;

            var endpoint = _settingsStore.Load();
            if (endpoint is null)
            {
                ShowServerSetup(null);
                return;
            }

            ServerEndpoint = endpoint;
            ReplaceSessionService(_sessionServiceFactory(endpoint));
            if (_vault.GetRefreshToken() is null)
            {
                ShowLogin();
                return;
            }

            CurrentState = AuthWorkflowState.RestoringSession;
            StatusMessage = "Восстанавливаем сохранённую сессию…";
            var restore = await _sessionService!
                .RestoreAsync(linkedCancellation.Token)
                .ConfigureAwait(true);
            ApplyReadiness(restore.Readiness, RecoveryMessageFor(restore.Refresh));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Best-effort server logout followed by an unconditional local sign-out.</summary>
    public async global::System.Threading.Tasks.Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _operationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
        try
        {
            if (_sessionService is not null)
            {
                await _sessionService.LogoutAsync().ConfigureAwait(true);
            }
            else
            {
                _vault.Clear();
            }

            ShowLogin("Вы вышли из Task.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        (CurrentViewModel as IDisposable)?.Dispose();
        if (_sessionService is not null)
        {
            _sessionService.SignedOut -= OnSessionSignedOut;
            _sessionService.Dispose();
        }

        _lifetimeCancellation.Dispose();
    }

    private async global::System.Threading.Tasks.Task<WorkflowOperationResult> UseVerifiedEndpointAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settingsStore.SaveVerifiedEndpoint(endpoint, _vault);
            ServerEndpoint = endpoint;
            ReplaceSessionService(_sessionServiceFactory(endpoint));
            ShowLogin();
            return new WorkflowOperationResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new WorkflowOperationResult(
                false,
                "Не удалось сохранить адрес сервера. Проверьте доступ к локальным настройкам.");
        }
    }

    private async global::System.Threading.Tasks.Task ChangeServerAsync(CancellationToken cancellationToken)
    {
        if (_sessionService is not null)
        {
            await _sessionService
                .ClearLocalSessionForServerChangeAsync(cancellationToken)
                .ConfigureAwait(true);
            _sessionService.SignedOut -= OnSessionSignedOut;
            _sessionService.Dispose();
            _sessionService = null;
        }
        else
        {
            _vault.Clear();
        }

        ShowServerSetup(ServerEndpoint);
    }

    private async global::System.Threading.Tasks.Task<WorkflowOperationResult> SignInAsync(
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var service = _sessionService;
        if (service is null)
        {
            ShowServerSetup(ServerEndpoint);
            return new WorkflowOperationResult(false, "Сначала настройте адрес сервера.");
        }

        var result = await service
            .LoginAsync(login, password, Guid.NewGuid().ToString("D"), cancellationToken)
            .ConfigureAwait(true);
        switch (result)
        {
            case LoginResult.Succeeded:
                return ApplyCurrentReadiness(
                    "Не удалось подтвердить состояние сессии. Повторите проверку или выйдите.");

            case LoginResult.AuthError { Error: var error }:
                return new WorkflowOperationResult(false, MapLoginError(error));

            case LoginResult.NetworkFailure:
                return new WorkflowOperationResult(
                    false,
                    "Сервер недоступен. Проверьте подключение и повторите вход.");

            default:
                return new WorkflowOperationResult(
                    false,
                    "Сервер вернул неожиданный ответ. Вход не выполнен.");
        }
    }

    private async global::System.Threading.Tasks.Task<WorkflowOperationResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var service = _sessionService;
        if (service is null)
        {
            ShowLogin("Сессия недоступна. Войдите снова.");
            return new WorkflowOperationResult(false, "Сессия недоступна. Войдите снова.");
        }

        var result = await service
            .ChangePasswordAsync(currentPassword, newPassword, cancellationToken)
            .ConfigureAwait(true);
        switch (result.ChangePassword)
        {
            case ChangePasswordResult.Succeeded:
                if (result.Readiness is not null)
                {
                    ApplyReadiness(
                        result.Readiness,
                        "Пароль изменён, но состояние сессии не подтверждено. " +
                        "Повторите проверку или выйдите.");
                }

                return result.Readiness is SessionReadinessResult.Ready
                    ? new WorkflowOperationResult(true)
                    : new WorkflowOperationResult(
                        false,
                        "Переход в Task заблокирован до подтверждения состояния сессии.");

            case ChangePasswordResult.AuthError { Error.ProblemCode: AuthProblemCode.InvalidCredentials }:
                return new WorkflowOperationResult(false, "Неверно указан текущий пароль.");

            case ChangePasswordResult.AuthError { Error.ProblemCode: AuthProblemCode.ValidationFailed }:
                return new WorkflowOperationResult(
                    false,
                    "Новый пароль не соответствует политике или уже использовался ранее.");

            case ChangePasswordResult.AuthError { Error: var error }:
                return new WorkflowOperationResult(false, MapPasswordChangeError(error));

            case ChangePasswordResult.NetworkFailure:
                return new WorkflowOperationResult(
                    false,
                    "Сервер недоступен. Пароль не подтверждён; повторите попытку.");

            default:
                return new WorkflowOperationResult(
                    false,
                    "Сервер вернул неожиданный ответ. Пароль не подтверждён.");
        }
    }

    private async global::System.Threading.Tasks.Task<WorkflowOperationResult> RetryRecoveryAsync(
        CancellationToken cancellationToken)
    {
        var service = _sessionService;
        if (service is null)
        {
            ShowLogin("Сессия недоступна. Войдите снова.");
            return new WorkflowOperationResult(false, "Сессия недоступна. Войдите снова.");
        }

        var refresh = await service.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (refresh is RefreshResult.Succeeded)
        {
            return ApplyCurrentReadiness(
                "Не удалось подтвердить состояние сессии. Повторите проверку или выйдите.");
        }

        if (service.CurrentState == SessionAuthState.SignedOut)
        {
            return new WorkflowOperationResult(false, MapSignOutReason(service.LastSignOutReason));
        }

        var message = refresh switch
        {
            RefreshResult.NetworkFailure =>
                "Сервер недоступен. Сохранённая сессия не удалена; повторите проверку позже.",
            RefreshResult.MalformedResponse =>
                "Сервер вернул неожиданный ответ. Сохранённая сессия не удалена.",
            RefreshResult.AuthError { Error.RetryAfterSeconds: var seconds } when seconds.HasValue =>
                $"Проверка временно ограничена. Повторите через {seconds.Value} сек.",
            _ => "Сессию пока не удалось подтвердить. Повторите проверку позже.",
        };
        if (Recovery is not null)
        {
            Recovery.Message = message;
        }

        return new WorkflowOperationResult(false, message);
    }

    private WorkflowOperationResult ApplyCurrentReadiness(string retryableMessage)
    {
        var service = _sessionService;
        if (service is null)
        {
            return new WorkflowOperationResult(false, "Сессия недоступна.");
        }

        switch (service.CurrentReadiness)
        {
            case SessionReadinessState.Ready:
                ShowReady();
                return new WorkflowOperationResult(true);

            case SessionReadinessState.PasswordChangeRequired:
                ShowPasswordChange();
                return new WorkflowOperationResult(true);

            case SessionReadinessState.Unavailable:
                ShowRecovery(retryableMessage);
                return new WorkflowOperationResult(true);

            default:
                return new WorkflowOperationResult(
                    false,
                    MapSignOutReason(service.LastSignOutReason));
        }
    }

    private void ApplyReadiness(SessionReadinessResult readiness, string retryableMessage)
    {
        switch (readiness)
        {
            case SessionReadinessResult.Ready:
                ShowReady();
                break;

            case SessionReadinessResult.PasswordChangeRequired:
                ShowPasswordChange();
                break;

            case SessionReadinessResult.RetryableFailure:
                ShowRecovery(retryableMessage);
                break;

            case SessionReadinessResult.SignedOut { Reason: var reason }:
                ShowLogin(MapSignOutReason(reason));
                break;
        }
    }

    private void ShowServerSetup(Uri? initialEndpoint)
    {
        StatusMessage = null;
        SwitchViewModel(
            AuthWorkflowState.ServerSetup,
            new ServerSetupViewModel(_probeClient, UseVerifiedEndpointAsync, initialEndpoint));
    }

    private void ShowLogin(string? message = null)
    {
        StatusMessage = null;
        SwitchViewModel(
            AuthWorkflowState.Login,
            new LoginViewModel(SignInAsync, ChangeServerAsync, initialLogin: null, message));
    }

    private void ShowPasswordChange()
    {
        StatusMessage = null;
        SwitchViewModel(
            AuthWorkflowState.PasswordChangeRequired,
            new PasswordChangeViewModel(ChangePasswordAsync, LogoutAsync));
    }

    private void ShowRecovery(string message)
    {
        StatusMessage = null;
        SwitchViewModel(
            AuthWorkflowState.Recovery,
            new SessionRecoveryViewModel(message, RetryRecoveryAsync, LogoutAsync));
    }

    private void ShowReady()
    {
        StatusMessage = "Сессия подтверждена.";
        SwitchViewModel(AuthWorkflowState.Ready, null);
        Ready?.Invoke();
    }

    private void SwitchViewModel(AuthWorkflowState state, ViewModelBase? viewModel)
    {
        var previous = CurrentViewModel;
        CurrentViewModel = viewModel;
        CurrentState = state;
        OnPropertyChanged(nameof(ServerSetup));
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(PasswordChange));
        OnPropertyChanged(nameof(Recovery));
        (previous as IDisposable)?.Dispose();
    }

    private void ReplaceSessionService(SessionService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_sessionService is not null)
        {
            _sessionService.SignedOut -= OnSessionSignedOut;
            _sessionService.Dispose();
        }

        _sessionService = service;
        _sessionService.SignedOut += OnSessionSignedOut;
    }

    private void OnSessionSignedOut(SessionSignOutReason reason)
    {
        if (_disposed || reason == SessionSignOutReason.ServerChanged)
        {
            return;
        }

        Dispatch(() => ShowLogin(MapSignOutReason(reason)));
    }

    private void Dispatch(Action action)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return;
        }

        _uiContext.Post(_ =>
        {
            if (!_disposed)
            {
                action();
            }
        }, null);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string RecoveryMessageFor(RefreshResult? refresh) => refresh switch
    {
        RefreshResult.NetworkFailure =>
            "Сервер недоступен. Сохранённая сессия не удалена; повторите проверку или выйдите.",
        RefreshResult.MalformedResponse =>
            "Сервер вернул неожиданный ответ. Сохранённая сессия не удалена; повторите проверку или выйдите.",
        _ => "Не удалось подтвердить состояние сессии. Повторите проверку или выйдите.",
    };

    private static string MapLoginError(AuthErrorResult error) => error.ProblemCode switch
    {
        AuthProblemCode.InvalidCredentials => "Неверный логин или пароль.",
        AuthProblemCode.AccountBlocked =>
            "Учётная запись заблокирована. Обратитесь к администратору.",
        AuthProblemCode.AccountLockedTemporarily => error.RetryAfterSeconds.HasValue
            ? $"Вход временно заблокирован. Повторите через {error.RetryAfterSeconds.Value} сек."
            : "Вход временно заблокирован. Повторите позже.",
        AuthProblemCode.RateLimited => error.RetryAfterSeconds.HasValue
            ? $"Слишком много попыток входа. Повторите через {error.RetryAfterSeconds.Value} сек."
            : "Слишком много попыток входа. Повторите позже.",
        _ => "Вход отклонён из соображений безопасности. Обратитесь к администратору.",
    };

    private static string MapPasswordChangeError(AuthErrorResult error) => error.ProblemCode switch
    {
        AuthProblemCode.AccountBlocked =>
            "Учётная запись заблокирована. Выполнен локальный выход.",
        AuthProblemCode.SessionExpired or AuthProblemCode.SessionRevoked
            or AuthProblemCode.RefreshTokenReuse or AuthProblemCode.AuthenticationRequired =>
            "Сессия завершена. Войдите снова.",
        _ => "Операция отклонена из соображений безопасности. Войдите снова.",
    };

    private static string MapSignOutReason(SessionSignOutReason? reason) => reason switch
    {
        SessionSignOutReason.NoStoredSession => "Сохранённая сессия отсутствует. Выполните вход.",
        SessionSignOutReason.NoDeviceKey => "Сохранённая сессия повреждена. Выполните вход снова.",
        SessionSignOutReason.SessionExpired => "Срок действия сессии истёк. Войдите снова.",
        SessionSignOutReason.SessionRevoked => "Сессия завершена администратором. Войдите снова.",
        SessionSignOutReason.RefreshTokenReuse =>
            "Сессия завершена из соображений безопасности. Войдите снова.",
        SessionSignOutReason.AccountBlocked =>
            "Учётная запись заблокирована. Обратитесь к администратору.",
        SessionSignOutReason.AuthenticationRequired => "Сессия больше недействительна. Войдите снова.",
        SessionSignOutReason.ServerChanged => "Адрес сервера изменён. Выполните вход снова.",
        _ => "Сессия завершена из соображений безопасности. Войдите снова.",
    };
}
