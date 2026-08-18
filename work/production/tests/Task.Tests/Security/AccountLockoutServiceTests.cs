using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class AccountLockoutServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid UserId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    private static AccountLockoutService CreateService(
        FakeLockoutStore store,
        AccountLockoutPolicy? policy = null) =>
        new(store, policy ?? new AccountLockoutPolicy());

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_ForUnknownUser_ReturnsFailedAndWritesNothing()
    {
        var store = new FakeLockoutStore { State = null };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.Failed, outcome);
        Assert.Empty(store.FailedLogins);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_ForBlockedAccount_ReturnsBlockedAndWritesNothing()
    {
        var store = new FakeLockoutStore { State = State(5, "blocked", null) };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.Blocked, outcome);
        Assert.Empty(store.FailedLogins);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_BelowThreshold_CountsWithoutLock()
    {
        var store = new FakeLockoutStore { State = State(3, "active", null) };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.Failed, outcome);
        var recorded = Assert.Single(store.FailedLogins);
        Assert.Equal(OrganizationId, recorded.OrganizationId);
        Assert.Equal(UserId, recorded.UserId);
        Assert.Equal(4, recorded.NewFailedCount);
        Assert.Null(recorded.LockedUntilUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_ReachingThreshold_LocksWithMediumDuration()
    {
        var store = new FakeLockoutStore { State = State(4, "active", null) };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.LockedTemporarily, outcome);
        var recorded = Assert.Single(store.FailedLogins);
        Assert.Equal(5, recorded.NewFailedCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), recorded.LockedUntilUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_WhileTemporarilyLocked_EscalatesToLongLock()
    {
        var store = new FakeLockoutStore { State = State(5, "active", Now.AddMinutes(15)) };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.LockedTemporarily, outcome);
        var recorded = Assert.Single(store.FailedLogins);
        Assert.Equal(6, recorded.NewFailedCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(60), recorded.LockedUntilUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_AfterLockExpiry_CountContinuesProgressively()
    {
        var store = new FakeLockoutStore { State = State(5, "active", Now.AddMinutes(-1)) };
        var service = CreateService(store);

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.LockedTemporarily, outcome);
        var recorded = Assert.Single(store.FailedLogins);
        Assert.Equal(6, recorded.NewFailedCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(60), recorded.LockedUntilUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_WithCustomThreshold_UsesConfiguredPolicy()
    {
        var store = new FakeLockoutStore { State = State(1, "active", null) };
        var service = CreateService(
            store,
            new AccountLockoutPolicy(
                failedLoginThreshold: 2,
                shortLockDuration: TimeSpan.FromMinutes(1),
                mediumLockDuration: TimeSpan.FromMinutes(2),
                longLockDuration: TimeSpan.FromMinutes(3)));

        var outcome = await service.RegisterFailedAsync(OrganizationId, UserId);

        Assert.Equal(LoginAttemptOutcome.LockedTemporarily, outcome);
        var recorded = Assert.Single(store.FailedLogins);
        Assert.Equal(2, recorded.NewFailedCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), recorded.LockedUntilUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterSuccessAsync_DelegatesToStore()
    {
        var store = new FakeLockoutStore { State = State(5, "active", Now.AddMinutes(10)) };
        var service = CreateService(store);

        await service.RegisterSuccessAsync(OrganizationId, UserId);

        var recorded = Assert.Single(store.SuccessfulLogins);
        Assert.Equal(OrganizationId, recorded.OrganizationId);
        Assert.Equal(UserId, recorded.UserId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_ForUnknownUser_ReturnsUserNotFound()
    {
        var store = new FakeLockoutStore { State = null };
        var service = CreateService(store);

        var status = await service.GetStatusAsync(OrganizationId, UserId);

        Assert.Equal(LockoutStatus.UserNotFound, status);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_ForUnlockedAccount_ReturnsNotLocked()
    {
        var store = new FakeLockoutStore { State = State(2, "active", null) };
        var service = CreateService(store);

        var status = await service.GetStatusAsync(OrganizationId, UserId);

        Assert.Equal(LockoutStatus.NotLocked, status);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_ForTemporarilyLockedAccount_ReturnsLockedTemporarily()
    {
        var store = new FakeLockoutStore { State = State(5, "active", Now.AddMinutes(15)) };
        var service = CreateService(store);

        var status = await service.GetStatusAsync(OrganizationId, UserId);

        Assert.Equal(LockoutStatus.LockedTemporarily, status);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_ForExpiredLock_ReturnsNotLocked()
    {
        var store = new FakeLockoutStore { State = State(5, "active", Now.AddMinutes(-1)) };
        var service = CreateService(store);

        var status = await service.GetStatusAsync(OrganizationId, UserId);

        Assert.Equal(LockoutStatus.NotLocked, status);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_ForBlockedAccount_ReturnsBlockedEvenWithFutureLock()
    {
        var store = new FakeLockoutStore { State = State(5, "blocked", Now.AddMinutes(30)) };
        var service = CreateService(store);

        var status = await service.GetStatusAsync(OrganizationId, UserId);

        Assert.Equal(LockoutStatus.Blocked, status);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_WithEmptyOrganization_ThrowsArgumentException()
    {
        var store = new FakeLockoutStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterFailedAsync(Guid.Empty, UserId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterFailedAsync_WithEmptyUser_ThrowsArgumentException()
    {
        var store = new FakeLockoutStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterFailedAsync(OrganizationId, Guid.Empty));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RegisterSuccessAsync_WithEmptyIdentifiers_ThrowsArgumentException()
    {
        var store = new FakeLockoutStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterSuccessAsync(Guid.Empty, UserId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterSuccessAsync(OrganizationId, Guid.Empty));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetStatusAsync_WithEmptyIdentifiers_ThrowsArgumentException()
    {
        var store = new FakeLockoutStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetStatusAsync(Guid.Empty, UserId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetStatusAsync(OrganizationId, Guid.Empty));
    }

    private static LockoutState State(
        int failedLoginCount,
        string accountStatus,
        DateTimeOffset? lockedUntilUtc) =>
        new(failedLoginCount, accountStatus, lockedUntilUtc, Now);

    private sealed class FakeLockoutStore : IAccountLockoutStore
    {
        public LockoutState? State { get; set; }

        public List<FailedLoginCall> FailedLogins { get; } = [];

        public List<(Guid OrganizationId, Guid UserId)> SuccessfulLogins { get; } = [];

        public global::System.Threading.Tasks.Task<LockoutState?> GetLockoutStateAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(State);

        public global::System.Threading.Tasks.Task<int> RecordFailedLoginAsync(
            Guid organizationId,
            Guid userId,
            int newFailedCount,
            DateTimeOffset? lockedUntilUtcOrNull,
            CancellationToken cancellationToken = default)
        {
            FailedLogins.Add(new FailedLoginCall(organizationId, userId, newFailedCount, lockedUntilUtcOrNull));
            return global::System.Threading.Tasks.Task.FromResult(newFailedCount);
        }

        public global::System.Threading.Tasks.Task RecordSuccessfulLoginAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            SuccessfulLogins.Add((organizationId, userId));
            return global::System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed record FailedLoginCall(
        Guid OrganizationId,
        Guid UserId,
        int NewFailedCount,
        DateTimeOffset? LockedUntilUtc);
}
