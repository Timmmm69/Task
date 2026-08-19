using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class PasswordChangeServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid UserId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid CurrentSessionId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");

    private const string CurrentPassword = "OldPassword1!";
    private const string NewPassword = "NewStrongPass1!";

    private static PasswordChangeService CreateService(
        FakeCredentialStore store,
        FakeSessionRepository sessions,
        int? historyLimit = null) =>
        new(
            store,
            sessions,
            new FakePasswordHasher(),
            historyLimit ?? PasswordChangeService.DefaultHistoryLimit);

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_Success_RotatesCredentialAndRevokesOtherSessions()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository { RevokedCount = 2 };
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.Success, result.Outcome);
        Assert.Equal(2, result.RevokedSessionCount);

        var update = Assert.Single(store.Updates);
        Assert.Equal(OrganizationId, update.OrganizationId);
        Assert.Equal(UserId, update.UserId);
        Assert.Equal(2, update.NewVersion);
        Assert.Equal($"hash:{NewPassword}", update.Hash.Hash);
        Assert.Equal($"params:{NewPassword}", update.Hash.Parameters);

        var archived = Assert.Single(store.HistoryWrites);
        Assert.Equal($"hash:{CurrentPassword}", archived.Hash.Hash);

        var revoke = Assert.Single(sessions.RevokeCalls);
        Assert.Equal(OrganizationId, revoke.OrganizationId);
        Assert.Equal(UserId, revoke.UserId);
        Assert.Equal(CurrentSessionId, revoke.ExceptSessionId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_Success_WithNullCurrentSession_RevokesAllSessions()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, null);

        Assert.Equal(PasswordChangeOutcome.Success, result.Outcome);
        var revoke = Assert.Single(sessions.RevokeCalls);
        Assert.Null(revoke.ExceptSessionId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_ForUnknownAccount_ReturnsUnknownAccountAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = null };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.UnknownAccount, result.Outcome);
        Assert.Equal(0, result.RevokedSessionCount);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(sessions.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_ForBlockedAccount_ReturnsAccountBlockedAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential("blocked") };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.AccountBlocked, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(sessions.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithWrongCurrentPassword_ReturnsInvalidCurrentPasswordAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, "WrongPassword1!", NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.InvalidCurrentPassword, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(sessions.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithNewPasswordEqualToCurrent_ReturnsPasswordReuseDetected()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, CurrentPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.PasswordReuseDetected, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(sessions.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithNewPasswordInHistory_ReturnsPasswordReuseDetected()
    {
        var store = new FakeCredentialStore
        {
            Credential = ActiveCredential(),
        };
        store.History.Add(new PasswordHashRecord($"hash:{NewPassword}", $"params:{NewPassword}"));
        store.History.Add(new PasswordHashRecord($"hash:EvenOlderPass1!", $"params:EvenOlderPass1!"));
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.PasswordReuseDetected, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(sessions.RevokeCalls);
        Assert.Equal(PasswordChangeService.DefaultHistoryLimit, store.LastHistoryReadLimit);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithCustomHistoryLimit_AsksForConfiguredLimit()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions, historyLimit: 3);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.Success, result.Outcome);
        Assert.Equal(3, store.LastHistoryReadLimit);
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("nouppercase1!")]
    [InlineData("NODIGITS!!!")]
    [InlineData("NoSpecialChar1")]
    [InlineData("1234567890!")]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithWeakNewPassword_ReturnsWeakPassword(string weakPassword)
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, weakPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.WeakPassword, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(sessions.RevokeCalls);
    }

    [Fact]
    public void ChangePasswordAsync_WithCustomHistoryLimitLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateService(
            new FakeCredentialStore(), new FakeSessionRepository(), historyLimit: 0));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyOrganization_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(Guid.Empty, UserId, CurrentPassword, NewPassword, CurrentSessionId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyUser_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, Guid.Empty, CurrentPassword, NewPassword, CurrentSessionId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyPasswords_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var sessions = new FakeSessionRepository();
        var service = CreateService(store, sessions);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, UserId, "", NewPassword, CurrentSessionId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, UserId, CurrentPassword, " ", CurrentSessionId));
    }

    [Fact]
    public void ChangePasswordService_WithNullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordChangeService(
            null!, new FakeSessionRepository(), new FakePasswordHasher()));
        Assert.Throws<ArgumentNullException>(() => new PasswordChangeService(
            new FakeCredentialStore(), null!, new FakePasswordHasher()));
        Assert.Throws<ArgumentNullException>(() => new PasswordChangeService(
            new FakeCredentialStore(), new FakeSessionRepository(), null!));
    }

    private static AccountCredential ActiveCredential(string accountStatus = "active") =>
        new($"hash:{CurrentPassword}", $"params:{CurrentPassword}", 1, accountStatus);

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public PasswordHashRecord HashPassword(string password) =>
            new($"hash:{password}", $"params:{password}");

        public bool VerifyPassword(string password, PasswordHashRecord stored) =>
            stored.Hash == $"hash:{password}";
    }

    private sealed class FakeCredentialStore : IAccountCredentialStore
    {
        public AccountCredential? Credential { get; set; }

        public List<PasswordHashRecord> History { get; } = [];

        public List<(Guid OrganizationId, Guid UserId, PasswordHashRecord Hash, int NewVersion)> Updates { get; } = [];

        public List<(Guid OrganizationId, Guid UserId, PasswordHashRecord Hash)> HistoryWrites { get; } = [];

        public int? LastHistoryReadLimit { get; private set; }

        public global::System.Threading.Tasks.Task<AccountCredential?> GetCredentialAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(Credential);

        public global::System.Threading.Tasks.Task<bool> UpdateCredentialAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord hash,
            int newCredentialVersion,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((organizationId, userId, hash, newCredentialVersion));
            return global::System.Threading.Tasks.Task.FromResult(true);
        }

        public global::System.Threading.Tasks.Task AddPasswordToHistoryAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord hash,
            CancellationToken cancellationToken = default)
        {
            HistoryWrites.Add((organizationId, userId, hash));
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PasswordHashRecord>> GetRecentPasswordHistoryAsync(
            Guid organizationId,
            Guid userId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastHistoryReadLimit = limit;
            return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<PasswordHashRecord>>(
                History.ToArray());
        }
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public int RevokedCount { get; set; }

        public List<(Guid OrganizationId, Guid UserId, Guid? ExceptSessionId)> RevokeCalls { get; } = [];

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            throw new global::System.NotImplementedException();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) =>
            throw new global::System.NotImplementedException();

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            throw new global::System.NotImplementedException();

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) =>
            throw new global::System.NotImplementedException();

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
            CancellationToken cancellationToken = default)
        {
            RevokeCalls.Add((organizationId, userId, exceptSessionId));
            return global::System.Threading.Tasks.Task.FromResult(RevokedCount);
        }

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
}