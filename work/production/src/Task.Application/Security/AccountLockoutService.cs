namespace Task.Application.Security;

/// <summary>
/// Outcome of registering a failed login attempt against the lockout service.
/// </summary>
public enum LoginAttemptOutcome
{
    /// <summary>The attempt was counted; the account is not locked (or does not exist).</summary>
    Failed,

    /// <summary>The attempt was counted and the account is now (or remains) temporarily locked.</summary>
    LockedTemporarily,

    /// <summary>The account is permanently blocked; nothing was recorded.</summary>
    Blocked,
}

/// <summary>
/// Current lockout status of an account, as observed by the service.
/// </summary>
public enum LockoutStatus
{
    /// <summary>The account exists and is neither temporarily nor permanently locked.</summary>
    NotLocked,

    /// <summary>The account is temporarily locked (locked_until is in the future).</summary>
    LockedTemporarily,

    /// <summary>The account is permanently blocked (account_status == 'blocked').</summary>
    Blocked,

    /// <summary>No account with the given identity exists in the organization.</summary>
    UserNotFound,
}

/// <summary>
/// Orchestrates failed-login counting and lockout enforcement. Accepts no passwords: the login
/// flow decides when to call RegisterFailedAsync/RegisterSuccessAsync; this service only counts
/// attempts and computes lock state through the AccountLockoutPolicy. All time comes from the
/// database clock carried in LockoutState.DbNowUtc.
/// </summary>
public sealed class AccountLockoutService
{
    private readonly IAccountLockoutStore _store;
    private readonly AccountLockoutPolicy _policy;

    public AccountLockoutService(IAccountLockoutStore store, AccountLockoutPolicy policy)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Counts one failed login attempt, applies the policy and persists the incremented count
    /// with the lock deadline when the threshold is reached. Returns Failed when the account
    /// does not exist or is below the threshold, LockedTemporarily when the account is locked
    /// (now or after this attempt), Blocked when the account is permanently blocked.
    /// </summary>
    public async global::System.Threading.Tasks.Task<LoginAttemptOutcome> RegisterFailedAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        var state = await _store.GetLockoutStateAsync(organizationId, userId, cancellationToken);
        if (state is null)
        {
            return LoginAttemptOutcome.Failed;
        }

        if (string.Equals(state.AccountStatus, AccountLockoutPolicy.BlockedAccountStatus, StringComparison.Ordinal))
        {
            return LoginAttemptOutcome.Blocked;
        }

        var nowUtc = state.DbNowUtc;
        var wasLocked = _policy.IsLocked(state, nowUtc, out _);

        var newFailedCount = state.FailedLoginCount + 1;
        DateTimeOffset? lockedUntilUtc = null;
        if (_policy.ShouldLock(newFailedCount))
        {
            lockedUntilUtc = nowUtc + _policy.GetLockDuration(newFailedCount);
        }

        await _store.RecordFailedLoginAsync(
            organizationId, userId, newFailedCount, lockedUntilUtc, cancellationToken);

        return wasLocked || lockedUntilUtc is not null
            ? LoginAttemptOutcome.LockedTemporarily
            : LoginAttemptOutcome.Failed;
    }

    /// <summary>
    /// Records a successful login: the store resets the failed login counter and any temporary
    /// lock. Permanently blocked accounts are never reset.
    /// </summary>
    public async global::System.Threading.Tasks.Task RegisterSuccessAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await _store.RecordSuccessfulLoginAsync(organizationId, userId, cancellationToken);
    }

    /// <summary>
    /// Reports the current lockout status of an account, evaluated against the database clock.
    /// </summary>
    public async global::System.Threading.Tasks.Task<LockoutStatus> GetStatusAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        var state = await _store.GetLockoutStateAsync(organizationId, userId, cancellationToken);
        if (state is null)
        {
            return LockoutStatus.UserNotFound;
        }

        if (string.Equals(state.AccountStatus, AccountLockoutPolicy.BlockedAccountStatus, StringComparison.Ordinal))
        {
            return LockoutStatus.Blocked;
        }

        return _policy.IsLocked(state, state.DbNowUtc, out _)
            ? LockoutStatus.LockedTemporarily
            : LockoutStatus.NotLocked;
    }

    /// <summary>
    /// Remaining time of the current temporary lock (locked_until - db now), or null when the
    /// account is not temporarily locked. Evaluated against the database clock
    /// (LockoutState.DbNowUtc); permanently blocked accounts without a locked_until also
    /// report null (their rejection is signalled by <see cref="GetStatusAsync"/>).
    /// </summary>
    public async global::System.Threading.Tasks.Task<TimeSpan?> GetLockoutRemainingAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        var state = await _store.GetLockoutStateAsync(organizationId, userId, cancellationToken);
        if (state is null || state.LockedUntilUtc is not { } lockedUntil || lockedUntil <= state.DbNowUtc)
        {
            return null;
        }

        return lockedUntil - state.DbNowUtc;
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
