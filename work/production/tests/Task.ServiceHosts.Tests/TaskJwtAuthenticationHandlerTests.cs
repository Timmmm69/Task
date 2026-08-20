using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class TaskJwtAuthenticationHandlerTests
{
    private const string Scheme = TaskApiSecurityFoundation.FoundationAuthenticationScheme;
    private const string KeyId = "test-key-1";
    private const string OtherKeyId = "test-key-2";
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string TraceId = "trace-test";

    private static readonly Guid UserAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly ECDsa OtherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly RSA Rs256Key = RSA.Create(2048);

    private static readonly ECDsaSecurityKey SigningCredentialsKey =
        new(SigningKey) { KeyId = KeyId };

    private static readonly ECDsaSecurityKey OtherKeyWithKnownKid =
        new(OtherKey) { KeyId = KeyId };

    private static readonly ECDsaSecurityKey OtherKeyWithUnknownKid =
        new(OtherKey) { KeyId = OtherKeyId };

    private static readonly string VerificationKeysDirectory = CreateKeysDirectory();

    private static string CreateKeysDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"task-jwt-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{KeyId}.pem"),
            SigningKey.ExportSubjectPublicKeyInfoPem());
        return directory;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithoutBearer_ReturnsNoResultEvenWithIdentityHeaders()
    {
        var (context, handler) = await CreateHandlerAsync(new FakeSessionRepository(ActiveSession()));
        context.Request.Headers["X-User-ID"] = "user-999";
        context.Request.Headers["X-Organization-ID"] = "org-999";
        context.Request.Headers["X-Role"] = "admin";

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
        Assert.False(context.Items.ContainsKey(TaskApiProblemResponse.AuthenticationResponseWrittenItemName));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithNonBearerScheme_ReturnsNoResult()
    {
        var (context, handler) = await CreateHandlerAsync(new FakeSessionRepository(ActiveSession()));
        context.Request.Headers["Authorization"] = "Basic dXNlcjpwYXNz";

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithValidTokenAndActiveSession_SucceedsAndStoresRequestContext()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(cver: 3, sver: 7));
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession(cver: 3, sver: 7)),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(Scheme, result.Principal!.Identity!.AuthenticationType);
        Assert.Equal(UserAccountId.ToString("D"), result.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(UserAccountId.ToString("D"), result.Principal.FindFirstValue("sub"));
        Assert.Equal(SessionId.ToString("D"), result.Principal.FindFirstValue("sid"));
        Assert.Equal(OrganizationId.ToString("D"), result.Principal.FindFirstValue("org"));
        Assert.Equal("3", result.Principal.FindFirstValue("cver"));
        Assert.Equal("7", result.Principal.FindFirstValue("sver"));

        var requestContext = Assert.IsType<AuthenticatedRequestContext>(
            context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName]);
        Assert.Equal(UserAccountId, requestContext.UserAccountId);
        Assert.Equal(SessionId, requestContext.SessionId);
        Assert.Equal(OrganizationId, requestContext.OrganizationId);
        Assert.Equal(3, requestContext.CredentialVersion);
        Assert.Equal(7, requestContext.AuthorizationScopeVersion);
        Assert.Equal(correlationId, requestContext.CorrelationId);
        Assert.Equal(TraceId, requestContext.TraceId);
        Assert.False(context.Items.ContainsKey(TaskApiProblemResponse.AuthenticationResponseWrittenItemName));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithIdentityHeadersAndValidBearer_IdentityComesFromTokenOnly()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims());
        var (context, handler) = await CreateHandlerAsync(new FakeSessionRepository(ActiveSession()));
        context.Request.Headers["Authorization"] = $"Bearer {token}";
        context.Request.Headers["X-User-ID"] = "user-999";
        context.Request.Headers["X-Organization-ID"] = "org-999";
        context.Request.Headers["X-Role"] = "admin";

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(UserAccountId.ToString("D"), result.Principal!.FindFirstValue("sub"));
        Assert.Equal(OrganizationId.ToString("D"), result.Principal.FindFirstValue("org"));
        Assert.DoesNotContain(result.Principal.Claims, claim => claim.Value == "user-999");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithExpiredToken_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(
            SigningCredentialsKey,
            DefaultClaims(),
            issuedAt: DateTime.UtcNow.AddMinutes(-2),
            expires: DateTime.UtcNow.AddMinutes(-1));
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession()),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(context, handler, "SESSION_EXPIRED", correlationId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithFutureNotBefore_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(
            SigningCredentialsKey,
            DefaultClaims(),
            issuedAt: DateTime.UtcNow,
            notBefore: DateTime.UtcNow.AddMinutes(5),
            expires: DateTime.UtcNow.AddMinutes(7));
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession()),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(context, handler, "SESSION_EXPIRED", correlationId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithExcessiveTokenLifetime_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(
            SigningCredentialsKey,
            DefaultClaims(),
            issuedAt: DateTime.UtcNow.AddMinutes(-6),
            expires: DateTime.UtcNow.AddMinutes(1));
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession()),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(context, handler, "SESSION_EXPIRED", correlationId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithSignatureFromAnotherKey_RejectsOnce()
    {
        var token = CreateToken(OtherKeyWithKnownKid, DefaultClaims());
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithUnknownKid_RejectsOnce()
    {
        var token = CreateToken(OtherKeyWithUnknownKid, DefaultClaims());
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithMissingKid_RejectsOnce()
    {
        var credentialKeyWithoutKid = new ECDsaSecurityKey(SigningKey);
        var token = CreateToken(credentialKeyWithoutKid, DefaultClaims());
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithMultiValueAudience_RejectsOnce()
    {
        var claims = DefaultClaims();
        claims["aud"] = new[] { Audience, "another-client" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Claims = claims,
            SigningCredentials = new SigningCredentials(SigningCredentialsKey, SecurityAlgorithms.EcdsaSha256),
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(4),
        };
        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithWrongIssuer_RejectsOnce()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(), issuer: "https://evil.example.internal");
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithWrongAudience_RejectsOnce()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(), audience: "other-client");
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithRs256Algorithm_RejectsOnce()
    {
        var token = CreateToken(
            new RsaSecurityKey(Rs256Key) { KeyId = KeyId },
            SecurityAlgorithms.RsaSha256,
            DefaultClaims());
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithMalformedToken_RejectsOnce()
    {
        await AssertRejectedWithMatchingSessionAsync("not-a-jwt");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithNonGuidSubClaim_RejectsOnce()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(subOverride: "not-a-guid"));
        await AssertRejectedWithMatchingSessionAsync(token);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithMissingServerSession_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims());
        await AssertRejectedAsync(
            token,
            new FakeSessionRepository(null, SessionRequestState.SessionExpired),
            "SESSION_EXPIRED",
            "The session has expired.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithCredentialVersionMismatch_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(cver: 5));
        await AssertRejectedAsync(
            token,
            new FakeSessionRepository(ActiveSession(cver: 1), SessionRequestState.VersionMismatch),
            "SESSION_EXPIRED",
            "The session has expired.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithScopeVersionMismatch_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims(sver: 9));
        await AssertRejectedAsync(
            token,
            new FakeSessionRepository(ActiveSession(sver: 2), SessionRequestState.VersionMismatch),
            "SESSION_EXPIRED",
            "The session has expired.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithRevokedServerSession_RejectsOnceWithSessionRevoked()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims());
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession(), SessionRequestState.SessionRevoked),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(
            context,
            handler,
            "SESSION_REVOKED",
            correlationId,
            expectedRetryable: false,
            expectedTitle: "The session was revoked.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithExpiredServerSession_RejectsOnceWithSessionExpired()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims());
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession(), SessionRequestState.SessionExpired),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(
            context,
            handler,
            "SESSION_EXPIRED",
            correlationId,
            expectedTitle: "The session has expired.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithBlockedAccount_RejectsOnceWithAccountBlocked()
    {
        var token = CreateToken(SigningCredentialsKey, DefaultClaims());
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession(), SessionRequestState.AccountBlocked),
            correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(
            context,
            handler,
            "ACCOUNT_BLOCKED",
            correlationId,
            expectedStatusCode: StatusCodes.Status423Locked,
            expectedRetryable: false,
            expectedTitle: "The account is blocked.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithoutVerificationKeysOrSessionRepository_ReturnsNoResult()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(UrlEncoder.Default);
        services.AddTaskApiSecurityFoundation();
        services.AddSingleton(Options.Create(new TaskIdentityFoundationOptions()));

        using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.RequestServices = provider;
        context.Response.Body = new MemoryStream();
        context.Request.Headers["Authorization"] = "Bearer some-token";

        var handlerProvider = provider.GetRequiredService<IAuthenticationHandlerProvider>();
        var handler = await handlerProvider.GetHandlerAsync(context, Scheme);
        Assert.NotNull(handler);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Challenge_WithoutPendingResponse_WritesCorrelated401ProblemResponse()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(
            new FakeSessionRepository(ActiveSession()),
            correlationId);

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("AUTHENTICATION_REQUIRED", root.GetProperty("code").GetString());
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        Assert.Equal(TraceId, root.GetProperty("traceId").GetString());
        Assert.True(root.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Forbid_Writes403ProblemResponse()
    {
        var (context, handler) = await CreateHandlerAsync(new FakeSessionRepository(ActiveSession()));

        await handler.ForbidAsync(new AuthenticationProperties());

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("FORBIDDEN", root.GetProperty("code").GetString());
        Assert.False(root.GetProperty("retryable").GetBoolean());
    }

    private static async global::System.Threading.Tasks.Task AssertRejectedWithMatchingSessionAsync(
        string token)
    {
        await AssertRejectedAsync(
            token,
            new FakeSessionRepository(ActiveSession()),
            "AUTHENTICATION_REQUIRED",
            "Authentication is required.");
    }

    private static async global::System.Threading.Tasks.Task AssertRejectedAsync(
        string token,
        FakeSessionRepository sessionRepository,
        string expectedCode,
        string expectedTitle)
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync(sessionRepository, correlationId);
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        await AssertRejectedOnceAsync(
            context,
            handler,
            expectedCode,
            correlationId,
            expectedTitle: expectedTitle);
    }

    private static async global::System.Threading.Tasks.Task AssertRejectedOnceAsync(
        DefaultHttpContext context,
        IAuthenticationHandler handler,
        string expectedCode,
        string expectedCorrelationId,
        int expectedStatusCode = StatusCodes.Status401Unauthorized,
        bool expectedRetryable = true,
        string expectedTitle = "The session has expired.")
    {
        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Null(result.Principal);
        Assert.True(context.Items.ContainsKey(TaskApiProblemResponse.AuthenticationResponseWrittenItemName));

        var responseBytesBeforeChallenge = ((MemoryStream)context.Response.Body).ToArray();
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        Assert.Equal(TraceId, root.GetProperty("traceId").GetString());
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(expectedRetryable, root.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("exception", root.GetRawText(), StringComparison.OrdinalIgnoreCase);

        await handler.ChallengeAsync(new AuthenticationProperties());
        Assert.Equal(responseBytesBeforeChallenge, ((MemoryStream)context.Response.Body).ToArray());
    }

    private static async global::System.Threading.Tasks.Task<(DefaultHttpContext Context, IAuthenticationHandler Handler)> CreateHandlerAsync(
        FakeSessionRepository sessionRepository,
        string? correlationId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(UrlEncoder.Default);
        services.AddTaskApiSecurityFoundation();
        services.AddSingleton(Options.Create(IdentityOptions()));
        services.AddSingleton<ISessionRepository>(sessionRepository);

        using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.RequestServices = provider;
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = TraceId;
        if (correlationId is not null)
        {
            context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId;
        }

        var handlerProvider = provider.GetRequiredService<IAuthenticationHandlerProvider>();
        var handler = await handlerProvider.GetHandlerAsync(context, Scheme);

        Assert.NotNull(handler);
        return (context, handler);
    }

    private static TaskIdentityFoundationOptions IdentityOptions() =>
        new()
        {
            Issuer = Issuer,
            Audience = Audience,
            SigningKeyReference = "file:/run/secrets/task-signing",
            PepperReference = "file:/run/secrets/task-pepper",
            VerificationKeysDirectory = $"file:{VerificationKeysDirectory}",
        };

    private static string CreateToken(
        SecurityKey signingKey,
        Dictionary<string, object> claims,
        string? issuer = null,
        string? audience = null,
        DateTime? issuedAt = null,
        DateTime? notBefore = null,
        DateTime? expires = null) =>
        CreateToken(signingKey, SecurityAlgorithms.EcdsaSha256, claims, issuer, audience, issuedAt, notBefore, expires);

    private static string CreateToken(
        SecurityKey signingKey,
        string algorithm,
        Dictionary<string, object> claims,
        string? issuer = null,
        string? audience = null,
        DateTime? issuedAt = null,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey, algorithm),
            IssuedAt = issuedAt ?? DateTime.UtcNow,
            NotBefore = notBefore,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(4),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static Dictionary<string, object> DefaultClaims(
        Guid? sub = null,
        Guid? sid = null,
        Guid? org = null,
        long cver = 1,
        long sver = 1,
        string? subOverride = null) =>
        new()
        {
            ["sub"] = subOverride ?? (sub ?? UserAccountId).ToString("D"),
            ["sid"] = (sid ?? SessionId).ToString("D"),
            ["org"] = (org ?? OrganizationId).ToString("D"),
            ["cver"] = cver,
            ["sver"] = sver,
        };

    private static SessionSnapshot ActiveSession(long cver = 1, long sver = 1) =>
        new(
            SessionId,
            OrganizationId,
            UserAccountId,
            null,
            cver,
            sver,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            null);

    private sealed class FakeSessionRepository : ISessionRepository
    {
        private readonly SessionSnapshot? _session;
        private readonly SessionRequestState _requestState;

        public FakeSessionRepository(
            SessionSnapshot? session,
            SessionRequestState requestState = SessionRequestState.Active)
        {
            _session = session;
            _requestState = requestState;
        }

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => _session;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) =>
            throw new NotSupportedException();

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            throw new NotSupportedException();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) =>
            throw new NotSupportedException();

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            _requestState;

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) =>
            throw new NotSupportedException();

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) =>
            throw new NotSupportedException();

        public void TouchSession(Guid organizationId, Guid sessionId) =>
            throw new NotSupportedException();

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) =>
            throw new NotSupportedException();

        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}