using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Audit;
using Task.Api.Security;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class AuditEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string AuditUrl = "/api/v1/audit";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EntryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RequestId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly Lazy<TestKeyMaterial> KeyMaterial = new(CreateKeyMaterial);

    private sealed record TestKeyMaterial(string PrivateKeyPath, string VerificationKeysDirectory);

    private static TestKeyMaterial CreateKeyMaterial()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"task-audit-tests-{Guid.NewGuid():N}");
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
    public async global::System.Threading.Tasks.Task GetAudit_WithoutToken_Returns401()
    {
        using var server = CreateServer(new FakeAuditEntryStore(), GrantAuditRead());
        var client = server.CreateClient();

        var response = await client.GetAsync(AuditUrl);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetAudit_WithTokenWithoutPermission_Returns403()
    {
        using var server = CreateServer(new FakeAuditEntryStore(), DenyAll());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(AuditUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetAudit_WithPermission_Returns200_WithExpectedShape()
    {
        var occurredAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var store = new FakeAuditEntryStore(
            new AuditEntryRecord(
                EntryId,
                OrganizationId,
                occurredAt,
                UserId,
                SessionId,
                "UserLoggedIn",
                "success",
                null,
                CorrelationId,
                RequestId,
                AuditEntryRecord.DefaultMetadata,
                null,
                null,
                "none"));
        using var server = CreateServer(store, GrantAuditRead());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(AuditUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        Assert.True(document.RootElement.TryGetProperty("items", out var items));
        Assert.True(document.RootElement.TryGetProperty("nextPageToken", out var nextPageToken));
        Assert.Equal(JsonValueKind.Null, nextPageToken.ValueKind);
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal(EntryId.ToString("D"), item.GetProperty("id").GetString());
        Assert.Equal(UserId.ToString("D"), item.GetProperty("actorUserId").GetString());
        Assert.Equal(SessionId.ToString("D"), item.GetProperty("actorSessionId").GetString());
        Assert.Equal("UserLoggedIn", item.GetProperty("actionCode").GetString());
        Assert.Equal("success", item.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("reasonCode").ValueKind);
        Assert.Equal(CorrelationId.ToString("D"), item.GetProperty("correlationId").GetString());
        Assert.Equal(RequestId.ToString("D"), item.GetProperty("requestId").GetString());
        Assert.True(item.TryGetProperty("occurredAtUtc", out _));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetAudit_ForwardsFiltersToStore()
    {
        var store = new FakeAuditEntryStore();
        using var server = CreateServer(store, GrantAuditRead());
        using var client = await CreateAuthenticatedClientAsync(server);

        var from = "2026-01-01T00:00:00Z";
        var to = "2026-08-01T00:00:00Z";
        var response = await client.GetAsync(
            $"{AuditUrl}?action=UserLoggedIn&outcome=success&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&pageToken=tok-1&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(store.LastQuery);
        Assert.Equal(OrganizationId, store.LastQuery!.OrgId);
        Assert.Equal("UserLoggedIn", store.LastQuery.ActionFilter);
        Assert.Equal("success", store.LastQuery.OutcomeFilter);
        Assert.Equal(DateTimeOffset.Parse(from), store.LastQuery.FromUtc);
        Assert.Equal(DateTimeOffset.Parse(to), store.LastQuery.ToUtc);
        Assert.Equal("tok-1", store.LastQuery.PageToken);
        Assert.Equal(25, store.LastQuery.PageSize);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetAudit_WhenFromAfterTo_Returns422()
    {
        using var server = CreateServer(new FakeAuditEntryStore(), GrantAuditRead());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(
            $"{AuditUrl}?from={Uri.EscapeDataString("2026-08-02T00:00:00Z")}&to={Uri.EscapeDataString("2026-08-01T00:00:00Z")}");

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("?from=not-a-date")]
    [InlineData("?to=2026-01-01")]
    [InlineData("?pageSize=not-a-number")]
    [InlineData("?pageSize=201")]
    public async global::System.Threading.Tasks.Task GetAudit_WhenQueryIsInvalid_Returns422ValidationProblem(
        string query)
    {
        using var server = CreateServer(new FakeAuditEntryStore(), GrantAuditRead());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(AuditUrl + query);

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetAudit_WithoutStore_Returns503()
    {
        using var server = CreateServer(auditStore: null, GrantAuditRead());
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(AuditUrl);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    private static FakePolicyStore GrantAuditRead() => new()
    {
        UserOrg = OrganizationId,
        Grants = [new PolicyGrantRow(HasDirectRoleMembership: true)],
    };

    private static FakePolicyStore DenyAll() => new()
    {
        UserOrg = OrganizationId,
    };

    private static TestServer CreateServer(FakeAuditEntryStore? auditStore, FakePolicyStore policyStore)
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
                services.AddSingleton<ISessionRepository>(new FakeSessionRepository());
                if (auditStore is not null)
                {
                    services.AddSingleton<IAuditEntryStore>(auditStore);
                }

                services.AddSingleton<IAuthorizationPolicyStore>(policyStore);
                services.AddSingleton<PermissionDecisionService>();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton(
                    new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyMaterial.PrivateKeyPath}"));
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
                app.UseEndpoints(endpoints => endpoints.MapAuditEndpoints());
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

    private sealed class FakeAuditEntryStore : IAuditEntryStore
    {
        private readonly IReadOnlyList<AuditEntryRecord> _entries;

        public FakeAuditEntryStore(params AuditEntryRecord[] entries)
        {
            _entries = entries;
        }

        public AuditQuery? LastQuery { get; private set; }

        public global::System.Threading.Tasks.Task AppendAsync(
            AuditEntryRecord entry,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task<AuditPage> ReadAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return global::System.Threading.Tasks.Task.FromResult(new AuditPage(_entries, null));
        }
    }

    private sealed class FakePolicyStore : IAuthorizationPolicyStore
    {
        public Guid? UserOrg { get; set; }

        public IReadOnlyList<PolicyGrantRow> Grants { get; set; } = [];

        public IReadOnlyList<PolicyDenyRow> Denies { get; set; } = [];

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(UserOrg);

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
            global::System.Threading.Tasks.Task.FromResult(Denies);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => null;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => null;

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            Array.Empty<UserSessionListItem>();

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

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason)
        {
        }

        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason) =>
            0;

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
}
