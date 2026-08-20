using System.Collections.Concurrent;

namespace Task.Application.Security;

/// <summary>
/// Результат проверки rate limit: либо разрешено, либо блокировано с рекомендуемым
/// временем ожидания перед следующей попыткой.
/// </summary>
public sealed record RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    /// <summary>
    /// Разрешённая попытка.
    /// </summary>
    public static RateLimitDecision Allowed() => new(true, TimeSpan.Zero);

    /// <summary>
    /// Блокированная попытка. RetryAfter указывает рекомендуемое время ожидания.
    /// </summary>
    public static RateLimitDecision Blocked(TimeSpan retryAfter) => new(false, retryAfter);
}

/// <summary>
/// Процессный in-memory rate limiter для попыток входа. Ключ формирует вызывающий
/// (например, "ip|login" в нижнем регистре). Использует скользящее окно на основе
/// монотонного времени <see cref="TimeProvider"/>; при превышении порога прогрессивно
/// увеличивает задержку. Сервис не имеет внешних зависимостей и не использует фоновый
/// таймер — устаревшие записи чистятся лениво при доступе.
/// </summary>
public sealed class LoginRateLimiter
{
    public const int DefaultMaxAttempts = 10;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultMaxRetryAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan ProgressiveDelayBase = TimeSpan.FromSeconds(15);

    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _maxRetryAfter;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, KeyState> _states = new();
    private readonly object _lock = new();
    private long _lastGlobalCleanup;

    public LoginRateLimiter(
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? window = null,
        TimeSpan? maxRetryAfter = null,
        TimeProvider? timeProvider = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "Maximum attempts must be at least 1.");
        }

        _window = window ?? DefaultWindow;
        _maxRetryAfter = maxRetryAfter ?? DefaultMaxRetryAfter;

        if (_window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(window),
                "Window must be positive.");
        }

        if (_maxRetryAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetryAfter),
                "Maximum retry-after must be positive.");
        }

        _maxAttempts = maxAttempts;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastGlobalCleanup = _timeProvider.GetTimestamp();
    }

    public int MaxAttempts => _maxAttempts;

    public TimeSpan Window => _window;

    public TimeSpan MaxRetryAfter => _maxRetryAfter;

    /// <summary>
    /// Регистрирует событие для ключа и возвращает Allowed или Blocked с
    /// рекомендуемым временем ожидания. Событие регистрируется всегда, в том числе
    /// при блокировке; блок сохраняется, пока в скользящем окне не менее
    /// <see cref="MaxAttempts"/> событий.
    /// </summary>
    public RateLimitDecision TryRecord(string key)
    {
        var normalized = NormalizeKey(key);

        lock (_lock)
        {
            var now = _timeProvider.GetTimestamp();
            CleanupIfNeeded(now);

            if (!_states.TryGetValue(normalized, out var state))
            {
                state = new KeyState();
                _states[normalized] = state;
            }

            state.Timestamps.Add(now);
            state.LastTimestamp = now;

            PruneWindow(state, now);

            if (state.Timestamps.Count >= _maxAttempts)
            {
                state.Violations++;
                return RateLimitDecision.Blocked(ComputeRetryAfter(state.Violations));
            }

            state.Violations = 0;
            return RateLimitDecision.Allowed();
        }
    }

    /// <summary>
    /// Проверяет ключ без регистрации события. Возвращает Blocked с текущим
    /// retryAfter, если в окне уже достигнут порог.
    /// </summary>
    public RateLimitDecision TryAllow(string key)
    {
        var normalized = NormalizeKey(key);

        lock (_lock)
        {
            var now = _timeProvider.GetTimestamp();
            CleanupIfNeeded(now);

            if (!_states.TryGetValue(normalized, out var state))
            {
                return RateLimitDecision.Allowed();
            }

            PruneWindow(state, now);

            if (state.Timestamps.Count >= _maxAttempts)
            {
                return RateLimitDecision.Blocked(ComputeRetryAfter(state.Violations));
            }

            return RateLimitDecision.Allowed();
        }
    }

    /// <summary>
    /// Сбрасывает состояние для ключа (вызывается после успешного входа).
    /// </summary>
    public void Reset(string key)
    {
        var normalized = NormalizeKey(key);
        lock (_lock)
        {
            _states.TryRemove(normalized, out _);
        }
    }

    /// <summary>
    /// Полный сброс всех ключей.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _states.Clear();
            _lastGlobalCleanup = _timeProvider.GetTimestamp();
        }
    }

    private static string NormalizeKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var normalized = key.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Key cannot be empty or whitespace.", nameof(key));
        }

        return normalized;
    }

    private void CleanupIfNeeded(long now)
    {
        if (_timeProvider.GetElapsedTime(_lastGlobalCleanup, now) <= _window)
        {
            return;
        }

        var staleThreshold = _window + _window;
        foreach (var pair in _states.ToArray())
        {
            if (_timeProvider.GetElapsedTime(pair.Value.LastTimestamp, now) > staleThreshold)
            {
                _states.TryRemove(pair.Key, out _);
            }
        }

        _lastGlobalCleanup = now;
    }

    private void PruneWindow(KeyState state, long now)
    {
        var count = state.Timestamps.Count;
        if (count == 0)
        {
            return;
        }

        // Timestamps хранятся в хронологическом порядке.
        var index = 0;
        while (index < count && _timeProvider.GetElapsedTime(state.Timestamps[index], now) >= _window)
        {
            index++;
        }

        if (index > 0)
        {
            state.Timestamps.RemoveRange(0, index);
        }
    }

    private TimeSpan ComputeRetryAfter(int violations)
    {
        if (violations <= 0)
        {
            return TimeSpan.Zero;
        }

        var delay = ProgressiveDelayBase;
        for (var i = 1; i < violations; i++)
        {
            if (delay >= _maxRetryAfter)
            {
                break;
            }

            delay += delay;
        }

        return delay > _maxRetryAfter ? _maxRetryAfter : delay;
    }

    private sealed class KeyState
    {
        public List<long> Timestamps { get; } = new();

        public int Violations { get; set; }

        public long LastTimestamp { get; set; }
    }
}
