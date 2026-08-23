using System.Security.Cryptography;
using System.Threading;

namespace Task.Desktop.Security;

/// <summary>Authentication state of the desktop session, exposed to the UI.</summary>
public enum SessionAuthState
{
    /// <summary>No session; the user is not signed in.</summary>
    SignedOut,

    /// <summary>A login attempt is in progress.</summary>
    SigningIn,

    /// <summary>A session is active and its tokens are usable.</summary>
    SignedIn,

    /// <summary>The session tokens are being rotated in the background.</summary>
    Refreshing,

    /// <summary>A saved session is being restored during startup.</summary>
    Restoring,

    /// <summary>Tokens exist, but current session metadata is not confirmed.</summary>
    SessionUnavailable,
}

/// <summary>Readiness of the authenticated session for entering the main shell.</summary>
public enum SessionReadinessState
{
    NotAuthenticated,
    Verifying,
    Ready,
    PasswordChangeRequired,
    Unavailable,
}

/// <summary>Reason a session ended without an explicit user logout.</summary>
public enum SessionSignOutReason
{
    /// <summary>No stored session exists (the vault is empty or its file was rejected).</summary>
    NoStoredSession,

    /// <summary>The stored session entry has no device key.</summary>
    NoDeviceKey,

    /// <summary>The server reported the session as expired.</summary>
    SessionExpired,

    /// <summary>The server reported that the session was revoked.</summary>
    SessionRevoked,

    /// <summary>The server detected refresh-token reuse and revoked the token family.</summary>
    RefreshTokenReuse,

    /// <summary>The account is blocked and the session cannot continue.</summary>
    AccountBlocked,

    /// <summary>The server no longer accepts the current authentication.</summary>
    AuthenticationRequired,

    /// <summary>The user selected a different server endpoint.</summary>
    ServerChanged,

    /// <summary>The server returned a terminal error this client does not recognize.</summary>
    Unknown,
}

/// <summary>Typed result of confirming current session metadata.</summary>
public abstract record SessionReadinessResult
{
    public sealed record Ready(CurrentSessionResponse Session) : SessionReadinessResult;
    public sealed record PasswordChangeRequired(CurrentSessionResponse Session) : SessionReadinessResult;
    public sealed record RetryableFailure(GetSessionResult Failure) : SessionReadinessResult;
    public sealed record SignedOut(SessionSignOutReason Reason) : SessionReadinessResult;
}

/// <summary>Typed startup restore result.</summary>
public sealed record SessionRestoreResult(RefreshResult? Refresh, SessionReadinessResult Readiness);

/// <summary>Typed password-change result plus post-change session confirmation.</summary>
public sealed record SessionPasswordChangeResult(
    ChangePasswordResult ChangePassword,
    SessionReadinessResult? Readiness);

/// <summary>
/// Orchestrates the desktop session lifecycle: login, background token refresh with
/// single-flight semantics, server-side logout and local sign-out. UI-agnostic: it exposes
/// state and events only and has no WPF or view-model dependencies.
///
/// Responsibilities and limits:
/// <list type="bullet">
/// <item>The persistent device key lives only in the credential vault: it is read from an
/// existing vault entry or generated once (base64url, no padding) and never leaves the
/// vault except inside the serialized JSON request bodies.</item>
/// <item>The access token is kept in memory by the vault; the refresh token and the device
/// key are persisted by the vault. The service stores no secrets of its own.</item>
/// <item>A background <see cref="System.Threading.Timer"/> refreshes the session before the
/// access token expires (<c>accessExpiresAt - refreshMargin</c>, clamped to a 5-second
/// minimum). After a retryable refresh failure the refresh is re-attempted after
/// <c>retryDelay</c>. The timer is stopped on sign-out and after logout.</item>
/// <item>Refresh is single-flight: concurrent callers share one in-flight request and one
/// result; the timer re-uses whatever refresh is already running.</item>
/// <item>Terminal refresh failures (expired session, token reuse, blocked account,
/// unrecognized codes) clear the vault and sign the user out locally. Retryable failures
/// (rate limit, temporary lock, transport, malformed responses) keep the previous tokens
/// and schedule a retry.</item>
/// <item>Logout revokes the session on the server best-effort (the server result is
/// returned, but a failure never blocks the local sign-out) and always clears the vault.</item>
/// <item>No logging anywhere: secrets and correlation ids never reach logs, and the client
/// has no logging at all.</item>
/// </list>
///
/// Thread safety: state, the current session, the refresh schedule and the timer are
/// guarded by a private lock; <see cref="StateChanged"/> is raised after the transition,
/// outside the lock, so handlers may call back into the service. <see cref="RefreshAsync"/>
/// serializes network work with a semaphore and coalesces concurrent callers onto a single
/// in-flight task; <see cref="LoginAsync"/> and <see cref="LogoutAsync"/> take the same
/// semaphore so a late rotation can never overwrite the session established by a login or
/// re-populate the vault after a logout.
/// </summary>
public sealed class SessionService : IDisposable
{
    /// <summary>Floor for the scheduled refresh delay; also the clamp for expired tokens.</summary>
    private static readonly TimeSpan MinRefreshDelay = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DesktopAuthApiClient _client;
    private readonly DesktopCredentialVault _vault;
    private readonly string _deviceName;
    private readonly ClientPlatform _platform;
    private readonly string _appVersion;
    private readonly string? _osVersion;
    private readonly TimeSpan _refreshMargin;
    private readonly int _deviceKeyByteLength;
    private readonly TimeSpan _retryDelay;

