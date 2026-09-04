using System.Security.Cryptography;
using System.Text;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class LoginServiceTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_ExpiredTemporaryPassword_DoesNotCreateSession()
    {
        var account=ActiveAccount();
        var lookup=new FakeLookupStore { Account=account with { MustChangePassword=true, TemporaryPasswordExpiresAtUtc=account.DbNowUtc.AddSeconds(-1) } };
        var sessions=new FakeSessionRepository();
        var service=CreateService(lookup,new FakeHasher { VerifyResult=true },new FakeLockoutStore { State=LockoutState(0,"active",null) },new FakeDeviceStore { Device=ActiveDevice() },sessions,new FakeAuditStore());
        Assert.IsType<LoginOutcome.InvalidCredentials>(await service.LoginAsync(Command()));
    }
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid UserId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid DeviceId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid CorrelationId = Guid.Parse("5f2a44e1-8c3b-4f70-a1d2-9c1b4e6f8a10");
    private static readonly Guid RequestId = Guid.Parse("b7d0c9a3-1e45-4f62-9c8a-3d5e7f9a2b41");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private const string Login = "ivanov";
    private const string CorrectPassword = "Correct-password-1";
    private const string FingerprintHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static LoginService CreateService(
        FakeLookupStore lookupStore,
        FakeHasher hasher,
        FakeLockoutStore lockoutStore,
        FakeDeviceStore deviceStore,
        FakeSessionRepository sessionRepository,
        FakeAuditStore auditStore,
        TimeSpan? idleTimeout = null,
        TimeSpan? absoluteTimeout = null) =>
        new(
            lookupStore,
            hasher,
            new AccountLockoutService(lockoutStore, new AccountLockoutPolicy()),
            deviceStore,
            sessionRepository,
            new RefreshTokenRotationService(sessionRepository),
            auditStore,
            idleTimeout,
            absoluteTimeout);

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_Success_CreatesSessionWithVersionsAndExpiries()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = true };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var device = new FakeDeviceStore { Device = ActiveDevice() };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore();
        var service = CreateService(
            lookup, hasher, lockout, device, sessions, audit,
            idleTimeout: TimeSpan.FromHours(2),
            absoluteTimeout: TimeSpan.FromDays(7));

        var outcome = await service.LoginAsync(Command());

        var succeeded = Assert.IsType<LoginOutcome.Succeeded>(outcome);
        Assert.Equal(OrganizationId, succeeded.OrganizationId);
        Assert.Equal(UserId, succeeded.UserId);
        Assert.Equal(3, succeeded.CredentialVersion);
        Assert.Equal(5, succeeded.AuthorizationScopeVersion);
        Assert.False(succeeded.MustChangePassword);

        var call = Assert.Single(sessions.CreateCalls);
        var session = call.Session;
        Assert.Equal(succeeded.SessionId, session.SessionId);
        Assert.Equal(OrganizationId, session.OrganizationId);
        Assert.Equal(UserId, session.UserAccountId);
        Assert.Equal(DeviceId, session.DeviceId);
        Assert.Equal(3, session.CredentialVersion);
        Assert.Equal(5, session.AuthorizationScopeVersion);
        Assert.Equal(session.CreatedAtUtc, session.LastSeenAtUtc);
        Assert.Equal(TimeSpan.FromHours(2), session.IdleExpiresAtUtc - session.CreatedAtUtc);
        Assert.Equal(TimeSpan.FromDays(7), session.AbsoluteExpiresAtUtc - session.CreatedAtUtc);
        Assert.Null(session.RevokedAtUtc);
        Assert.Null(session.RevokeReason);

        Assert.Equal(session.SessionId, call.RefreshToken.SessionId);
        Assert.Equal(session.CreatedAtUtc, call.RefreshToken.IssuedAtUtc);
        Assert.Equal(
            RefreshTokenRotationService.DefaultRefreshTokenLifetime,
            call.RefreshToken.ExpiresAtUtc - call.RefreshToken.IssuedAtUtc);
        Assert.Equal(ComputeHash(succeeded.RawRefreshToken), call.RefreshToken.TokenHash);
        Assert.Equal(call.RefreshToken.ExpiresAtUtc, succeeded.RefreshExpiresAtUtc);
        Assert.Equal(session.AbsoluteExpiresAtUtc, succeeded.AbsoluteExpiresAtUtc);
        Assert.Null(call.RefreshToken.ConsumedAtUtc);
        Assert.Null(call.RefreshToken.ReplacedById);
        Assert.Null(call.RefreshToken.RevokedAtUtc);

        var upsert = Assert.Single(device.Upserts);
        Assert.Equal(OrganizationId, upsert.OrganizationId);
        Assert.Equal(UserId, upsert.UserId);
        Assert.Equal(FingerprintHash, upsert.FingerprintHash);
        Assert.Equal("Work PC", upsert.DisplayName);

        Assert.Single(lockout.SuccessfulLogins);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OrganizationId, entry.OrganizationId);
        Assert.Equal(UserId, entry.ActorUserId);
        Assert.Equal(succeeded.SessionId, entry.ActorSessionId);
        Assert.Equal("UserLoggedIn", entry.ActionCode);
        Assert.Equal("success", entry.Outcome);
        Assert.Null(entry.ReasonCode);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Equal(RequestId, entry.RequestId);
        Assert.Equal(AuditEntryRecord.DefaultMetadata, entry.Metadata);
        Assert.Null(entry.OldState);
        Assert.Null(entry.NewState);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_WithDefaultTimeouts_UsesCanonicalEightHoursAndThirtyDays()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = true };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var device = new FakeDeviceStore { Device = ActiveDevice() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(lookup, hasher, lockout, device, sessions, new FakeAuditStore());

        Assert.Equal(TimeSpan.FromHours(8), LoginService.DefaultIdleTimeout);
        Assert.Equal(TimeSpan.FromDays(30), LoginService.DefaultAbsoluteTimeout);

        var outcome = await service.LoginAsync(Command());

        var succeeded = Assert.IsType<LoginOutcome.Succeeded>(outcome);
        var session = Assert.Single(sessions.CreateCalls).Session;
        Assert.Equal(LoginService.DefaultIdleTimeout, session.IdleExpiresAtUtc - session.CreatedAtUtc);
        Assert.Equal(LoginService.DefaultAbsoluteTimeout, session.AbsoluteExpiresAtUtc - session.CreatedAtUtc);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_Success_PropagatesMustChangePasswordFromAccount()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount(mustChangePassword: true) };
        var hasher = new FakeHasher { VerifyResult = true };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var device = new FakeDeviceStore { Device = ActiveDevice() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(lookup, hasher, lockout, device, sessions, new FakeAuditStore());

        var outcome = await service.LoginAsync(Command());

        var succeeded = Assert.IsType<LoginOutcome.Succeeded>(outcome);
        Assert.True(succeeded.MustChangePassword);
        Assert.Single(sessions.CreateCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_UnknownLogin_ReturnsInvalidCredentialsWithDummyVerifyAndNoLockout()
    {
        var lookup = new FakeLookupStore { Account = null };
        var hasher = new FakeHasher();
        var lockout = new FakeLockoutStore();
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, hasher, lockout, new FakeDeviceStore(), new FakeSessionRepository(), audit);

        var outcome = await service.LoginAsync(Command("anything"));

        Assert.IsType<LoginOutcome.InvalidCredentials>(outcome);
        Assert.Equal(1, hasher.DummyVerifyCalls);
        Assert.Equal(0, lockout.GetStateCalls);
        Assert.Empty(lockout.FailedLogins);
        Assert.Empty(lockout.SuccessfulLogins);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_WrongPassword_ReturnsInvalidCredentialsAndRegistersFailure()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = false };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, hasher, lockout, new FakeDeviceStore(), sessions, audit);

        var outcome = await service.LoginAsync(Command("wrong-password"));

        Assert.IsType<LoginOutcome.InvalidCredentials>(outcome);
        var failed = Assert.Single(lockout.FailedLogins);
        Assert.Equal(OrganizationId, failed.OrganizationId);
        Assert.Equal(UserId, failed.UserId);
        Assert.Equal(1, failed.NewFailedCount);
        Assert.Null(failed.LockedUntilUtc);
        Assert.Empty(sessions.CreateCalls);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("LoginFailed", entry.ActionCode);
        Assert.Equal("failed", entry.Outcome);
        Assert.Equal("INVALID_CREDENTIALS", entry.ReasonCode);
        Assert.Equal(UserId, entry.ActorUserId);
        Assert.Null(entry.ActorSessionId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_WrongPasswordCrossingThreshold_ReturnsLockedWithRecalculatedRemaining()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = false };
        var lockout = new FakeLockoutStore { State = LockoutState(4, "active", null) };
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, hasher, lockout, new FakeDeviceStore(), new FakeSessionRepository(), audit);

        var outcome = await service.LoginAsync(Command("wrong-password"));

        var locked = Assert.IsType<LoginOutcome.LockedTemporarily>(outcome);
        Assert.Equal(TimeSpan.FromMinutes(15), locked.Remaining);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), Assert.Single(lockout.FailedLogins).LockedUntilUtc);
        Assert.Equal("ACCOUNT_LOCKED_TEMPORARILY", Assert.Single(audit.Entries).ReasonCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_TemporarilyLocked_ReturnsLockedWithPositiveRemaining()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var lockout = new FakeLockoutStore { State = LockoutState(5, "active", Now.AddMinutes(15)) };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, new FakeHasher(), lockout, new FakeDeviceStore(), sessions, audit);

        var outcome = await service.LoginAsync(Command());

        var locked = Assert.IsType<LoginOutcome.LockedTemporarily>(outcome);
        Assert.True(locked.Remaining > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(15), locked.Remaining);
        Assert.Empty(lockout.FailedLogins);
        Assert.Empty(sessions.CreateCalls);
        Assert.Equal("ACCOUNT_LOCKED_TEMPORARILY", Assert.Single(audit.Entries).ReasonCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_BlockedAccount_ReturnsAccountBlocked()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var lockout = new FakeLockoutStore { State = LockoutState(5, "blocked", null) };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, new FakeHasher(), lockout, new FakeDeviceStore(), sessions, audit);

        var outcome = await service.LoginAsync(Command());

        Assert.IsType<LoginOutcome.AccountBlocked>(outcome);
        Assert.Empty(lockout.FailedLogins);
        Assert.Empty(lockout.SuccessfulLogins);
        Assert.Empty(sessions.CreateCalls);
        Assert.Equal("ACCOUNT_BLOCKED", Assert.Single(audit.Entries).ReasonCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_LockoutStoreDiscrepancy_ReturnsInvalidCredentials()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var lockout = new FakeLockoutStore { State = null };
        var service = CreateService(lookup, new FakeHasher(), lockout, new FakeDeviceStore(), new FakeSessionRepository(), new FakeAuditStore());

        var outcome = await service.LoginAsync(Command());

        Assert.IsType<LoginOutcome.InvalidCredentials>(outcome);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_RevokedDevice_ReturnsDeviceRevokedWithoutSession()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = true };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var device = new FakeDeviceStore
        {
            Device = new DeviceRegistrationRecord(DeviceId, UserId, FingerprintHash, Now.AddDays(-1)),
        };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore();
        var service = CreateService(lookup, hasher, lockout, device, sessions, audit);

        var outcome = await service.LoginAsync(Command());

        Assert.IsType<LoginOutcome.DeviceRevoked>(outcome);
        Assert.Single(lockout.SuccessfulLogins);
        Assert.Empty(sessions.CreateCalls);
        Assert.Equal("DEVICE_REVOKED", Assert.Single(audit.Entries).ReasonCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAsync_AuditStoreFailure_StillSucceeds()
    {
        var lookup = new FakeLookupStore { Account = ActiveAccount() };
        var hasher = new FakeHasher { VerifyResult = true };
        var lockout = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var device = new FakeDeviceStore { Device = ActiveDevice() };
        var sessions = new FakeSessionRepository();
        var audit = new FakeAuditStore { ThrowOnAppend = new InvalidOperationException("journal unavailable") };
        var service = CreateService(lookup, hasher, lockout, device, sessions, audit);

        var outcome = await service.LoginAsync(Command());

        Assert.IsType<LoginOutcome.Succeeded>(outcome);
        Assert.Single(sessions.CreateCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetLockoutRemainingAsync_ForNotLockedAccount_ReturnsNull()
    {
        var store = new FakeLockoutStore { State = LockoutState(0, "active", null) };
        var service = new AccountLockoutService(store, new AccountLockoutPolicy());

        Assert.Null(await service.GetLockoutRemainingAsync(OrganizationId, UserId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetLockoutRemainingAsync_ForUnknownAccount_ReturnsNull()
    {
        var store = new FakeLockoutStore { State = null };
        var service = new AccountLockoutService(store, new AccountLockoutPolicy());

        Assert.Null(await service.GetLockoutRemainingAsync(OrganizationId, UserId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetLockoutRemainingAsync_ForExpiredLock_ReturnsNull()
    {
        var store = new FakeLockoutStore { State = LockoutState(5, "active", Now.AddMinutes(-1)) };
        var service = new AccountLockoutService(store, new AccountLockoutPolicy());

        Assert.Null(await service.GetLockoutRemainingAsync(OrganizationId, UserId));
    }

    private static AccountLoginRecord ActiveAccount(bool mustChangePassword = false) => new(
        OrganizationId,
        UserId,
        Login,
        $"hash:{CorrectPassword}",
        "{}",
        3,
        5,
        "active",
        0,
        null,
        Now,
        mustChangePassword);

    private static DeviceRegistrationRecord ActiveDevice() =>
        new(DeviceId, UserId, FingerprintHash, null);

    private static LockoutState LockoutState(int failedLoginCount, string accountStatus, DateTimeOffset? lockedUntilUtc) =>
        new(failedLoginCount, accountStatus, lockedUntilUtc, Now);

    private static LoginCommand Command(string password = CorrectPassword) =>
        new(Login, password, "raw-device-key", "Work PC", FingerprintHash, CorrelationId, RequestId);

    private static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed class FakeLookupStore : IAccountLookupStore
    {
        public AccountLoginRecord? Account { get; set; }

        public int LookupCalls { get; private set; }

        public global::System.Threading.Tasks.Task<AccountLoginRecord?> FindByLoginAsync(
            string login,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            return global::System.Threading.Tasks.Task.FromResult(Account);
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public PasswordHashRecord DummyPasswordHash { get; } = new("dummy-hash", "{}");

        public bool VerifyResult { get; set; } = true;

        public int DummyVerifyCalls { get; private set; }

        public List<(string Password, PasswordHashRecord Stored)> VerifyCalls { get; } = [];

        public PasswordHashRecord HashPassword(string password) =>
            throw new global::System.NotImplementedException();

        public bool VerifyPassword(string password, PasswordHashRecord stored)
        {
            VerifyCalls.Add((password, stored));
            if (stored == DummyPasswordHash)
            {
                DummyVerifyCalls++;
            }

            return VerifyResult;
        }
    }

    private sealed class FakeLockoutStore : IAccountLockoutStore
    {
        public LockoutState? State { get; set; }

        public int GetStateCalls { get; private set; }

        public List<FailedLoginCall> FailedLogins { get; } = [];

        public List<(Guid OrganizationId, Guid UserId)> SuccessfulLogins { get; } = [];

        public global::System.Threading.Tasks.Task<LockoutState?> GetLockoutStateAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            GetStateCalls++;
            return global::System.Threading.Tasks.Task.FromResult(State);
        }

        public global::System.Threading.Tasks.Task<int> RecordFailedLoginAsync(
            Guid organizationId,
            Guid userId,
            int newFailedCount,
            DateTimeOffset? lockedUntilUtcOrNull,
            CancellationToken cancellationToken = default)
        {
            FailedLogins.Add(new FailedLoginCall(organizationId, userId, newFailedCount, lockedUntilUtcOrNull));
            if (State is not null)
            {
                State = new LockoutState(newFailedCount, State.AccountStatus, lockedUntilUtcOrNull, State.DbNowUtc);
            }

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

    private sealed class FakeDeviceStore : IDeviceRegistrationStore
    {
        public Guid DeviceId { get; set; } = LoginServiceTests.DeviceId;

        public DeviceRegistrationRecord? Device { get; set; }

        public List<(Guid OrganizationId, Guid UserId, string FingerprintHash, string? DisplayName)> Upserts { get; } = [];

        public global::System.Threading.Tasks.Task<Guid> UpsertAsync(
            Guid organizationId,
            Guid userId,
            string fingerprintHash,
            string? displayName,
            CancellationToken cancellationToken = default)
        {
            Upserts.Add((organizationId, userId, fingerprintHash, displayName));
            return global::System.Threading.Tasks.Task.FromResult(DeviceId);
        }

        public global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(
            Guid organizationId,
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(Device);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public List<(SessionSnapshot Session, RefreshTokenRecord RefreshToken)> CreateCalls { get; } = [];

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            throw new global::System.NotImplementedException();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) =>
            throw new global::System.NotImplementedException();

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            throw new global::System.NotImplementedException();

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken)
        {
            CreateCalls.Add((session, refreshToken));
        }

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) =>
            throw new global::System.NotImplementedException();

        public void TouchSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) =>
            throw new global::System.NotImplementedException();

        public int RevokeAllUserSessions(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            string? reason) =>
            throw new global::System.NotImplementedException();

        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken cancellationToken = default) =>
            throw new global::System.NotImplementedException();

        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            throw new global::System.NotImplementedException();

        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            throw new global::System.NotImplementedException();
    }

    private sealed class FakeAuditStore : IAuditEntryStore
    {
        public Exception? ThrowOnAppend { get; set; }

        public List<AuditEntryRecord> Entries { get; } = [];

        public global::System.Threading.Tasks.Task AppendAsync(
            AuditEntryRecord entry,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnAppend is not null)
            {
                throw ThrowOnAppend;
            }

            Entries.Add(entry);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task<AuditPage> ReadAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default) =>
            throw new global::System.NotImplementedException();
    }

    private sealed record FailedLoginCall(
        Guid OrganizationId,
        Guid UserId,
        int NewFailedCount,
        DateTimeOffset? LockedUntilUtc);
}
