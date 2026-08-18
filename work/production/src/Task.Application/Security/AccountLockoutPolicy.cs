namespace Task.Application.Security;

/// <summary>
/// Immutable lockout-relevant view of a user account. DbNowUtc carries the PostgreSQL server
/// clock reading (clock_timestamp()) taken at the moment the state was loaded, so that policy
/// evaluation never depends on the client clock.
/// </summary>
public sealed record LockoutState(
    int FailedLoginCount,
    string AccountStatus,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset DbNowUtc);

/// <summary>
/// Pure lockout policy: decides whether an account is locked, when a failed login count reaches
/// the locking threshold and how long a temporary lock lasts. Durations escalate with the failed
/// count (progressive delay): 1-3 failures -> short, 4-5 -> medium, 6+ -> long. The numbers are
/// configuration defaults, supplied through the constructor.
/// </summary>
public sealed class AccountLockoutPolicy
{
    public const string BlockedAccountStatus = "blocked";

    public const int DefaultFailedLoginThreshold = 5;
    public static readonly TimeSpan DefaultShortLockDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultMediumLockDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultLongLockDuration = TimeSpan.FromMinutes(60);

    private readonly int _failedLoginThreshold;
    private readonly TimeSpan _shortLockDuration;
    private readonly TimeSpan _mediumLockDuration;
    private readonly TimeSpan _longLockDuration;

    public AccountLockoutPolicy(
        int failedLoginThreshold = DefaultFailedLoginThreshold,
        TimeSpan? shortLockDuration = null,
        TimeSpan? mediumLockDuration = null,
        TimeSpan? longLockDuration = null)
    {
        if (failedLoginThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedLoginThreshold),
                "Failed login threshold must be at least 1.");
        }

        _failedLoginThreshold = failedLoginThreshold;
        _shortLockDuration = shortLockDuration ?? DefaultShortLockDuration;
        _mediumLockDuration = mediumLockDuration ?? DefaultMediumLockDuration;
        _longLockDuration = longLockDuration ?? DefaultLongLockDuration;

        if (_shortLockDuration <= TimeSpan.Zero || _mediumLockDuration <= TimeSpan.Zero || _longLockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shortLockDuration),
                "Lock durations must be positive.");
        }

        if (_mediumLockDuration < _shortLockDuration || _longLockDuration < _mediumLockDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mediumLockDuration),
                "Lock durations must be non-decreasing (progressive delay).");
        }
    }

    public int FailedLoginThreshold => _failedLoginThreshold;

    public TimeSpan ShortLockDuration => _shortLockDuration;

    public TimeSpan MediumLockDuration => _mediumLockDuration;

    public TimeSpan LongLockDuration => _longLockDuration;

    /// <summary>
    /// True when the account is permanently blocked (account_status == 'blocked', remaining is
    /// TimeSpan.MaxValue) or when locked_until is strictly in the future relative to nowUtc
    /// (remaining is the time until the lock expires). False otherwise, with remaining = Zero.
    /// </summary>
    public bool IsLocked(LockoutState state, DateTimeOffset nowUtc, out TimeSpan remaining)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.Equals(state.AccountStatus, BlockedAccountStatus, StringComparison.Ordinal))
        {
            remaining = TimeSpan.MaxValue;
            return true;
        }

        if (state.LockedUntilUtc is { } lockedUntil && lockedUntil > nowUtc)
        {
            remaining = lockedUntil - nowUtc;
            return true;
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// True when a failed login count reaches the configured threshold (boundary included).
    /// </summary>
    public bool ShouldLock(int failedCount) => failedCount >= _failedLoginThreshold;

    /// <summary>
    /// Progressive lock duration for the given failed login count:
    /// 1-3 -> short, 4-5 -> medium, 6+ -> long. Counts at or below zero fall into the short
    /// tier defensively and are never used with ShouldLock.
    /// </summary>
    public TimeSpan GetLockDuration(int failedCount) => failedCount switch
    {
        <= 3 => _shortLockDuration,
        <= 5 => _mediumLockDuration,
        _ => _longLockDuration,
    };
}
