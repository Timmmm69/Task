using Task.Application.Audit;

namespace Task.Application.Security;

/// <summary>
/// Client-supplied login request. DeviceKey is the raw client device key; DeviceFingerprintHash
/// is its SHA-256 hex digest (lowercase), computed by the caller. LoginService never hashes the
/// raw key itself and only hands the fingerprint hash to the device store, which enforces the
/// 32-256 character format rule. CorrelationId and RequestId trace the attempt in the audit
/// journal.
/// </summary>
public sealed record LoginCommand(
    string Login,
    string Password,
    string DeviceKey,
    string DeviceName,
    string DeviceFingerprintHash,
    Guid CorrelationId,
    Guid RequestId);

/// <summary>
/// Outcome of a login attempt. Succeeded carries everything the API layer needs to issue an
/// access token: the service itself never issues tokens. RawRefreshToken is presented exactly
/// once to the caller and is never persisted, logged or returned again.
/// </summary>
public abstract record LoginOutcome
{
    /// <summary>
    /// Login accepted; a session and its initial refresh token were created. The caller issues
    /// the access token from these data (JwtAccessTokenIssuer, API layer).
    /// </summary>
    public sealed record Succeeded(
        Guid SessionId,
        Guid UserId,
        Guid OrganizationId,
        long CredentialVersion,
        long AuthorizationScopeVersion,
        string RawRefreshToken,
        DateTimeOffset RefreshExpiresAtUtc,
        DateTimeOffset AbsoluteExpiresAtUtc,
        bool MustChangePassword = false) : LoginOutcome;

    /// <summary>
    /// The login does not identify an account or the password does not verify. The same outcome
    /// is returned for both cases so callers cannot distinguish them.
    /// </summary>
    public sealed record InvalidCredentials : LoginOutcome;

    /// <summary>The account is permanently blocked; no attempt is recorded.</summary>
    public sealed record AccountBlocked : LoginOutcome;

    /// <summary>The account is temporarily locked; Remaining is the time until the lock expires.</summary>
    public sealed record LockedTemporarily(TimeSpan Remaining) : LoginOutcome;

    /// <summary>The device fingerprint belongs to a revoked device; no session is created.</summary>
    public sealed record DeviceRevoked : LoginOutcome;
}

/// <summary>
/// Orchestrates the interactive login flow: account lookup with timing-equivalent rejection of
/// unknown logins, lockout enforcement, password verification, device registration, atomic
/// session creation and audit journaling. Fail-closed: passwords and tokens are never logged,
/// unknown-login and wrong-password paths are indistinguishable to callers, and a journal write
/// failure never fails a login (audit is best-effort). MustChangePassword is propagated from the
/// account record on success; callers (API/desktop, wave B) enforce the change — this service
/// does not block login when the flag is set.
/// </summary>
public sealed class LoginService
{
    /// <summary>
    /// Default idle session timeout (canon #16): a session expires after 8 hours without
    /// activity. Overridable through the constructor for tests and special deployments.
    /// </summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromHours(8);

    /// <summary>
    /// Default absolute session timeout (canon #16): a session expires 30 days after creation
    /// regardless of activity. Overridable through the constructor for tests and special
    /// deployments.
    /// </summary>
    public static readonly TimeSpan DefaultAbsoluteTimeout = TimeSpan.FromDays(30);

    private const string UserLoggedInActionCode = "UserLoggedIn";
    private const string LoginFailedActionCode = "LoginFailed";
    private const string SuccessOutcome = "success";
    private const string FailedOutcome = "failed";
    private const string InvalidCredentialsReasonCode = "INVALID_CREDENTIALS";
    private const string AccountBlockedReasonCode = "ACCOUNT_BLOCKED";
    private const string AccountLockedTemporarilyReasonCode = "ACCOUNT_LOCKED_TEMPORARILY";
    private const string DeviceRevokedReasonCode = "DEVICE_REVOKED";
    private const string StandardRedactionLevel = "standard";

