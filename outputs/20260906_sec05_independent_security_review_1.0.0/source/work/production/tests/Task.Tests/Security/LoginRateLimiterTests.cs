using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class LoginRateLimiterTests
{
    private static LoginRateLimiter CreateLimiter(TestTimeProvider? time = null) =>
        new(timeProvider: time ?? new TestTimeProvider());

    [Fact]
    public void DefaultCtor_UsesDefaults()
    {
        var limiter = new LoginRateLimiter();

        Assert.Equal(10, limiter.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), limiter.Window);
        Assert.Equal(TimeSpan.FromHours(1), limiter.MaxRetryAfter);
        Assert.Equal(4096, limiter.MaxTrackedKeys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveMaxAttempts_Throws(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoginRateLimiter(maxAttempts));
    }

    [Fact]
    public void Constructor_WithNonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoginRateLimiter(window: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoginRateLimiter(window: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_WithNonPositiveMaxRetryAfter_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoginRateLimiter(maxRetryAfter: TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveMaxTrackedKeys_Throws(int maxTrackedKeys)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoginRateLimiter(maxTrackedKeys: maxTrackedKeys));
    }

    [Fact]
    public void TryRecord_UntilThreshold_Allowed()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < limiter.MaxAttempts - 1; i++)
        {
            var decision = limiter.TryRecord("key");
            Assert.True(decision.IsAllowed);
            Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
        }
    }

    [Fact]
    public void TryRecord_AtThreshold_BlocksWithBaseDelay()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < limiter.MaxAttempts - 1; i++)
        {
            limiter.TryRecord("key");
        }

        var decision = limiter.TryRecord("key");
        Assert.False(decision.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(15), decision.RetryAfter);
    }

    [Fact]
    public void TryRecord_ConsecutiveBlocks_ProgressiveDelay_CappedAtMax()
    {
        var time = new TestTimeProvider();
        var limiter = CreateLimiter(time);

        // Достигаем порога; MaxAttempts-я попытка — первое нарушение (15s).
        for (var i = 0; i < limiter.MaxAttempts - 1; i++)
        {
            limiter.TryRecord("key");
        }

        var first = limiter.TryRecord("key");
        Assert.False(first.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(15), first.RetryAfter);

        var expected = new[]
        {
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(240),
            TimeSpan.FromSeconds(480),
            TimeSpan.FromSeconds(960),
            TimeSpan.FromSeconds(1920),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
        };

        foreach (var delay in expected)
        {
            var decision = limiter.TryRecord("key");
            Assert.False(decision.IsAllowed);
            Assert.Equal(delay, decision.RetryAfter);
        }
    }

    [Fact]
    public void TryRecord_AfterWindowExpires_AllowedAgain()
    {
        var time = new TestTimeProvider();
        var limiter = CreateLimiter(time);

        for (var i = 0; i < limiter.MaxAttempts; i++)
        {
            limiter.TryRecord("key");
        }

        Assert.False(limiter.TryRecord("key").IsAllowed);

        time.Advance(limiter.Window);

        var decision = limiter.TryRecord("key");
        Assert.True(decision.IsAllowed);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    [Fact]
    public void TryRecord_EventsOlderThanWindow_NotCounted()
    {
        var time = new TestTimeProvider();
        var limiter = CreateLimiter(time);

        for (var i = 0; i < limiter.MaxAttempts - 1; i++)
        {
            limiter.TryRecord("key");
        }

        time.Advance(limiter.Window + TimeSpan.FromSeconds(1));

        // Одно событие в новом окне.
        limiter.TryRecord("key");

        // Добавляем свежих событий до порога.
        for (var i = 2; i < limiter.MaxAttempts; i++)
        {
            var decision = limiter.TryRecord("key");
            Assert.True(decision.IsAllowed);
        }

        var blocked = limiter.TryRecord("key");
        Assert.False(blocked.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(15), blocked.RetryAfter);
    }

    [Fact]
    public void TryAllow_DoesNotRegisterEvents()
    {
        var time = new TestTimeProvider();
        var limiter = CreateLimiter(time);

        for (var i = 0; i < limiter.MaxAttempts; i++)
        {
            limiter.TryRecord("key");
        }

        var check = limiter.TryAllow("key");
        Assert.False(check.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(15), check.RetryAfter);

        time.Advance(limiter.Window + TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAllow("key").IsAllowed);

        // TryAllow не регистрировал событий, поэтому TryRecord разрешена.
        var decision = limiter.TryRecord("key");
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Reset_RemovesBlock()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < limiter.MaxAttempts; i++)
        {
            limiter.TryRecord("key");
        }

        Assert.False(limiter.TryRecord("key").IsAllowed);

        limiter.Reset("key");

        var decision = limiter.TryRecord("key");
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Clear_RemovesAllKeys()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < limiter.MaxAttempts; i++)
        {
            limiter.TryRecord("a");
            limiter.TryRecord("b");
        }

        Assert.False(limiter.TryRecord("a").IsAllowed);
        Assert.False(limiter.TryRecord("b").IsAllowed);

        limiter.Clear();

        Assert.True(limiter.TryRecord("a").IsAllowed);
        Assert.True(limiter.TryRecord("b").IsAllowed);
    }

    [Fact]
    public void TryRecord_WithNullKey_ThrowsArgumentNullException()
    {
        var limiter = CreateLimiter();
        Assert.Throws<ArgumentNullException>(() => limiter.TryRecord(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryRecord_WithEmptyOrWhitespaceKey_ThrowsArgumentException(string key)
    {
        var limiter = CreateLimiter();
        Assert.Throws<ArgumentException>(() => limiter.TryRecord(key));
    }

    [Fact]
    public void TryRecord_KeyIsNormalized()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < limiter.MaxAttempts - 2; i++)
        {
            limiter.TryRecord("IP|Login");
        }

        Assert.True(limiter.TryRecord("  ip|login  ").IsAllowed);
        Assert.False(limiter.TryRecord("IP|LOGIN").IsAllowed);
    }

    [Fact]
    public void TryRecord_WhenKeyCapacityIsFull_BlocksNovelKeysWithoutGrowingState()
    {
        var time = new TestTimeProvider();
        var limiter = new LoginRateLimiter(timeProvider: time, maxTrackedKeys: 2);

        Assert.True(limiter.TryRecord("first").IsAllowed);
        Assert.True(limiter.TryRecord("second").IsAllowed);

        var saturated = limiter.TryRecord("third");
        Assert.False(saturated.IsAllowed);
        Assert.Equal(limiter.Window, saturated.RetryAfter);

        time.Advance(limiter.Window);
        Assert.True(limiter.TryRecord("third").IsAllowed);
    }

    [Fact]
    public void ParallelCalls_AccurateCount()
    {
        var limiter = CreateLimiter();
        var allowed = 0;
        var blocked = 0;

        Parallel.For(0, 1000, _ =>
        {
            var decision = limiter.TryRecord("key");
            if (decision.IsAllowed)
            {
                Interlocked.Increment(ref allowed);
            }
            else
            {
                Interlocked.Increment(ref blocked);
            }
        });

        Assert.Equal(limiter.MaxAttempts - 1, allowed);
        Assert.Equal(1000 - (limiter.MaxAttempts - 1), blocked);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;

        public void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _timestamp, (long)(elapsed.TotalSeconds * Stopwatch.Frequency));

        public override long GetTimestamp() =>
            Interlocked.Read(ref _timestamp);
    }
}
