using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Auth;
using Task.Api.Security;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class AuthSessionEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherSessionId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private const string CurrentPassword = "correct-current";
    private const string NewPassword = "Strong-N3w!Password";

    private static readonly Lazy<TestKeyMaterial> KeyMaterial = new(CreateKeyMaterial);

    private sealed record TestKeyMaterial(string PrivateKeyPath, string VerificationKeysDirectory);

    private static TestKeyMaterial CreateKeyMaterial()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"task-auth-session-tests-{Guid.NewGuid():N}");
        var signingDirectory = Path.Combine(baseDirectory, "signing");
        var verificationDirectory = Path.Combine(baseDirectory, "verification");
        Directory.CreateDirectory(signingDirectory);
        Directory.CreateDirectory(verificationDirectory);

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPath = Path.Combine(signingDirectory, "task-signing.pem");
        File.WriteAllText(privateKeyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            Path.Combine(verificationDirectory, "task-signing.pem"),
            ecdsa.ExportSubjectPublicKeyInfoPem());

        return new TestKeyMaterial(privateKeyPath, verificationDirectory);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_Returns204_AndRevokesCurrentSession()
    {
        var sessionRepository = new FakeSessionRepository();
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var revocation = Assert.Single(sessionRepository.Revoked);
        Assert.Equal(OrganizationId, revocation.OrganizationId);
        Assert.Equal(SessionId, revocation.SessionId);
        Assert.Equal("user-logout", revocation.Reason);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LogoutAll_Returns200_WithRevokedSessionCount()
    {
        var sessionRepository = new FakeSessionRepository { RevokeAllCount = 3 };
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsync("/api/v1/auth/logout-all", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        Assert.Equal(3, document.RootElement.GetProperty("revokedSessionCount").GetInt32());
        Assert.NotNull(sessionRepository.LastRevokeAll);
        Assert.Equal(OrganizationId, sessionRepository.LastRevokeAll!.Value.OrganizationId);
        Assert.Equal(UserId, sessionRepository.LastRevokeAll!.Value.UserId);
        Assert.Equal(SessionId, sessionRepository.LastRevokeAll!.Value.ExceptSessionId);
        Assert.Equal("user-logout-all", sessionRepository.LastRevokeAll!.Value.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async global::System.Threading.Tasks.Task CurrentSession_Returns200_WithClaimProjection(
        bool mustChangePassword)
    {
        using var server = CreateServer(new FakeSessionRepository(), services =>
        {
            services.AddSingleton<IAccountCredentialStore>(new FakeAccountCredentialStore(
                new AccountCredential(CurrentPassword, "{}", 1, "active"),
                mustChangePassword: mustChangePassword));
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync("/api/v1/auth/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        Assert.Equal(UserId.ToString("D"), document.RootElement.GetProperty("userId").GetString());
        Assert.Equal(SessionId.ToString("D"), document.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(OrganizationId.ToString("D"), document.RootElement.GetProperty("organizationId").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("credentialVersion").GetInt64());
        Assert.Equal(1, document.RootElement.GetProperty("authorizationScopeVersion").GetInt64());
        Assert.Equal(mustChangePassword, document.RootElement.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Sessions_Returns200_WithProjectedList()
    {
        var now = DateTimeOffset.UtcNow;
        var sessionRepository = new FakeSessionRepository();
        sessionRepository.SessionList.Add(new UserSessionListItem(
            SessionId,
            "Work PC",
            now.AddDays(-2),
            now.AddMinutes(-1),
            now.AddHours(1),
            now.AddDays(1),
            null,
            null));
        sessionRepository.SessionList.Add(new UserSessionListItem(
            OtherSessionId,
            null,
            now.AddDays(-5),
            now.AddDays(-2),
            now.AddHours(-1),
            now,
            now.AddDays(-2),
            "user-revoked"));
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync("/api/v1/auth/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        var items = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(SessionId.ToString("D"), items[0].GetProperty("sessionId").GetString());
        Assert.Equal("Work PC", items[0].GetProperty("deviceDisplayName").GetString());
        Assert.True(items[0].TryGetProperty("createdAtUtc", out _));
        Assert.True(items[0].TryGetProperty("lastSeenAtUtc", out _));
        Assert.True(items[0].TryGetProperty("idleExpiresAtUtc", out _));
        Assert.True(items[0].TryGetProperty("absoluteExpiresAtUtc", out _));
        Assert.True(items[0].GetProperty("revokedAtUtc").ValueKind == JsonValueKind.Null);
        Assert.True(items[0].GetProperty("revokeReason").ValueKind == JsonValueKind.Null);
        Assert.Equal(OtherSessionId.ToString("D"), items[1].GetProperty("sessionId").GetString());
        Assert.True(items[1].GetProperty("deviceDisplayName").ValueKind == JsonValueKind.Null);
        Assert.Equal("user-revoked", items[1].GetProperty("revokeReason").GetString());
    }

    private static TestServer CreateServer(
        FakeSessionRepository sessionRepository,
        Action<IServiceCollection>? configure = null)
    {
        var keyMaterial = KeyMaterial.Value;
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddProblemDetails();
                services.AddTaskApiSecurityFoundation();
                services.AddSingleton<IOptions<TaskIdentityFoundationOptions>>(
                    new OptionsWrapper<TaskIdentityFoundationOptions>(new TaskIdentityFoundationOptions
                    {
                        Issuer = Issuer,
                        Audience = Audience,
                        SigningKeyReference = $"file:{keyMaterial.PrivateKeyPath}",
                        PepperReference = "file:/run/secrets/task-pepper",
                        VerificationKeysDirectory = $"file:{keyMaterial.VerificationKeysDirectory}",
                    }));
                services.AddSingleton<ISessionRepository>(sessionRepository);
                services.AddSingleton<IAuditEntryStore>(new FakeAuditEntryStore());
                services.AddSingleton<IAccountCredentialStore>(new FakeAccountCredentialStore(
                    new AccountCredential(CurrentPassword, "{}", 1, "active")));
                services.AddSingleton<IPasswordHasher>(new PasswordEqualsHashHasher());
                services.AddSingleton<PasswordChangeService>();
                services.AddSingleton<IAuthorizationPolicyStore>(new FakeAuthorizationPolicyStore());
                services.AddSingleton<PermissionDecisionService>();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton(
                    new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyMaterial.PrivateKeyPath}"));
                configure?.Invoke(services);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = Guid.NewGuid().ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapAuthSessionEndpoints());
            }));
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(TestServer server)
    {
        var client = server.CreateClient();
        var issuer = server.Host.Services.GetRequiredService<JwtAccessTokenIssuer>();
        var token = await issuer.IssueAsync(
            new JwtIssuanceRequest(UserId, SessionId, OrganizationId, 1, 1),
            CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonDocument> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = await ReadJsonAsync(response);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        return document;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RevokeForeignSession_Returns403_Forbidden()
    {
        var sessionRepository = new FakeSessionRepository
        {
            OwnedSession = OwnSession(with: OtherUserId),
        };
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsync(
            $"/api/v1/auth/sessions/{OtherSessionId:D}/revoke",
            null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(sessionRepository.Revoked);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RevokeUnknownSession_Returns404_ObjectNotVisible()
    {
        var sessionRepository = new FakeSessionRepository();
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsync(
            $"/api/v1/auth/sessions/{OtherSessionId:D}/revoke",
            null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
        Assert.Empty(sessionRepository.Revoked);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RevokeOwnSession_Returns204_AndIsIdempotent()
    {
        var sessionRepository = new FakeSessionRepository
        {
            OwnedSession = OwnSession(with: UserId),
        };
        using var server = CreateServer(sessionRepository);
        using var client = await CreateAuthenticatedClientAsync(server);
        var url = $"/api/v1/auth/sessions/{OtherSessionId:D}/revoke";

        // Both calls succeed: the endpoint is idempotent because GetSession returns sessions
        // in any state (including revoked), so the second call still finds the session.
        var first = await client.PostAsync(url, null);
        var second = await client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var revocations = sessionRepository.Revoked;
        Assert.Equal(2, revocations.Count);
        Assert.All(revocations, revocation =>
        {
            Assert.Equal(OtherSessionId, revocation.SessionId);
            Assert.Equal("user-revoked", revocation.Reason);
        });
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_Success_Returns204()
    {
        using var server = CreateServer(new FakeSessionRepository());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = CurrentPassword, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        var credentialStore = Assert.IsType<FakeAccountCredentialStore>(
            server.Host.Services.GetRequiredService<IAccountCredentialStore>());
        var commit = Assert.Single(credentialStore.CommitCalls);
        Assert.Equal(OrganizationId, commit.OrganizationId);
        Assert.Equal(UserId, commit.UserId);
        Assert.Equal(SessionId, commit.CurrentSessionId);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_UnknownAccount_Returns401_InvalidCredentials()
    {
        var sessionRepository = new FakeSessionRepository();
        using var server = CreateServer(sessionRepository, services =>
        {
            services.AddSingleton<IAccountCredentialStore>(new FakeAccountCredentialStore(null));
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = CurrentPassword, newPassword = NewPassword });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_InvalidCurrentPassword_Returns401_InvalidCredentials()
    {
        using var server = CreateServer(new FakeSessionRepository());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = "wrong-current", newPassword = NewPassword });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_BlockedAccount_Returns423_AccountBlocked()
    {
        using var server = CreateServer(new FakeSessionRepository(), services =>
        {
            services.AddSingleton<IAccountCredentialStore>(new FakeAccountCredentialStore(
                new AccountCredential(CurrentPassword, "{}", 1, "blocked")));
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = CurrentPassword, newPassword = NewPassword });

        await AssertProblemAsync(response, (HttpStatusCode)423, "ACCOUNT_BLOCKED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_WeakPassword_Returns422_ValidationFailed()
    {
        using var server = CreateServer(new FakeSessionRepository());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = CurrentPassword, newPassword = "weakpass" });

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_ReuseDetected_Returns422_ValidationFailed()
    {
        using var server = CreateServer(new FakeSessionRepository(), services =>
        {
            services.AddSingleton<IAccountCredentialStore>(new FakeAccountCredentialStore(
                new AccountCredential(CurrentPassword, "{}", 1, "active"),
                new[] { new PasswordHashRecord("Old-Password-2024!", "{}") }));
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = CurrentPassword, newPassword = "Old-Password-2024!" });

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_EmptyFields_Returns422_ValidationFailed()
    {
        using var server = CreateServer(new FakeSessionRepository());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = "", newPassword = NewPassword });

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAttempts_Returns200_WithFiltersAndPagination()
    {
        var auditStore = new FakeAuditEntryStore(
            LoginEntry("UserLoggedIn", "success", null, "2026-01-01T10:00:00Z"),
            LoginEntry("LoginFailed", "failed", "INVALID_CREDENTIALS", "2026-01-01T11:00:00Z"),
            LoginEntry("SessionRefreshed", "success", null, "2026-01-01T12:00:00Z"))
        {
            NextPageToken = "next-page-token",
        };
        var sessionRepository = new FakeSessionRepository();
        using var server = CreateServer(sessionRepository, services =>
        {
            services.AddSingleton<IAuditEntryStore>(auditStore);
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(
            "/api/v1/auth/login-attempts?result=failed&from=2026-01-01T09%3A00%3A00Z"
            + "&to=2026-01-01T12%3A00%3A00Z&pageToken=abc&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(auditStore.LastQuery);
        Assert.Equal(OrganizationId, auditStore.LastQuery!.OrgId);
        Assert.Null(auditStore.LastQuery!.ActionFilter);
        Assert.Equal("failed", auditStore.LastQuery!.OutcomeFilter);
        Assert.Equal("abc", auditStore.LastQuery!.PageToken);
        Assert.Equal(5, auditStore.LastQuery!.PageSize);
        Assert.NotNull(auditStore.LastQuery!.FromUtc);
        Assert.NotNull(auditStore.LastQuery!.ToUtc);

        var document = await ReadJsonAsync(response);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("failed", items[0].GetProperty("outcome").GetString());
        Assert.Equal("INVALID_CREDENTIALS", items[0].GetProperty("reasonCode").GetString());
        Assert.Equal(UserId.ToString("D"), items[0].GetProperty("actorUserId").GetString());
        Assert.True(items[0].TryGetProperty("occurredAtUtc", out _));
        Assert.Equal("next-page-token", document.RootElement.GetProperty("nextPageToken").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAttempts_WithoutFilters_ProjectsOnlyLoginEvents()
    {
        var auditStore = new FakeAuditEntryStore(
            LoginEntry("UserLoggedIn", "success", null, "2026-01-01T10:00:00Z"),
            LoginEntry("LoginFailed", "failed", "INVALID_CREDENTIALS", "2026-01-01T11:00:00Z"),
            LoginEntry("SessionRefreshed", "success", null, "2026-01-01T12:00:00Z"));
        using var server = CreateServer(new FakeSessionRepository(), services =>
        {
            services.AddSingleton<IAuditEntryStore>(auditStore);
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync("/api/v1/auth/login-attempts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, auditStore.LastQuery!.PageSize);
        var document = await ReadJsonAsync(response);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.True(document.RootElement.GetProperty("nextPageToken").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAttempts_WithoutPermission_Returns403()
    {
        using var server = CreateServer(new FakeSessionRepository(), services =>
        {
            services.AddSingleton<IAuthorizationPolicyStore>(new FakeAuthorizationPolicyStore
            {
                Grants = [],
            });
        });
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync("/api/v1/auth/login-attempts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginAttempts_FromAfterTo_Returns422_ValidationFailed()
    {
        using var server = CreateServer(new FakeSessionRepository());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(
            "/api/v1/auth/login-attempts?from=2026-01-02T00%3A00%3A00Z&to=2026-01-01T00%3A00%3A00Z");

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task SessionEndpoints_WithoutToken_Return401()
    {
        using var server = CreateServer(new FakeSessionRepository());
        var client = server.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/session");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    private static SessionSnapshot OwnSession(Guid with) => new(
        OtherSessionId,
        OrganizationId,
        with,
        null,
        1,
        1,
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddDays(1),
        null,
        null);

    private static AuditEntryRecord LoginEntry(
        string actionCode,
        string outcome,
        string? reasonCode,
        string occurredAt) =>
        new(
            Guid.NewGuid(),
            OrganizationId,
            DateTimeOffset.Parse(occurredAt),
            UserId,
            SessionId,
            actionCode,
            outcome,
            reasonCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            AuditEntryRecord.DefaultMetadata,
            null,
            null,
            "standard");

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionSnapshot? OwnedSession { get; set; }

        public int RevokeAllCount { get; set; }

        public List<UserSessionListItem> SessionList { get; } = new();

        public List<(Guid OrganizationId, Guid SessionId, string? Reason)> Revoked { get; } = new();

        public (Guid OrganizationId, Guid UserId, Guid? ExceptSessionId, string? Reason)? LastRevokeAll { get; private set; }

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => null;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => OwnedSession;

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            SessionList;

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) => null;

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            SessionRequestState.Active;

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken)
        {
        }

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) =>
            true;

        public void TouchSession(Guid organizationId, Guid sessionId)
        {
        }

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) =>
            Revoked.Add((organizationId, sessionId, reason));

        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason)
        {
            LastRevokeAll = (organizationId, userId, exceptSessionId, reason);
            return RevokeAllCount;
        }

        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);

        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);

        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);
    }

    private sealed class FakeAccountCredentialStore : IAccountCredentialStore
    {
        private readonly AccountCredential? _credential;
        private readonly IReadOnlyList<PasswordHashRecord> _history;
        private readonly bool _mustChangePassword;

        public List<(Guid OrganizationId, Guid UserId, Guid? CurrentSessionId)> CommitCalls { get; } = [];

        public FakeAccountCredentialStore(
            AccountCredential? credential,
            IReadOnlyList<PasswordHashRecord>? history = null,
            bool mustChangePassword = false)
        {
            _credential = credential;
            _history = history ?? Array.Empty<PasswordHashRecord>();
            _mustChangePassword = mustChangePassword;
        }

        public global::System.Threading.Tasks.Task<bool> GetMustChangePasswordAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(_mustChangePassword);

        public global::System.Threading.Tasks.Task<AccountCredential?> GetCredentialAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(_credential);

        public global::System.Threading.Tasks.Task<bool> UpdateCredentialAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord hash,
            int newCredentialVersion,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(true);

        public global::System.Threading.Tasks.Task AddPasswordToHistoryAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord hash,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task<IReadOnlyList<PasswordHashRecord>> GetRecentPasswordHistoryAsync(
            Guid organizationId,
            Guid userId,
            int limit,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(_history);

        public global::System.Threading.Tasks.Task<PasswordChangeCommitResult> CommitPasswordChangeAsync(
            Guid organizationId,
            Guid userId,
            PasswordHashRecord expectedCurrentHash,
            PasswordHashRecord newHash,
            long expectedCredentialVersion,
            Guid? currentSessionId,
            CancellationToken cancellationToken = default)
        {
            CommitCalls.Add((organizationId, userId, currentSessionId));
            return global::System.Threading.Tasks.Task.FromResult(new PasswordChangeCommitResult(true, 0));
        }
    }

    private sealed class FakeAuthorizationPolicyStore : IAuthorizationPolicyStore
    {
        public IReadOnlyList<PolicyGrantRow> Grants { get; init; } =
            [new PolicyGrantRow(HasDirectRoleMembership: true)];

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<Guid?>(OrganizationId);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(Grants);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyDenyRow>>([]);
    }

    private sealed class PasswordEqualsHashHasher : IPasswordHasher
    {
        public PasswordHashRecord DummyPasswordHash => new("dummy-hash", "{}");

        public PasswordHashRecord HashPassword(string password) => new(password, "{}");

        public bool VerifyPassword(string password, PasswordHashRecord stored) =>
            stored.Hash == DummyPasswordHash.Hash ? false : stored.Hash == password;
    }

    private sealed class FakeAuditEntryStore : IAuditEntryStore
    {
        private readonly IReadOnlyList<AuditEntryRecord> _entries;

        public FakeAuditEntryStore(params AuditEntryRecord[] entries)
        {
            _entries = entries;
        }

        public AuditQuery? LastQuery { get; private set; }

        public string? NextPageToken { get; init; }

        public global::System.Threading.Tasks.Task AppendAsync(
            AuditEntryRecord entry,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task<AuditPage> ReadAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var filtered = _entries
                .Where(entry =>
                    query.OutcomeFilter is null || entry.Outcome == query.OutcomeFilter)
                .Where(entry =>
                    !query.FromUtc.HasValue || entry.OccurredAt >= query.FromUtc.Value)
                .Where(entry =>
                    !query.ToUtc.HasValue || entry.OccurredAt <= query.ToUtc.Value)
                .ToList();
            return global::System.Threading.Tasks.Task.FromResult(new AuditPage(filtered, NextPageToken));
        }
    }
}
