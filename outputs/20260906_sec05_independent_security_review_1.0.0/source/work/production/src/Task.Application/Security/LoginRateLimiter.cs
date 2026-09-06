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
/// таймер — устаревшие записи чистятся лениво при доступе. Для потокобезопасности
/// используется единый lock на всё состояние limiter.
/// </summary>
public sealed class LoginRateLimiter
{
    public const int DefaultMaxAttempts = 10;
    public const int DefaultMaxTrackedKeys = 4096;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultMaxRetryAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan ProgressiveDelayBase = TimeSpan.FromSeconds(15);

    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _maxRetryAfter;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxTrackedKeys;
    private readonly ConcurrentDictionary<string, KeyState> _states = new();
    private readonly object _lock = new();
    private long _lastGlobalCleanup;

    public LoginRateLimiter(
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? window = null,
        TimeSpan? maxRetryAfter = null,
        TimeProvider? timeProvider = null,
        int maxTrackedKeys = DefaultMaxTrackedKeys)
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

        if (maxTrackedKeys < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTrackedKeys),
                "Maximum tracked keys must be at least 1.");
        }

        _maxAttempts = maxAttempts;
        _maxTrackedKeys = maxTrackedKeys;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastGlobalCleanup = _timeProvider.GetTimestamp();
    }

    public int MaxAttempts => _maxAttempts;

    public TimeSpan Window => _window;

    public TimeSpan MaxRetryAfter => _maxRetryAfter;

    public int MaxTrackedKeys => _maxTrackedKeys;

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
            CleanupIfNeeded();

            if (!_states.TryGetValue(normalized, out var state))
            {
                if (_states.Count >= _maxTrackedKeys)
                {
                    return RateLimitDecision.Blocked(_window);
                }

                state = new KeyState();
                _states[normalized] = state;
            }

            var now = _timeProvider.GetTimestamp();
            state.Timestamps.Add(now);
            state.LastTimestamp = now;

            PruneWindow(state);

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
            CleanupIfNeeded();

            if (!_states.TryGetValue(normalized, out var state))
            {
                return _states.Count >= _maxTrackedKeys
                    ? RateLimitDecision.Blocked(_window)
                    : RateLimitDecision.Allowed();
            }

            PruneWindow(state);

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

    private void CleanupIfNeeded()
    {
        var now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_lastGlobalCleanup) < _window)
        {
            return;
        }

        foreach (var pair in _states.ToArray())
        {
            if (_timeProvider.GetElapsedTime(pair.Value.LastTimestamp) >= _window)
            {
                _states.TryRemove(pair.Key, out _);
            }
        }

        _lastGlobalCleanup = now;
    }

    private void PruneWindow(KeyState state)
    {
        var count = state.Timestamps.Count;
        if (count == 0)
        {
            return;
        }

        // Timestamps хранятся в хронологическом порядке.
        var index = 0;
        while (index < count && _timeProvider.GetElapsedTime(state.Timestamps[index]) >= _window)
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
