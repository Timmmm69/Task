using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class RefreshTokenRotationServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid SessionId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid UserId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");

    private static readonly SessionSnapshot ActiveSession = new(
        SessionId,
        OrganizationId,
        UserId,
        null,
        1,
        1,
        DateTimeOffset.UtcNow.AddHours(-1),
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddHours(8),
        null,
        null);

    private static RefreshTokenRotationService CreateService(FakeSessionRepository repository) =>
        new(repository);

    [Fact]
    public void GenerateToken_ReturnsBase64UrlRawAndSha256HexHash()
    {
        var service = CreateService(new FakeSessionRepository());

        var descriptor = service.GenerateToken();

        Assert.Equal(43, descriptor.RawToken.Length);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), descriptor.RawToken);
        Assert.Equal(64, descriptor.TokenHash.Length);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), descriptor.TokenHash);
        Assert.Equal(ComputeHash(descriptor.RawToken), descriptor.TokenHash);
    }

    [Fact]
    public void GenerateToken_TwoCalls_AreNotEqual()
    {
        var service = CreateService(new FakeSessionRepository());

        var first = service.GenerateToken();
        var second = service.GenerateToken();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_Success_ReturnsNewTokenAndExpiry()
    {
        var rawToken = "presented-token";
        var repository = new FakeSessionRepository { ShouldRotateSucceed = true };
        var service = CreateService(repository);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(7);

        var outcome = await service.RotateAsync(OrganizationId, SessionId, rawToken, newExpiry);

        var rotated = Assert.IsType<RotationOutcome.Rotated>(outcome);
        Assert.Equal(newExpiry, rotated.NewExpiryUtc);
        Assert.Equal(43, rotated.NewRefreshToken.Length);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), rotated.NewRefreshToken);

        var call = Assert.Single(repository.RotateCalls);
        Assert.Equal(OrganizationId, call.OrganizationId);
        Assert.Equal(SessionId, call.SessionId);
        Assert.Equal(ComputeHash(rawToken), call.PresentedHash);
        Assert.Equal(ComputeHash(rotated.NewRefreshToken), call.NewToken.TokenHash);
        Assert.Equal(newExpiry, call.NewToken.ExpiresAtUtc);

        Assert.Empty(repository.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WhenSessionNotActive_ReturnsUnknownToken()
    {
        var rawToken = "stale-token";
        var repository = new FakeSessionRepository
        {
            ShouldRotateSucceed = false,
            ActiveSession = null,
        };
        var service = CreateService(repository);

        var outcome = await service.RotateAsync(
            OrganizationId, SessionId, rawToken, DateTimeOffset.UtcNow.AddDays(7));

        Assert.IsType<RotationOutcome.UnknownToken>(outcome);
        Assert.Single(repository.RotateCalls);
        Assert.Empty(repository.RevokeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WhenReuseDetected_RevokesSessionWithReason()
    {
        var rawToken = "reused-token";
        var repository = new FakeSessionRepository
        {
            ShouldRotateSucceed = false,
            ActiveSession = ActiveSession,
        };
        var service = CreateService(repository);

        var outcome = await service.RotateAsync(
            OrganizationId, SessionId, rawToken, DateTimeOffset.UtcNow.AddDays(7));

        Assert.IsType<RotationOutcome.ReuseDetected>(outcome);
        Assert.Single(repository.RotateCalls);

        var revoke = Assert.Single(repository.RevokeCalls);
        Assert.Equal(OrganizationId, revoke.OrganizationId);
        Assert.Equal(SessionId, revoke.SessionId);
        Assert.Equal("refresh-token-reuse", revoke.Reason);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WithEmptyOrganization_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RotateAsync(
                Guid.Empty, SessionId, "token", DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WithEmptySession_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RotateAsync(
                OrganizationId, Guid.Empty, "token", DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WithNullOrWhiteSpaceRawToken_ThrowsArgumentException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RotateAsync(
                OrganizationId, SessionId, "", DateTimeOffset.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RotateAsync(
                OrganizationId, SessionId, "   ", DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RotateAsync_WithExpiryNotInFuture_ThrowsArgumentOutOfRangeException()
    {
        var service = CreateService(new FakeSessionRepository());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RotateAsync(
                OrganizationId, SessionId, "token", DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void RefreshTokenRotationService_WithNullRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RefreshTokenRotationService(null!));
    }

    private static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public bool ShouldRotateSucceed { get; set; }

        public SessionSnapshot? ActiveSession { get; set; }

        public List<(Guid OrganizationId, Guid SessionId, string PresentedHash, RefreshTokenRecord NewToken)> RotateCalls { get; } = [];

        public List<(Guid OrganizationId, Guid SessionId, string? Reason)> RevokeCalls { get; } = [];

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            organizationId == OrganizationId && sessionId == SessionId ? ActiveSession : null;

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
}
