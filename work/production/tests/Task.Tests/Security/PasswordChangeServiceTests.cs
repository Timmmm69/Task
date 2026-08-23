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
        int? historyLimit = null) =>
        new(
            store,
            new FakePasswordHasher(),
            historyLimit ?? PasswordChangeService.DefaultHistoryLimit);

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_Success_RotatesCredentialAndRevokesOtherSessions()
    {
        var store = new FakeCredentialStore
        {
            Credential = ActiveCredential(),
            CommitResult = new PasswordChangeCommitResult(true, 2),
        };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.Success, result.Outcome);
        Assert.Equal(2, result.RevokedSessionCount);

        var commit = Assert.Single(store.CommitCalls);
        Assert.Equal(OrganizationId, commit.OrganizationId);
        Assert.Equal(UserId, commit.UserId);
        Assert.Equal(1, commit.ExpectedCredentialVersion);
        Assert.Equal(CurrentSessionId, commit.CurrentSessionId);
        Assert.Equal($"hash:{CurrentPassword}", commit.ExpectedCurrentHash.Hash);
        Assert.Equal($"hash:{NewPassword}", commit.NewHash.Hash);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_Success_WithNullCurrentSession_RevokesAllSessions()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, null);

        Assert.Equal(PasswordChangeOutcome.Success, result.Outcome);
        var commit = Assert.Single(store.CommitCalls);
        Assert.Null(commit.CurrentSessionId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WhenAtomicCommitIsRejected_FailsClosed()
    {
        var store = new FakeCredentialStore
        {
            Credential = ActiveCredential(),
            CommitResult = new PasswordChangeCommitResult(false, 0),
        };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.InvalidCurrentPassword, result.Outcome);
        Assert.Equal(0, result.RevokedSessionCount);
        Assert.Single(store.CommitCalls);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_ForUnknownAccount_ReturnsUnknownAccountAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = null };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.UnknownAccount, result.Outcome);
        Assert.Equal(0, result.RevokedSessionCount);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_ForBlockedAccount_ReturnsAccountBlockedAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential("blocked") };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.AccountBlocked, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithWrongCurrentPassword_ReturnsInvalidCurrentPasswordAndWritesNothing()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, "WrongPassword1!", NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.InvalidCurrentPassword, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithNewPasswordEqualToCurrent_ReturnsPasswordReuseDetected()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, CurrentPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.PasswordReuseDetected, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
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
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, NewPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.PasswordReuseDetected, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
        Assert.Equal(PasswordChangeService.DefaultHistoryLimit, store.LastHistoryReadLimit);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithCustomHistoryLimit_AsksForConfiguredLimit()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store, historyLimit: 3);

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
        var service = CreateService(store);

        var result = await service.ChangePasswordAsync(
            OrganizationId, UserId, CurrentPassword, weakPassword, CurrentSessionId);

        Assert.Equal(PasswordChangeOutcome.WeakPassword, result.Outcome);
        Assert.Empty(store.Updates);
        Assert.Empty(store.HistoryWrites);
        Assert.Empty(store.MustChangePasswordResets);
        Assert.Empty(store.CommitCalls);
    }

    [Fact]
    public void ChangePasswordAsync_WithCustomHistoryLimitLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateService(
            new FakeCredentialStore(), historyLimit: 0));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyOrganization_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(Guid.Empty, UserId, CurrentPassword, NewPassword, CurrentSessionId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyUser_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, Guid.Empty, CurrentPassword, NewPassword, CurrentSessionId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePasswordAsync_WithEmptyPasswords_ThrowsArgumentException()
    {
        var store = new FakeCredentialStore { Credential = ActiveCredential() };
        var service = CreateService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, UserId, "", NewPassword, CurrentSessionId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ChangePasswordAsync(OrganizationId, UserId, CurrentPassword, " ", CurrentSessionId));
    }

    [Fact]
    public void ChangePasswordService_WithNullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordChangeService(
            null!, new FakePasswordHasher()));
        Assert.Throws<ArgumentNullException>(() => new PasswordChangeService(
            new FakeCredentialStore(), null!));
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

        public List<(Guid OrganizationId, Guid UserId)> MustChangePasswordResets { get; } = [];

        public int? LastHistoryReadLimit { get; private set; }

        public PasswordChangeCommitResult CommitResult { get; set; } =
            new(true, 0);

        public List<(
            Guid OrganizationId,
            Guid UserId,
            PasswordHashRecord ExpectedCurrentHash,
            PasswordHashRecord NewHash,
            long ExpectedCredentialVersion,
            Guid? CurrentSessionId)> CommitCalls
        { get; } = [];

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

        public global::System.Threading.Tasks.Task<bool> ResetMustChangePasswordAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            MustChangePasswordResets.Add((organizationId, userId));
            return global::System.Threading.Tasks.Task.FromResult(true);
        }

        public global::System.Threading.Tasks.Task<PasswordChangeCommitResult> CommitPasswordChangeAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord expectedCurrentHash,
            PasswordHashRecord newHash,
            long expectedCredentialVersion,
            Guid? currentSessionId,
            CancellationToken cancellationToken = default)
        {
            CommitCalls.Add((
                organizationId,
                userId,
                expectedCurrentHash,
                newHash,
                expectedCredentialVersion,
                currentSessionId));
            return global::System.Threading.Tasks.Task.FromResult(CommitResult);
        }
    }
}
