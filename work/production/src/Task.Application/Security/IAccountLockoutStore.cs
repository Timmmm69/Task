namespace Task.Application.Security;

/// <summary>
/// Persistence port for account lockout state in iam.user_accounts (failed_login_count,
/// locked_until, account_status). All time values derive from the PostgreSQL server clock
/// (clock_timestamp()); the application never supplies client-clock timestamps.
/// </summary>
public interface IAccountLockoutStore
{
    /// <summary>
    /// Loads the lockout state for a user account together with the PostgreSQL server clock
    /// reading. Returns null when the account does not exist in the organization
    /// (a distinct outcome for the caller).
    /// </summary>
    global::System.Threading.Tasks.Task<LockoutState?> GetLockoutStateAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records a failed login: sets failed_login_count to newFailedCount and, when
    /// lockedUntilUtcOrNull is not null, locked_until to that value. No-op for permanently
    /// blocked accounts and unknown accounts. Returns the persisted failed_login_count
    /// (0 when no row was updated).
    /// </summary>
    global::System.Threading.Tasks.Task<int> RecordFailedLoginAsync(
        Guid organizationId,
        Guid userId,
        int newFailedCount,
        DateTimeOffset? lockedUntilUtcOrNull,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the failed login counter and any temporary lock after a successful login.
    /// Never touches accounts with account_status == 'blocked': a permanent block is
    /// not cleared by a successful login.
    /// </summary>
    global::System.Threading.Tasks.Task RecordSuccessfulLoginAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
