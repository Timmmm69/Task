using Task.Application.Security;

namespace Task.Api.Auth;

/// <summary>
/// Bounds anonymous login work before a password hash is evaluated. Account, connection-address
/// and process-wide windows prevent key rotation from bypassing throttling; the semaphore caps
/// simultaneous memory-hard password checks.
/// </summary>
internal sealed class LoginAbuseProtector
{
    internal const int DefaultAddressMaxAttempts = 100;
    internal const int DefaultGlobalMaxAttempts = 500;
    internal const int DefaultMaxTrackedAddresses = 1024;
    internal const int DefaultMaxConcurrentPasswordChecks = 2;

    private static readonly TimeSpan CapacityRetryAfter = TimeSpan.FromSeconds(1);

    private readonly LoginRateLimiter _accounts;
    private readonly LoginRateLimiter _addresses;
    private readonly LoginRateLimiter _global;
    private readonly SemaphoreSlim _passwordCheckSlots;

    public LoginAbuseProtector()
        : this(
            LoginRateLimiter.DefaultMaxAttempts,
            DefaultAddressMaxAttempts,
            DefaultGlobalMaxAttempts,
            DefaultMaxConcurrentPasswordChecks,
            LoginRateLimiter.DefaultMaxTrackedKeys,
            DefaultMaxTrackedAddresses)
    {
    }

    internal LoginAbuseProtector(
        int accountMaxAttempts,
        int addressMaxAttempts,
        int globalMaxAttempts,
        int maxConcurrentPasswordChecks,
        int maxTrackedAccounts = LoginRateLimiter.DefaultMaxTrackedKeys,
        int maxTrackedAddresses = DefaultMaxTrackedAddresses,
        TimeProvider? timeProvider = null)
    {
        if (maxConcurrentPasswordChecks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentPasswordChecks));
        }

        _accounts = new LoginRateLimiter(
            accountMaxAttempts,
            timeProvider: timeProvider,
            maxTrackedKeys: maxTrackedAccounts);
        _addresses = new LoginRateLimiter(
            addressMaxAttempts,
            timeProvider: timeProvider,
            maxTrackedKeys: maxTrackedAddresses);
        _global = new LoginRateLimiter(
            globalMaxAttempts,
            timeProvider: timeProvider,
            maxTrackedKeys: 1);
        _passwordCheckSlots = new SemaphoreSlim(
            maxConcurrentPasswordChecks,
            maxConcurrentPasswordChecks);
    }

    public LoginAttemptAdmission TryAcquire(string remoteAddress, string login)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        // Always charge every scope, even when one is already blocked. Otherwise an attacker
        // could alternate saturated and unsaturated keys to avoid the broader window.
        var account = _accounts.TryRecord($"account|{login}");
        var address = _addresses.TryRecord($"address|{remoteAddress}");
        var global = _global.TryRecord("global");
        var retryAfter = Max(account.RetryAfter, address.RetryAfter, global.RetryAfter);
        if (!account.IsAllowed || !address.IsAllowed || !global.IsAllowed)
        {
            return LoginAttemptAdmission.Blocked(retryAfter);
        }

        if (!_passwordCheckSlots.Wait(0))
        {
            return LoginAttemptAdmission.Blocked(CapacityRetryAfter);
        }

        return LoginAttemptAdmission.Allowed(_passwordCheckSlots);
    }

    public void ResetAccount(string login)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        _accounts.Reset($"account|{login}");
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second, TimeSpan third)
    {
        var result = first > second ? first : second;
        return result > third ? result : third;
    }
}

internal sealed class LoginAttemptAdmission : IDisposable
{
    private SemaphoreSlim? _semaphore;

    private LoginAttemptAdmission(bool isAllowed, TimeSpan retryAfter, SemaphoreSlim? semaphore)
    {
        IsAllowed = isAllowed;
        RetryAfter = retryAfter;
        _semaphore = semaphore;
    }

    public bool IsAllowed { get; }

    public TimeSpan RetryAfter { get; }

    public static LoginAttemptAdmission Allowed(SemaphoreSlim semaphore) =>
        new(true, TimeSpan.Zero, semaphore);

    public static LoginAttemptAdmission Blocked(TimeSpan retryAfter) =>
        new(false, retryAfter, null);

    public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
}
