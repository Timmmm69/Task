using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class AccountLockoutPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    private static LockoutState State(
        int failedLoginCount,
        string accountStatus = "active",
        DateTimeOffset? lockedUntilUtc = null) =>
        new(failedLoginCount, accountStatus, lockedUntilUtc, Now);

    [Fact]
    public void DefaultPolicy_UsesConfiguredDefaults()
    {
        var policy = new AccountLockoutPolicy();

        Assert.Equal(5, policy.FailedLoginThreshold);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.ShortLockDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), policy.MediumLockDuration);
        Assert.Equal(TimeSpan.FromMinutes(60), policy.LongLockDuration);
    }

    [Fact]
    public void Constructor_WithExplicitValues_OverridesDefaults()
    {
        var policy = new AccountLockoutPolicy(
            failedLoginThreshold: 3,
            shortLockDuration: TimeSpan.FromMinutes(1),
            mediumLockDuration: TimeSpan.FromMinutes(2),
            longLockDuration: TimeSpan.FromMinutes(4));

        Assert.Equal(3, policy.FailedLoginThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.ShortLockDuration);
        Assert.Equal(TimeSpan.FromMinutes(2), policy.MediumLockDuration);
        Assert.Equal(TimeSpan.FromMinutes(4), policy.LongLockDuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveThreshold_ThrowsArgumentOutOfRangeException(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountLockoutPolicy(threshold));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveDuration_ThrowsArgumentOutOfRangeException(int minutes)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AccountLockoutPolicy(shortLockDuration: duration));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AccountLockoutPolicy(mediumLockDuration: duration));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AccountLockoutPolicy(longLockDuration: duration));
    }

    [Fact]
    public void Constructor_WithNonProgressiveDurations_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AccountLockoutPolicy(
                mediumLockDuration: TimeSpan.FromMinutes(1),
                longLockDuration: TimeSpan.FromMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AccountLockoutPolicy(longLockDuration: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void IsLocked_WithActiveAccountAndNoLock_ReturnsFalseWithZeroRemaining()
    {
        var policy = new AccountLockoutPolicy();

        var locked = policy.IsLocked(State(failedLoginCount: 0), Now, out var remaining);

        Assert.False(locked);
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void IsLocked_WithFutureLock_ReturnsTrueWithRemainingTime()
    {
        var policy = new AccountLockoutPolicy();
        var lockedUntil = Now.AddMinutes(15);

        var locked = policy.IsLocked(State(failedLoginCount: 5, lockedUntilUtc: lockedUntil), Now, out var remaining);

        Assert.True(locked);
        Assert.Equal(TimeSpan.FromMinutes(15), remaining);
    }

    [Fact]
    public void IsLocked_WithLockExactlyAtNow_ReturnsFalse()
    {
        var policy = new AccountLockoutPolicy();

        var locked = policy.IsLocked(State(failedLoginCount: 5, lockedUntilUtc: Now), Now, out var remaining);

        Assert.False(locked);
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void IsLocked_WithExpiredLock_ReturnsFalse()
    {
        var policy = new AccountLockoutPolicy();
        var lockedUntil = Now.AddMinutes(-1);

        var locked = policy.IsLocked(State(failedLoginCount: 5, lockedUntilUtc: lockedUntil), Now, out var remaining);

        Assert.False(locked);
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void IsLocked_WithBlockedStatusAndNullLock_ReturnsTrueWithMaxRemaining()
    {
        var policy = new AccountLockoutPolicy();

        var locked = policy.IsLocked(
            State(failedLoginCount: 0, accountStatus: "blocked", lockedUntilUtc: null),
            Now,
            out var remaining);

        Assert.True(locked);
        Assert.Equal(TimeSpan.MaxValue, remaining);
    }

    [Fact]
    public void IsLocked_WithBlockedStatusAndFutureLock_BlockedWinsWithMaxRemaining()
    {
        var policy = new AccountLockoutPolicy();
        var lockedUntil = Now.AddMinutes(30);

        var locked = policy.IsLocked(
            State(failedLoginCount: 5, accountStatus: "blocked", lockedUntilUtc: lockedUntil),
            Now,
            out var remaining);

        Assert.True(locked);
        Assert.Equal(TimeSpan.MaxValue, remaining);
    }

    [Fact]
    public void IsLocked_WithBlockedStatusAndExpiredLock_BlockedStillWinsWithMaxRemaining()
    {
        var policy = new AccountLockoutPolicy();
        var lockedUntil = Now.AddMinutes(-30);

        var locked = policy.IsLocked(
            State(failedLoginCount: 5, accountStatus: "blocked", lockedUntilUtc: lockedUntil),
            Now,
            out var remaining);

        Assert.True(locked);
        Assert.Equal(TimeSpan.MaxValue, remaining);
    }

    [Fact]
    public void IsLocked_WithNullState_ThrowsArgumentNullException()
    {
        var policy = new AccountLockoutPolicy();

        Assert.Throws<ArgumentNullException>(() => policy.IsLocked(null!, Now, out _));
    }

    [Fact]
    public void ShouldLock_BelowThreshold_ReturnsFalse()
    {
        var policy = new AccountLockoutPolicy();

        Assert.False(policy.ShouldLock(0));
        Assert.False(policy.ShouldLock(4));
    }

    [Fact]
    public void ShouldLock_AtThresholdBoundary_ReturnsTrue()
    {
        var policy = new AccountLockoutPolicy();

        Assert.True(policy.ShouldLock(5));
        Assert.True(policy.ShouldLock(6));
    }

    [Fact]
    public void ShouldLock_WithCustomThreshold_RespectsBoundary()
    {
        var policy = new AccountLockoutPolicy(failedLoginThreshold: 2);

        Assert.False(policy.ShouldLock(1));
        Assert.True(policy.ShouldLock(2));
        Assert.True(policy.ShouldLock(3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GetLockDuration_ForFirstTier_ReturnsShort(int failedCount)
    {
        var policy = new AccountLockoutPolicy();

        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetLockDuration(failedCount));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void GetLockDuration_ForSecondTier_ReturnsMedium(int failedCount)
    {
        var policy = new AccountLockoutPolicy();

        Assert.Equal(TimeSpan.FromMinutes(15), policy.GetLockDuration(failedCount));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(100)]
    public void GetLockDuration_ForThirdTier_ReturnsLong(int failedCount)
    {
        var policy = new AccountLockoutPolicy();

        Assert.Equal(TimeSpan.FromMinutes(60), policy.GetLockDuration(failedCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GetLockDuration_ForNonPositiveCount_FallsBackToShortDefensively(int failedCount)
    {
        var policy = new AccountLockoutPolicy();

        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetLockDuration(failedCount));
    }

    [Fact]
    public void GetLockDuration_WithCustomDurations_UsesConfiguredTiers()
    {
        var policy = new AccountLockoutPolicy(
            shortLockDuration: TimeSpan.FromMinutes(1),
            mediumLockDuration: TimeSpan.FromMinutes(2),
            longLockDuration: TimeSpan.FromMinutes(4));

        Assert.Equal(TimeSpan.FromMinutes(1), policy.GetLockDuration(2));
        Assert.Equal(TimeSpan.FromMinutes(2), policy.GetLockDuration(4));
        Assert.Equal(TimeSpan.FromMinutes(4), policy.GetLockDuration(10));
    }
}