    private SessionAuthState _state = SessionAuthState.SignedOut;
    private SessionTokensResponse? _currentSession;
    private CurrentSessionResponse? _currentSessionMetadata;
    private bool _currentMustChangePassword;
    private SessionReadinessState _readiness = SessionReadinessState.NotAuthenticated;
    private SessionReadinessResult? _lastReadinessResult;
    private SessionSignOutReason? _lastSignOutReason;
    private TimeSpan? _nextRefreshDelay;
    private Timer? _refreshTimer;
    private Task<RefreshResult>? _refreshInFlight;
    private bool _disposed;

    /// <summary>
    /// Creates a session service for the given client and vault.
    /// </summary>
    /// <param name="client">HTTP client for the auth endpoints.</param>
    /// <param name="vault">Credential vault holding the refresh token, the device key and the
    /// in-memory access token.</param>
    /// <param name="deviceName">Human-readable device name sent in device registration.</param>
    /// <param name="platform">Platform of the client sent in device registration.</param>
    /// <param name="appVersion">Version of the desktop application sent in device registration.</param>
    /// <param name="osVersion">Operating system version sent in device registration; <c>null</c>
    /// when unknown.</param>
    /// <param name="refreshMargin">How long before <c>accessExpiresAt</c> the background refresh
    /// fires; defaults to 2 minutes. <c>null</c> selects the default.</param>
    /// <param name="deviceKeyByteLength">Length in bytes of the generated device key
    /// (base64url-encoded); defaults to 32.</param>
    /// <param name="retryDelay">Fixed delay after a retryable refresh failure; defaults to
    /// 5 minutes. <c>null</c> selects the default.</param>
    public SessionService(
        DesktopAuthApiClient client,
        DesktopCredentialVault vault,
        string deviceName,
        ClientPlatform platform,
        string appVersion,
        string? osVersion = null,
        TimeSpan? refreshMargin = null,
        int deviceKeyByteLength = 32,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        if (refreshMargin.HasValue && refreshMargin.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshMargin));
        }

        if (retryDelay.HasValue && retryDelay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        if (deviceKeyByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceKeyByteLength));
        }

        _client = client;
        _vault = vault;
        _deviceName = deviceName;
        _platform = platform;
        _appVersion = appVersion;
        _osVersion = osVersion;
        _refreshMargin = refreshMargin ?? TimeSpan.FromMinutes(2);
        _deviceKeyByteLength = deviceKeyByteLength;
        _retryDelay = retryDelay ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Raised after <see cref="CurrentState"/> transitions to a new value. The handler is
    /// invoked outside the internal lock; rapid successive transitions may deliver events
    /// out of order, so handlers should treat the argument as the current state indicator.
    /// </summary>
    public event Action<SessionAuthState>? StateChanged;

    /// <summary>Raised after a terminal/local sign-out, outside the internal state lock.</summary>
    public event Action<SessionSignOutReason>? SignedOut;

    /// <summary>Current authentication state.</summary>
    public SessionAuthState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Tokens of the last successful login or refresh; <c>null</c> after sign-out or logout.
    /// </summary>
    public SessionTokensResponse? CurrentSession
    {
        get
        {
            lock (_sync)
            {
                return _currentSession;
            }
        }
    }

    /// <summary>
    /// Whether the account must replace its password. This value changes only after the server
    /// confirms session metadata; an unavailable or malformed response moves readiness to
    /// <see cref="SessionReadinessState.Unavailable"/> and cannot unlock the main shell.
    /// </summary>
    public bool CurrentMustChangePassword
    {
        get
        {
            lock (_sync)
            {
                return _currentMustChangePassword;
            }
        }
    }

    /// <summary>Current session metadata; non-null only after server confirmation.</summary>
    public CurrentSessionResponse? CurrentSessionMetadata
    {
        get
        {
            lock (_sync)
            {
                return _currentSessionMetadata;
            }
        }
    }

    /// <summary>Whether the session is safe to use for navigation.</summary>
    public SessionReadinessState CurrentReadiness
    {
        get
        {
            lock (_sync)
            {
                return _readiness;
            }
        }
    }

    /// <summary>Last terminal/local sign-out reason, cleared when a session becomes ready.</summary>
    public SessionSignOutReason? LastSignOutReason
    {
        get
        {
            lock (_sync)
            {
                return _lastSignOutReason;
            }
        }
    }

    /// <summary>
    /// Delay until the next background refresh attempt (exposed for tests); <c>null</c> when
    /// nothing is scheduled, i.e. after sign-out or logout or before the first successful
    /// login.
    /// </summary>
    public TimeSpan? NextRefreshDelay
    {
        get
        {
            lock (_sync)
            {
                return _nextRefreshDelay;
            }
        }
    }

    /// <summary>
    /// Signs the user in. On success the session tokens and the persistent device key are
    /// stored in the vault and session metadata is confirmed before readiness is granted. When
    /// metadata cannot be confirmed, tokens remain available for retry but the main shell stays
    /// blocked. On a login-request failure the previous state is left untouched.
    /// </summary>
    /// <param name="login">Account login.</param>
    /// <param name="password">Account password; travels only inside the JSON request body.</param>
    /// <param name="correlationId">Correlation identifier sent in the <c>X-Correlation-ID</c>
    /// header of the login request.</param>
    /// <param name="cancellationToken">Cancellation token propagated to the client.</param>
    /// <returns>The original client outcome of the login attempt.</returns>
    public async Task<LoginResult> LoginAsync(
        string login,
        string password,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        // Serialize with any in-flight refresh so a late rotation cannot overwrite
        // the session this call establishes.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        var storedNewSession = false;
        try
        {
            var previousState = CurrentState;
            SetState(SessionAuthState.SigningIn);

            try
            {
                var deviceKey = GetOrCreateDeviceKey();
                var device = new DeviceRegistrationInfo(deviceKey, _deviceName, _platform, _appVersion, _osVersion);
                var result = await _client
                    .LoginAsync(login, password, device, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                if (result is LoginResult.Succeeded { Tokens: var tokens })
                {
                    // The desktop client is single-org: orgId is stored empty and unused.
                    _vault.SaveRefreshToken(
                        tokens.SessionId.ToString("D"),
                        string.Empty,
                        login,
                        deviceKey,
                        tokens.RefreshToken);
                    _vault.SetAccessToken(tokens.AccessToken);
                    storedNewSession = true;
                    lock (_sync)
                    {
                        _currentSession = tokens;
                    }

                    var readiness = await ConfirmSessionAsync(tokens, cancellationToken).ConfigureAwait(false);
                    if (readiness is not SessionReadinessResult.SignedOut)
                    {
                        ScheduleRefresh(tokens);
                    }
                }
                else
                {
                    SetState(previousState);
                }

                return result;
            }
            catch
            {
                if (storedNewSession)
                {
                    _vault.Clear();
                    ClearSessionState();
                }

                SetState(previousState);
                throw;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Restores a saved session by rotating its token and confirming metadata.</summary>
    public async Task<SessionRestoreResult> RestoreAsync(CancellationToken cancellationToken)
    {
        if (_vault.GetRefreshToken() is null)
        {
            SignOut(SessionSignOutReason.NoStoredSession);
            return new SessionRestoreResult(
                null,
                new SessionReadinessResult.SignedOut(SessionSignOutReason.NoStoredSession));
        }

        SetState(SessionAuthState.Restoring);
        var refresh = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        SessionReadinessResult readiness;
        lock (_sync)
        {
            readiness = _lastReadinessResult
                ?? new SessionReadinessResult.RetryableFailure(new GetSessionResult.NetworkFailure());
        }

        return new SessionRestoreResult(refresh, readiness);
    }

    /// <summary>
    /// Changes the password, rotates the still-current refresh token and then re-reads session
    /// metadata. The password-change requirement is cleared only after the server confirms
    /// <c>mustChangePassword=false</c> with the new access token.
    /// </summary>
    public async Task<SessionPasswordChangeResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accessToken = _vault.GetAccessToken();
            var tokens = CurrentSession;
            if (string.IsNullOrWhiteSpace(accessToken) || tokens is null)
            {
                const SessionSignOutReason reason = SessionSignOutReason.AuthenticationRequired;
                SignOut(reason);
                return new SessionPasswordChangeResult(
                    new ChangePasswordResult.AuthError(
                        new AuthErrorResult(AuthProblemCode.AuthenticationRequired, null)),
                    new SessionReadinessResult.SignedOut(reason));
            }

            var result = await _client
                .ChangePasswordAsync(accessToken, currentPassword, newPassword, cancellationToken)
                .ConfigureAwait(false);
            if (result is ChangePasswordResult.Succeeded)
            {
                lock (_sync)
                {
                    _readiness = SessionReadinessState.Verifying;
                    _currentSessionMetadata = null;
                    _lastReadinessResult = null;
                }

                var refresh = await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
                var readiness = GetPostPasswordChangeReadiness(refresh);
                return new SessionPasswordChangeResult(result, readiness);
            }

            if (result is ChangePasswordResult.AuthError { Error.ProblemCode: var code }
                && IsTerminalAuthError(code))
            {
                var reason = MapSignOutReason(code);
                SignOut(reason);
                return new SessionPasswordChangeResult(
                    result,
                    new SessionReadinessResult.SignedOut(reason));
            }

            return new SessionPasswordChangeResult(result, null);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private SessionReadinessResult GetPostPasswordChangeReadiness(RefreshResult refresh)
    {
        lock (_sync)
        {
            if (_lastReadinessResult is not null)
            {
                return _lastReadinessResult;
            }
        }

        GetSessionResult failure = refresh switch
        {
            RefreshResult.NetworkFailure => new GetSessionResult.NetworkFailure(),
            RefreshResult.MalformedResponse => new GetSessionResult.MalformedResponse(),
            RefreshResult.AuthError { Error: var error } => new GetSessionResult.AuthError(error),
            _ => new GetSessionResult.MalformedResponse(),
        };
        var readiness = new SessionReadinessResult.RetryableFailure(failure);
        lock (_sync)
        {
            _readiness = SessionReadinessState.Unavailable;
            _lastReadinessResult = readiness;
        }

        SetState(SessionAuthState.SessionUnavailable);
        return readiness;
    }

    /// <summary>
    /// Clears all local session material without contacting the current server. This is used
    /// before switching endpoints and is serialized with refresh/login/logout work so no late
    /// request can repopulate the old server's credentials.
    /// </summary>
    public async global::System.Threading.Tasks.Task ClearLocalSessionForServerChangeAsync(
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SignOut(SessionSignOutReason.ServerChanged);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Rotates the session with the stored refresh token and device key, or signs the user
    /// out when no usable stored session exists. Single-flight: concurrent callers share
    /// the in-flight request and its result, and the background timer re-uses a refresh
    /// that is already running.
    /// </summary>
    /// <returns>The client outcome of the refresh attempt. On local sign-out
    /// (<see cref="SessionSignOutReason.NoStoredSession"/> /
    /// <see cref="SessionSignOutReason.NoDeviceKey"/>) an
    /// <see cref="RefreshResult.AuthError"/> with
    /// <see cref="AuthProblemCode.SessionExpired"/> is returned, since no request was sent.</returns>
    public Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_refreshInFlight is not null)
            {
                return _refreshInFlight;
            }

            _refreshInFlight = RefreshCoreAsync(cancellationToken);
            return _refreshInFlight;
        }
    }

    /// <summary>
    /// Signs the user out: revokes the session on the server best-effort (only when an
    /// access token is present; the returned result is informational and a failure never
    /// blocks the local sign-out), then clears the vault and transitions to
    /// <see cref="SessionAuthState.SignedOut"/>.
    /// </summary>
    /// <returns>The server logout outcome, or <see cref="LogoutResult.Succeeded"/> when
    /// there was no access token to revoke.</returns>
    public async Task<LogoutResult> LogoutAsync()
    {
        // Serialize with any in-flight refresh so a late rotation cannot re-populate
        // the vault after the local sign-out below.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            LogoutResult result;
            var accessToken = _vault.GetAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                result = new LogoutResult.Succeeded();
            }
            else
            {
                result = await _client.LogoutAsync(accessToken, CancellationToken.None).ConfigureAwait(false);
            }

            _vault.Clear();
            ClearSessionState();
            SetState(SessionAuthState.SignedOut);
            return result;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }

        // Do not dispose the semaphore here: a window-close cancellation may race with an
        // operation that already owns it and must still execute its finally/Release path.
        // SemaphoreSlim owns no external resource; leaving it for GC avoids turning orderly
        // cancellation into an ObjectDisposedException on the UI dispatcher.
    }

    private async Task<RefreshResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        // RefreshAsync publishes the task while holding _sync. Yield before any state event so
        // callbacks are never invoked under that lock, even when the semaphore is immediately free.
        await global::System.Threading.Tasks.Task.Yield();
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
            lock (_sync)
            {
                _refreshInFlight = null;
            }
        }
    }

    private async Task<RefreshResult> RefreshNowAsync(CancellationToken cancellationToken)
    {
        var entry = _vault.GetRefreshToken();
        if (entry is null)
        {
            SignOut(SessionSignOutReason.NoStoredSession);
            return new RefreshResult.AuthError(new AuthErrorResult(AuthProblemCode.SessionExpired, null));
        }

        if (string.IsNullOrWhiteSpace(entry.DeviceKey))
        {
            SignOut(SessionSignOutReason.NoDeviceKey);
            return new RefreshResult.AuthError(new AuthErrorResult(AuthProblemCode.SessionExpired, null));
        }

        SetState(SessionAuthState.Refreshing);

        var result = await _client
            .RefreshAsync(entry.RefreshToken, entry.DeviceKey, cancellationToken)
            .ConfigureAwait(false);

        switch (result)
        {
            case RefreshResult.Succeeded { Tokens: var tokens }:
                _vault.SaveRefreshToken(
                    tokens.SessionId.ToString("D"),
                    string.Empty,
                    entry.Login,
                    entry.DeviceKey,
                    tokens.RefreshToken);
                _vault.SetAccessToken(tokens.AccessToken);
                lock (_sync)
                {
                    _currentSession = tokens;
                }

                var readiness = await ConfirmSessionAsync(tokens, cancellationToken).ConfigureAwait(false);
                if (readiness is not SessionReadinessResult.SignedOut)
                {
                    ScheduleRefresh(tokens);
                }
                return result;

            case RefreshResult.AuthError { Error: var error }:
                if (IsTerminalAuthError(error.ProblemCode))
                {
                    SignOut(MapSignOutReason(error.ProblemCode));
                }
                else
                {
                    SetStateAfterRetryableFailure();
                    ScheduleRetry();
                }

                return result;

            case RefreshResult.NetworkFailure:
            case RefreshResult.MalformedResponse:
                // The previous tokens stay usable; retry on a fixed delay.
                SetStateAfterRetryableFailure();
                ScheduleRetry();
                return result;

            default:
                return result;
        }
    }

    private async Task<SessionReadinessResult> ConfirmSessionAsync(
        SessionTokensResponse tokens,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _readiness = SessionReadinessState.Verifying;
            _currentSessionMetadata = null;
        }

        var result = await _client.GetSessionAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        switch (result)
        {
            case GetSessionResult.Succeeded { Session: var session }
                when session.SessionId == tokens.SessionId:
                var readiness = session.MustChangePassword
                    ? (SessionReadinessResult)new SessionReadinessResult.PasswordChangeRequired(session)
                    : new SessionReadinessResult.Ready(session);
                lock (_sync)
                {
                    _currentSessionMetadata = session;
                    _currentMustChangePassword = session.MustChangePassword;
                    _readiness = session.MustChangePassword
                        ? SessionReadinessState.PasswordChangeRequired
                        : SessionReadinessState.Ready;
                    _lastReadinessResult = readiness;
                    _lastSignOutReason = null;
                }

                SetState(SessionAuthState.SignedIn);
                return readiness;

            case GetSessionResult.AuthError { Error.ProblemCode: var code }
                when IsTerminalAuthError(code):
                var reason = MapSignOutReason(code);
                SignOut(reason);
                return new SessionReadinessResult.SignedOut(reason);

            default:
                GetSessionResult failure = result is GetSessionResult.Succeeded
                    ? new GetSessionResult.MalformedResponse()
                    : result;
                var retryable = new SessionReadinessResult.RetryableFailure(failure);
                lock (_sync)
                {
                    _readiness = SessionReadinessState.Unavailable;
                    _lastReadinessResult = retryable;
                }

                SetState(SessionAuthState.SessionUnavailable);
                return retryable;
        }
    }

    private void SetStateAfterRetryableFailure()
    {
        lock (_sync)
        {
            if (_readiness is SessionReadinessState.NotAuthenticated or SessionReadinessState.Verifying)
            {
                _readiness = SessionReadinessState.Unavailable;
            }
        }

        SetState(CurrentReadiness == SessionReadinessState.Unavailable
            ? SessionAuthState.SessionUnavailable
            : SessionAuthState.SignedIn);
    }

    private string GetOrCreateDeviceKey()
    {
        var entry = _vault.GetRefreshToken();
        if (entry?.DeviceKey is { Length: > 0 } deviceKey)
        {
            return deviceKey;
        }

        return GenerateDeviceKey();
    }

    private string GenerateDeviceKey()
    {
        var bytes = new byte[_deviceKeyByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private void ScheduleRefresh(SessionTokensResponse tokens)
    {
        var delay = tokens.AccessExpiresAt - DateTimeOffset.UtcNow - _refreshMargin;
        if (delay < MinRefreshDelay)
        {
            delay = MinRefreshDelay;
        }

        ScheduleTimer(delay);
    }

    private void ScheduleRetry()
    {
        ScheduleTimer(_retryDelay);
    }

    private void ScheduleTimer(TimeSpan delay)
    {
        lock (_sync)
        {
            _refreshTimer?.Dispose();
            _nextRefreshDelay = delay;
            _refreshTimer = new Timer(OnRefreshDue, null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Clears the current session snapshot and stops the refresh timer; called on every
    /// transition to <see cref="SessionAuthState.SignedOut"/>.
    /// </summary>
    private void ClearSessionState()
    {
        lock (_sync)
        {
            _currentSession = null;
            _currentSessionMetadata = null;
            _currentMustChangePassword = false;
            _readiness = SessionReadinessState.NotAuthenticated;
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            _nextRefreshDelay = null;
        }
    }

    private void SignOut(SessionSignOutReason reason)
    {
        _vault.Clear();
        ClearSessionState();
        lock (_sync)
        {
            _lastSignOutReason = reason;
            _lastReadinessResult = new SessionReadinessResult.SignedOut(reason);
        }

        SetState(SessionAuthState.SignedOut);
        SignedOut?.Invoke(reason);
    }

    private void SetState(SessionAuthState newState)
    {
        SessionAuthState oldState;
        lock (_sync)
        {
            oldState = _state;
            if (oldState == newState)
            {
                return;
            }

            _state = newState;
        }

        StateChanged?.Invoke(newState);
    }

    private void OnRefreshDue(object? state)
    {
        _ = ObserveBackgroundRefreshAsync();
    }

    private async global::System.Threading.Tasks.Task ObserveBackgroundRefreshAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch
        {
            // A timer callback must never fault the process; the client has no logging.
        }
    }

    private static bool IsTerminalAuthError(AuthProblemCode code) => code switch
    {
        AuthProblemCode.SessionExpired => true,
        AuthProblemCode.SessionRevoked => true,
        AuthProblemCode.RefreshTokenReuse => true,
        AuthProblemCode.AccountBlocked => true,
        AuthProblemCode.AuthenticationRequired => true,
        // Unrecognized codes (including a future device-revoked code) are fail-closed.
        AuthProblemCode.Unknown => true,
        _ => false,
    };

    private static SessionSignOutReason MapSignOutReason(AuthProblemCode code) => code switch
    {
        AuthProblemCode.SessionExpired => SessionSignOutReason.SessionExpired,
        AuthProblemCode.SessionRevoked => SessionSignOutReason.SessionRevoked,
        AuthProblemCode.RefreshTokenReuse => SessionSignOutReason.RefreshTokenReuse,
        AuthProblemCode.AccountBlocked => SessionSignOutReason.AccountBlocked,
        AuthProblemCode.AuthenticationRequired => SessionSignOutReason.AuthenticationRequired,
        _ => SessionSignOutReason.Unknown,
    };
}
