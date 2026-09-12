using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Calendar;
using Task.Api.Security;
using Task.Application.Calendar;
using Task.Application.Security;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

#pragma warning disable ASPDEPR004 // TestServer currently requires the legacy IWebHostBuilder adapter.
public sealed class RecurrenceEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string RecurrenceRoute = "/api/v1/recurrence-series";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SeriesId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly Lazy<KeyMaterial> Keys = new(CreateKeyMaterial);

    [Fact]
    public async global::System.Threading.Tasks.Task GetMissingSeries_StoreThrowsKeyNotFound_Returns404Not422()
    {
        var store = new FakeRecurrenceStore(null, throwOnGet: true);
        using var server = CreateServer(store);
        using var client = await CreateClientAsync(server);

        var response = await client.GetAsync($"{RecurrenceRoute}/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemAsync(response, "OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Preview_WithOversizedBody_Returns413RequestTooLarge()
    {
        using var server = CreateServer(new FakeRecurrenceStore(null));
        using var client = await CreateClientAsync(server);
        using var request = new HttpRequestMessage(HttpMethod.Post, RecurrenceRoute + "/preview")
        {
            Content = new StringContent(OversizedBody(), Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertProblemAsync(response, "REQUEST_TOO_LARGE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Patch_WithoutIdempotencyKey_DerivesStableKeyAcrossRetries()
    {
        var store = new FakeRecurrenceStore(CreatePausedSeries());
        using var server = CreateServer(store);
        using var client = await CreateClientAsync(server);

        var first = await SendPatchAsync(client, """{"interval":2}""");
        var retry = await SendPatchAsync(client, """{"interval":2}""");
        var different = await SendPatchAsync(client, """{"interval":3}""");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, different.StatusCode);
        Assert.Equal(3, store.PatchKeys.Count);
        Assert.Equal(store.PatchKeys[0], store.PatchKeys[1]);
        Assert.NotEqual(store.PatchKeys[0], store.PatchKeys[2]);
        Assert.Equal(64, store.PatchKeys[0].Length);
        Assert.All(store.PatchKeys[0], character => Assert.True(char.IsAsciiHexDigit(character)));
    }

    private static async global::System.Threading.Tasks.Task<HttpResponseMessage> SendPatchAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), RecurrenceRoute + "/" + SeriesId.ToString("D"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        return await client.SendAsync(request);
    }

    private static RecurrenceRecord CreatePausedSeries() => new(
        SeriesId,
        OrganizationId,
        Version: 1,
        DateTimeOffset.Parse("2026-08-20T08:00:00Z"),
        DateTimeOffset.Parse("2026-08-20T08:00:00Z"),
        UserId,
        new RecurrenceDefinition
        {
            Status = "paused",
            Frequency = "daily",
            Interval = 1,
            OccurrenceStartDate = new DateOnly(2026, 8, 20),
            TimeZone = "UTC",
            Template = new RecurrenceTemplateData { Title = "Daily", AuthorUserId = UserId },
        });

    private static string OversizedBody() =>
        "{\"padding\":\"" + new string('x', 128 * 1024 + 1) + "\"}";

    private static async global::System.Threading.Tasks.Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, string expectedCode)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        return document;
    }

    private static TestServer CreateServer(FakeRecurrenceStore store, bool grantRead = true, bool grantManage = true)
    {
        var keys = Keys.Value;
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
                        SigningKeyReference = $"file:{keys.PrivateKeyPath}",
                        PepperReference = "file:/run/secrets/task-pepper",
                        VerificationKeysDirectory = $"file:{keys.VerificationDirectory}",
                    }));
                services.AddSingleton<ISessionRepository>(new FakeSessionRepository());
                services.AddSingleton<IAuthorizationPolicyStore>(new FakePolicyStore(grantRead, grantManage));
                services.AddSingleton<PermissionDecisionService>();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton(new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keys.PrivateKeyPath}"));
                services.AddSingleton<IRecurrenceStore>(store);
                services.AddSingleton<RecurrenceService>();
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    var supplied = context.Request.Headers["X-Correlation-ID"].ToString();
                    var correlationId = Guid.TryParseExact(supplied, "D", out var parsed) ? parsed : Guid.NewGuid();
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId.ToString("D");
                    context.Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapRecurrenceEndpoints());
            }));
    }

    private static async global::System.Threading.Tasks.Task<HttpClient> CreateClientAsync(TestServer server)
    {
        var client = server.CreateClient();
        var issuer = server.Host.Services.GetRequiredService<JwtAccessTokenIssuer>();
        var token = await issuer.IssueAsync(
            new JwtIssuanceRequest(UserId, SessionId, OrganizationId, 1, 1),
            CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class FakeRecurrenceStore : IRecurrenceStore
    {
        private readonly RecurrenceRecord? _series;
        private readonly bool _throwOnGet;

        public FakeRecurrenceStore(RecurrenceRecord? series, bool throwOnGet = false)
        {
            _series = series;
            _throwOnGet = throwOnGet;
        }

        public List<string> PatchKeys { get; } = [];

        public IReadOnlyList<RecurrenceRecord> List(Guid organizationId, Guid? actorId = null) =>
            _series is { } series && series.OrganizationId == organizationId ? [series] : [];

        public RecurrenceRecord? Get(Guid organizationId, Guid id)
        {
            if (_throwOnGet)
            {
                throw new KeyNotFoundException();
            }

            return _series is { } series && series.OrganizationId == organizationId && series.Id == id ? series : null;
        }

        public IReadOnlyList<RecurrenceOccurrenceDetails> GetOccurrences(Guid organizationId, Guid id, Guid? actorId = null) => [];

        public RecurrenceReply Execute(
            Guid organizationId,
            Guid actorId,
            Guid id,
            string operation,
            string idempotencyKey,
            string requestHash,
            Func<RecurrenceRecord?, IRecurrenceTransaction, RecurrenceReply> action)
        {
            if (operation == "patch")
            {
                PatchKeys.Add(idempotencyKey);
            }

            return action(Get(organizationId, id), new FakeTransaction(Get(organizationId, id)));
        }
    }

    private sealed class FakeTransaction(RecurrenceRecord? series) : IRecurrenceTransaction
    {
        public RecurrenceRecord? Series { get; private set; } = series;

        IReadOnlyList<RecurrenceOccurrenceRecord> IRecurrenceTransaction.Occurrences => [];

        public TaskAggregate? GetTask(Guid id) => null;

        public void SaveTask(TaskAggregate task, int? expectedVersion) { }

        public void SaveOccurrence(RecurrenceOccurrenceRecord occurrence) { }

        public void SaveSeries(RecurrenceRecord updated) => Series = updated;
    }

    private sealed class FakePolicyStore(bool grantRead, bool grantManage) : IAuthorizationPolicyStore
    {
        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<Guid?>(OrganizationId);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            var allowed = (grantRead && permissionCode == "recurrence.read")
                || (grantManage && permissionCode == "recurrence.manage");
            IReadOnlyList<PolicyGrantRow> grants = allowed ? [new PolicyGrantRow(HasDirectRoleMembership: true)] : [];
            return global::System.Threading.Tasks.Task.FromResult(grants);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyDenyRow>>([]);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long credentialVersion,
            long authorizationScopeVersion) => SessionRequestState.Active;
        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => null;
        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => null;
        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) => [];
        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) => null;
        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) { }
        public bool RotateRefreshToken(Guid organizationId, Guid sessionId, string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) => true;
        public void TouchSession(Guid organizationId, Guid sessionId) { }
        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) { }
        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason) => 0;
        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(Guid organizationId, Guid userId,
            Guid? exceptSessionId, CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);
        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset olderThanUtc, int maxCount,
            CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(0);
        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(DateTimeOffset olderThanUtc, int maxCount,
            CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(0);
    }

    private static KeyMaterial CreateKeyMaterial()
    {
        var root = Path.Combine(Path.GetTempPath(), $"task-recurrence-tests-{Guid.NewGuid():N}");
        var signing = Path.Combine(root, "signing");
        var verification = Path.Combine(root, "verification");
        Directory.CreateDirectory(signing);
        Directory.CreateDirectory(verification);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePath = Path.Combine(signing, "task-signing.pem");
        File.WriteAllText(privatePath, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(verification, "task-signing.pem"), ecdsa.ExportSubjectPublicKeyInfoPem());
        return new(privatePath, verification);
    }

    private sealed record KeyMaterial(string PrivateKeyPath, string VerificationDirectory);
}
#pragma warning restore ASPDEPR004