    private readonly IAccountLookupStore _lookupStore;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AccountLockoutService _lockoutService;
    private readonly IDeviceRegistrationStore _deviceStore;
    private readonly ISessionRepository _sessionRepository;
    private readonly RefreshTokenRotationService _rotationService;
    private readonly IAuditEntryStore _auditStore;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _absoluteTimeout;

    public LoginService(
        IAccountLookupStore lookupStore,
        IPasswordHasher passwordHasher,
        AccountLockoutService lockoutService,
        IDeviceRegistrationStore deviceStore,
        ISessionRepository sessionRepository,
        RefreshTokenRotationService rotationService,
        IAuditEntryStore auditStore,
        TimeSpan? idleTimeout = null,
        TimeSpan? absoluteTimeout = null)
    {
        _lookupStore = lookupStore ?? throw new ArgumentNullException(nameof(lookupStore));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _lockoutService = lockoutService ?? throw new ArgumentNullException(nameof(lockoutService));
        _deviceStore = deviceStore ?? throw new ArgumentNullException(nameof(deviceStore));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));

        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _absoluteTimeout = absoluteTimeout ?? DefaultAbsoluteTimeout;
        if (_idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be positive.");
        }

        if (_absoluteTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteTimeout), "Absolute timeout must be positive.");
        }
    }

    /// <summary>
    /// Runs the login flow. Checks run in a fixed order: account lookup (unknown login is
    /// rejected with a timing-equivalent dummy verification and is never audited because there
    /// is no organization/user context), lockout status, password verification (failures are
    /// counted and may escalate to a temporary lock), device registration (revoked devices are
    /// rejected before any session exists) and finally atomic session creation with the initial
    /// refresh token.
    /// </summary>
    /// <exception cref="ArgumentException">A required command field is empty, or a correlation/request id is empty.</exception>
    public async global::System.Threading.Tasks.Task<LoginOutcome> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Login);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Password);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DeviceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DeviceFingerprintHash);
        EnsureIdentifier(command.CorrelationId, nameof(command.CorrelationId));
        EnsureIdentifier(command.RequestId, nameof(command.RequestId));

        var account = await _lookupStore.FindByLoginAsync(command.Login, cancellationToken);
        if (account is null)
        {
            // Timing-equivalent rejection: spend the same verification cost as for a real
            // account so unknown logins are not distinguishable by response time. The lockout
            // counter is deliberately not touched for non-existent accounts.
            _passwordHasher.VerifyPassword(command.Password, _passwordHasher.DummyPasswordHash);
            return new LoginOutcome.InvalidCredentials();
        }

        var lockoutStatus = await _lockoutService.GetStatusAsync(
            account.OrganizationId, account.UserId, cancellationToken);
        switch (lockoutStatus)
        {
            case LockoutStatus.Blocked:
                await AppendAuditAsync(
                    account, null, LoginFailedActionCode, FailedOutcome, AccountBlockedReasonCode, command, cancellationToken);
                return new LoginOutcome.AccountBlocked();

            case LockoutStatus.LockedTemporarily:
                var remaining = await _lockoutService.GetLockoutRemainingAsync(
                    account.OrganizationId, account.UserId, cancellationToken) ?? TimeSpan.Zero;
                await AppendAuditAsync(
                    account, null, LoginFailedActionCode, FailedOutcome, AccountLockedTemporarilyReasonCode, command, cancellationToken);
                return new LoginOutcome.LockedTemporarily(remaining);

            case LockoutStatus.UserNotFound:
                // The lookup store resolved an account the lockout store does not know about;
                // fail closed with the same outcome as a wrong password.
                return new LoginOutcome.InvalidCredentials();

            case LockoutStatus.NotLocked:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(lockoutStatus), lockoutStatus, "Unknown lockout status.");
        }

        var storedPassword = new PasswordHashRecord(account.PasswordHash, account.PasswordParameters);
        if (!_passwordHasher.VerifyPassword(command.Password, storedPassword))
        {
            var failedOutcome = await _lockoutService.RegisterFailedAsync(
                account.OrganizationId, account.UserId, cancellationToken);
            if (failedOutcome == LoginAttemptOutcome.LockedTemporarily)
            {
                var lockRemaining = await _lockoutService.GetLockoutRemainingAsync(
                    account.OrganizationId, account.UserId, cancellationToken) ?? TimeSpan.Zero;
                await AppendAuditAsync(
                    account, null, LoginFailedActionCode, FailedOutcome, AccountLockedTemporarilyReasonCode, command, cancellationToken);
                return new LoginOutcome.LockedTemporarily(lockRemaining);
            }

            await AppendAuditAsync(
                account, null, LoginFailedActionCode, FailedOutcome, InvalidCredentialsReasonCode, command, cancellationToken);
            return new LoginOutcome.InvalidCredentials();
        }

        await _lockoutService.RegisterSuccessAsync(account.OrganizationId, account.UserId, cancellationToken);

        var deviceId = await _deviceStore.UpsertAsync(
            account.OrganizationId, account.UserId, command.DeviceFingerprintHash, command.DeviceName, cancellationToken);
        var device = await _deviceStore.GetByIdAsync(account.OrganizationId, deviceId, cancellationToken);
        if (device is not null && device.RevokedAtUtc is not null)
        {
            // The device is revoked: reject before a session is created. A revoked device can
            // never register a fresh session.
            await AppendAuditAsync(
                account, null, LoginFailedActionCode, FailedOutcome, DeviceRevokedReasonCode, command, cancellationToken);
            return new LoginOutcome.DeviceRevoked();
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var session = new SessionSnapshot(
            sessionId,
            account.OrganizationId,
            account.UserId,
            deviceId,
            account.CredentialVersion,
            account.AuthorizationScopeVersion,
            now,
            now,
            now + _idleTimeout,
            now + _absoluteTimeout,
            null,
            null);

        var refreshDescriptor = _rotationService.GenerateToken();
        var refreshToken = new RefreshTokenRecord(
            Guid.NewGuid(),
            sessionId,
            refreshDescriptor.TokenHash,
            now,
            now + RefreshTokenRotationService.DefaultRefreshTokenLifetime,
            null,
            null,
            null);

        _sessionRepository.CreateSession(session, refreshToken);

        await AppendAuditAsync(account, sessionId, UserLoggedInActionCode, SuccessOutcome, null, command, cancellationToken);

        return new LoginOutcome.Succeeded(
            sessionId,
            account.UserId,
            account.OrganizationId,
            account.CredentialVersion,
            account.AuthorizationScopeVersion,
            refreshDescriptor.RawToken,
            now + RefreshTokenRotationService.DefaultRefreshTokenLifetime,
            now + _absoluteTimeout,
            account.MustChangePassword);
    }

    /// <summary>
    /// Appends one journal entry for the attempt. Best-effort by design: a journal write
    /// failure (store outage, validation) is swallowed so the login outcome never depends on
    /// the audit trail; cancellation is still propagated. Entries carry no passwords, tokens
    /// or secrets (metadata stays the default empty object).
    /// </summary>
    private async global::System.Threading.Tasks.Task AppendAuditAsync(
        AccountLoginRecord account,
        Guid? actorSessionId,
        string actionCode,
        string outcome,
        string? reasonCode,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = new AuditEntryRecord(
                Guid.NewGuid(),
                account.OrganizationId,
                DateTimeOffset.UtcNow,
                account.UserId,
                actorSessionId,
                actionCode,
                outcome,
                reasonCode,
                command.CorrelationId,
                command.RequestId,
                AuditEntryRecord.DefaultMetadata,
                null,
                null,
                StandardRedactionLevel);
            await _auditStore.AppendAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Audit is best-effort; the login must not fail because of the journal.
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}