using System.Security.Cryptography;
using System.Text;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class RefreshServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid SessionId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid UserId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid DeviceId = Guid.Parse("44f70c86-b7f3-4a1d-b2a9-1f9e5c4d8a17");
    private static readonly Guid CorrelationId = Guid.Parse("a3b9e1f2-9c8d-4e7f-b6a5-1c2d3e4f5a6b");
    private static readonly Guid RequestId = Guid.Parse("d5e6f7a8-9b0c-4d1e-8f2a-3b4c5d6e7f80");

    private const string RefreshToken = "presented-refresh-token";
    private const string DeviceKey = "client-device-key";
    private const long CredentialVersion = 5;
    private const long AuthorizationScopeVersion = 3;

    private static readonly SessionSnapshot ActiveSession = new(
        SessionId,
        OrganizationId,
        UserId,
        null,
        CredentialVersion,
        AuthorizationScopeVersion,
        DateTimeOffset.UtcNow.AddHours(-1),
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddHours(8),
        null,
        null);

    private static RefreshService CreateService(
        FakeSessionRepository repository,
        FakeDeviceStore? deviceStore = null,
        FakeAuditStore? auditStore = null) =>
        new(
            repository,
            new RefreshTokenRotationService(repository),
            deviceStore ?? new FakeDeviceStore(),
            auditStore ?? new FakeAuditStore());

    private static RefreshCommand CreateCommand(string refreshToken = RefreshToken) =>
        new(refreshToken, DeviceKey, CorrelationId, RequestId);

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WrongDeviceKey_DoesNotRotate()
    {
        var repository = new FakeSessionRepository { Lookup=CreateLookup(TokenStatus.Active,DeviceId) };
        var devices = new FakeDeviceStore { Device=new(DeviceId,UserId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DeviceKey))).ToLowerInvariant(),null) };
        var result = await CreateService(repository,devices).RefreshAsync(CreateCommand() with { DeviceKey="another-device" });
        Assert.IsType<RefreshOutcome.SessionExpired>(result);
        Assert.Empty(repository.RotateCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_StaleCredentialVersion_DoesNotRotate()
    {
        var repository = new FakeSessionRepository { Lookup=CreateLookup(TokenStatus.Active), RequestState=SessionRequestState.VersionMismatch };
        var result = await CreateService(repository).RefreshAsync(CreateCommand());
        Assert.IsType<RefreshOutcome.SessionExpired>(result);
        Assert.Empty(repository.RotateCalls);
    }

    private static SessionRefreshLookup CreateLookup(TokenStatus status, Guid? deviceId = null) =>
        new(
            OrganizationId,
            SessionId,
            UserId,
            deviceId,
            CredentialVersion,
            AuthorizationScopeVersion,
            status);

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_ActiveToken_RotatesAndReturnsSucceeded()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active),
            ActiveSession = ActiveSession,
            ShouldRotateSucceed = true,
        };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        var succeeded = Assert.IsType<RefreshOutcome.Succeeded>(outcome);
        Assert.Equal(SessionId, succeeded.SessionId);
        Assert.Equal(UserId, succeeded.UserId);
        Assert.Equal(OrganizationId, succeeded.OrganizationId);
        Assert.Equal(CredentialVersion, succeeded.CredentialVersion);
        Assert.Equal(AuthorizationScopeVersion, succeeded.AuthorizationScopeVersion);
        Assert.Equal(43, succeeded.NewRefreshToken.Length);

        var call = Assert.Single(repository.RotateCalls);
        Assert.Equal(OrganizationId, call.OrganizationId);
        Assert.Equal(SessionId, call.SessionId);
        Assert.Equal(ComputeHash(RefreshToken), call.PresentedHash);
        Assert.Equal(ComputeHash(succeeded.NewRefreshToken), call.NewToken.TokenHash);
        Assert.Equal(succeeded.RefreshExpiresAtUtc, call.NewToken.ExpiresAtUtc);
        Assert.True(succeeded.RefreshExpiresAtUtc > DateTimeOffset.UtcNow.AddDays(29));
        Assert.True(succeeded.RefreshExpiresAtUtc < DateTimeOffset.UtcNow.AddDays(31));
        Assert.Empty(repository.RevokeCalls);

        var audit = Assert.Single(auditStore.Entries);
        Assert.Equal("SessionRefreshed", audit.ActionCode);
        Assert.Equal("success", audit.Outcome);
        Assert.Null(audit.ReasonCode);
        Assert.Equal(OrganizationId, audit.OrganizationId);
        Assert.Equal(UserId, audit.ActorUserId);
        Assert.Equal(SessionId, audit.ActorSessionId);
        Assert.Equal(CorrelationId, audit.CorrelationId);
        Assert.Equal(RequestId, audit.RequestId);
        Assert.Equal("standard", audit.RedactionLevel);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_UnknownTokenHash_ReturnsSessionExpired()
    {
        var repository = new FakeSessionRepository { Lookup = null };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.SessionExpired>(outcome);
        Assert.Empty(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_ExpiredToken_ReturnsSessionExpired()
    {
        var repository = new FakeSessionRepository { Lookup = CreateLookup(TokenStatus.Expired) };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.SessionExpired>(outcome);
        Assert.Empty(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_RevokedToken_ReturnsSessionRevoked()
    {
        var repository = new FakeSessionRepository { Lookup = CreateLookup(TokenStatus.Revoked) };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.SessionRevoked>(outcome);
        Assert.Empty(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_ConsumedToken_RevokesSessionAuditsAndReturnsReuseDetected()
    {
        var repository = new FakeSessionRepository { Lookup = CreateLookup(TokenStatus.Consumed) };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.ReuseDetected>(outcome);
        Assert.Empty(repository.RotateCalls);

        var revoke = Assert.Single(repository.RevokeCalls);
        Assert.Equal(OrganizationId, revoke.OrganizationId);
        Assert.Equal(SessionId, revoke.SessionId);
        Assert.Equal("refresh-token-reuse", revoke.Reason);

        var audit = Assert.Single(auditStore.Entries);
        Assert.Equal("RefreshTokenReuse", audit.ActionCode);
        Assert.Equal("failed", audit.Outcome);
        Assert.Equal("REFRESH_TOKEN_REUSE", audit.ReasonCode);
        Assert.Equal(OrganizationId, audit.OrganizationId);
        Assert.Equal(UserId, audit.ActorUserId);
        Assert.Equal(SessionId, audit.ActorSessionId);
        Assert.Equal(CorrelationId, audit.CorrelationId);
        Assert.Equal(RequestId, audit.RequestId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithRevokedDevice_ReturnsDeviceRevoked()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active, deviceId: DeviceId),
        };
        var deviceStore = new FakeDeviceStore
        {
            Device = new DeviceRegistrationRecord(
                DeviceId, UserId, "fingerprint-hash", DateTimeOffset.UtcNow.AddMinutes(-1)),
        };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, deviceStore, auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.DeviceRevoked>(outcome);
        Assert.Empty(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithMissingDevice_ReturnsSessionExpired()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active, deviceId: DeviceId),
        };
        var deviceStore = new FakeDeviceStore { Device = null };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, deviceStore, auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.SessionExpired>(outcome);
        Assert.Empty(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WhenRotationUnknownToken_ReturnsSessionExpired()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active),
            ActiveSession = null,
            ShouldRotateSucceed = false,
        };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.SessionExpired>(outcome);
        Assert.Single(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WhenRotationReuseDetected_ReturnsReuseDetected()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active),
            ActiveSession = ActiveSession,
            ShouldRotateSucceed = false,
        };
        var auditStore = new FakeAuditStore();
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.ReuseDetected>(outcome);
        Assert.Single(repository.RotateCalls);

        var revoke = Assert.Single(repository.RevokeCalls);
        Assert.Equal(OrganizationId, revoke.OrganizationId);
        Assert.Equal(SessionId, revoke.SessionId);
        Assert.Equal("refresh-token-reuse", revoke.Reason);
        Assert.Empty(auditStore.Entries);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WhenAuditAppendThrows_StillReturnsSucceeded()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active),
            ActiveSession = ActiveSession,
            ShouldRotateSucceed = true,
        };
        var auditStore = new FakeAuditStore
        {
            ThrowOnAppend = new InvalidOperationException("journal unavailable"),
        };
        var service = CreateService(repository, auditStore: auditStore);

        var outcome = await service.RefreshAsync(CreateCommand());

        Assert.IsType<RefreshOutcome.Succeeded>(outcome);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WhenAuditAppendCancelled_PropagatesCancellation()
    {
        var repository = new FakeSessionRepository
        {
            Lookup = CreateLookup(TokenStatus.Active),
            ActiveSession = ActiveSession,
            ShouldRotateSucceed = true,
        };
        var auditStore = new FakeAuditStore
        {
            ThrowOnAppend = new OperationCanceledException(),
        };
        var service = CreateService(repository, auditStore: auditStore);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAsync(CreateCommand()));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithNullCommand_ThrowsArgumentNullException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RefreshAsync(null!));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithEmptyRefreshToken_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(CreateCommand("")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(CreateCommand("   ")));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithEmptyDeviceKey_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(new RefreshCommand(RefreshToken, "", CorrelationId, RequestId)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(new RefreshCommand(RefreshToken, "   ", CorrelationId, RequestId)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithEmptyCorrelationId_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(new RefreshCommand(RefreshToken, DeviceKey, Guid.Empty, RequestId)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RefreshAsync_WithEmptyRequestId_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RefreshAsync(new RefreshCommand(RefreshToken, DeviceKey, CorrelationId, Guid.Empty)));
    }

    [Fact]
    public void RefreshService_WithNullDependencies_ThrowsArgumentNullException()
    {
        var repository = new FakeSessionRepository();
        var rotation = new RefreshTokenRotationService(repository);
        var deviceStore = new FakeDeviceStore();
        var auditStore = new FakeAuditStore();

        Assert.Throws<ArgumentNullException>(() => new RefreshService(null!, rotation, deviceStore, auditStore));
        Assert.Throws<ArgumentNullException>(() => new RefreshService(repository, null!, deviceStore, auditStore));
        Assert.Throws<ArgumentNullException>(() => new RefreshService(repository, rotation, null!, auditStore));
        Assert.Throws<ArgumentNullException>(() => new RefreshService(repository, rotation, deviceStore, null!));
    }

    private static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionRequestState RequestState { get; set; } = SessionRequestState.Active;
        public SessionRefreshLookup? Lookup { get; set; }

        public SessionSnapshot? ActiveSession { get; set; }

        public bool ShouldRotateSucceed { get; set; }

        public List<(Guid OrganizationId, Guid SessionId, string PresentedHash, RefreshTokenRecord NewToken)> RotateCalls { get; } = [];

        public List<(Guid OrganizationId, Guid SessionId, string? Reason)> RevokeCalls { get; } = [];

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            ActiveSession;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            throw new global::System.NotImplementedException();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) => Lookup;

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) => RequestState;

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) =>
            throw new global::System.NotImplementedException();

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken)
        {
            RotateCalls.Add((organizationId, sessionId, consumedTokenHash, newRefreshToken));
            return ShouldRotateSucceed;
        }

        public void TouchSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason)
        {
            RevokeCalls.Add((organizationId, sessionId, reason));
        }

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

    private sealed class FakeDeviceStore : IDeviceRegistrationStore
    {
        public DeviceRegistrationRecord? Device { get; set; }

        public global::System.Threading.Tasks.Task<Guid> UpsertAsync(
            Guid organizationId,
            Guid userId,
            string fingerprintHash,
            string? displayName,
            CancellationToken cancellationToken = default) =>
            throw new global::System.NotImplementedException();

        public global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(
            Guid organizationId,
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(Device);
    }

    private sealed class FakeAuditStore : IAuditEntryStore
    {
        public List<AuditEntryRecord> Entries { get; } = [];

        public Exception? ThrowOnAppend { get; set; }

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
}
