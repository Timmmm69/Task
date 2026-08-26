using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Security;
using Task.Api.Tasks;
using Task.Application;
using Task.Application.Security;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

#pragma warning disable ASPDEPR004 // TestServer currently requires the legacy IWebHostBuilder adapter.
public sealed partial class TaskEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string TasksUrl = "/api/v1/tasks";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ForeignOrganizationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TaskId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AuthorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly TaskReadProjection Projection = new(
        TaskId,
        OrganizationId,
        Version: 7,
        CreatedAtUtc: DateTimeOffset.Parse("2026-08-20T08:00:00Z"),
        UpdatedAtUtc: DateTimeOffset.Parse("2026-08-23T09:30:00Z"),
        Title: "Prepare quarterly review",
        AuthorUserId,
        TaskWorkStatus.InProgress,
        TaskPriority.High,
        StartAtUtc: DateTimeOffset.Parse("2026-08-24T07:00:00Z"),
        DeadlineAtUtc: DateTimeOffset.Parse("2026-08-25T15:00:00Z"));

    private static readonly Lazy<TestKeyMaterial> KeyMaterial = new(CreateKeyMaterial);

    [Theory]
    [InlineData(TasksUrl)]
    [InlineData(TasksUrl + "/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async global::System.Threading.Tasks.Task GetTaskEndpoints_WithoutToken_Return401(string url)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = server.CreateClient();

        var response = await client.GetAsync(url);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Theory]
    [InlineData(SessionRequestState.SessionRevoked, "SESSION_REVOKED")]
    [InlineData(SessionRequestState.SessionExpired, "SESSION_EXPIRED")]
    public async global::System.Threading.Tasks.Task GetTasks_WithTerminalSession_Returns401(
        SessionRequestState sessionState,
        string expectedCode)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            sessionState: sessionState);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, expectedCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithoutTaskManagePermission_Returns403()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), grantTaskRead: false);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithPermission_ReturnsCanonicalPageAndSecurityBinding()
    {
        var store = new FakeTaskReadStore(Projection)
        {
            Page = new TaskReadPage([Projection], "next-opaque-cursor"),
        };
        using var server = CreateServer(store);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(store.LastPageRequest);
        Assert.Equal(OrganizationId, store.LastPageRequest!.OrganizationId);
        Assert.Equal(UserId, store.LastPageRequest.UserAccountId);
        Assert.Equal(1, store.LastPageRequest.AuthorizationScopeVersion);
        Assert.Null(store.LastPageRequest.Cursor);

        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;
        Assert.Equal("next-opaque-cursor", root.GetProperty("nextCursor").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("total").ValueKind);
        AssertTaskContract(Assert.Single(root.GetProperty("items").EnumerateArray()));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithCursor_ForwardsCursorAndReturnsEmptyPage()
    {
        var store = new FakeTaskReadStore(Projection)
        {
            Page = new TaskReadPage([], null),
        };
        using var server = CreateServer(store);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + "?cursor=opaque-cursor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("opaque-cursor", store.LastPageRequest?.Cursor);
        using var document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("?filter=status%3Dnew")]
    [InlineData("?sort=updatedAt")]
    [InlineData("?page=2")]
    [InlineData("?page=0")]
    [InlineData("?page=not-a-number")]
    public async global::System.Threading.Tasks.Task GetTasks_WithUnsupportedQuery_Returns400ValidationProblem(
        string query)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + query);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithEmptyUnsupportedQueryValues_NormalizesThemAsAbsent()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + "?filter=&sort=%20&page=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithInvalidCursor_Returns400SearchCursorInvalid()
    {
        var store = new FakeTaskReadStore(Projection) { RejectCursor = true };
        using var server = CreateServer(store);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + "?cursor=corrupt");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "SEARCH_CURSOR_INVALID");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_WithoutReadStore_Returns503()
    {
        using var server = CreateServer(readStore: null);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTask_WithPermission_ReturnsCanonicalTaskAndEtag()
    {
        var store = new FakeTaskReadStore(Projection);
        using var server = CreateServer(store);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + "/" + TaskId.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v7\"", response.Headers.ETag?.Tag);
        Assert.Equal((OrganizationId, TaskId), store.LastDetailRequest);
        using var document = await ReadJsonAsync(response);
        AssertTaskContract(document.RootElement);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTask_FromForeignOrganization_ReturnsSame404AsAbsentTask()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), tokenOrganizationId: ForeignOrganizationId);
        using var client = await CreateAuthenticatedClientAsync(server, ForeignOrganizationId);

        var response = await client.GetAsync(TasksUrl + "/" + TaskId.ToString("D"));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
    }

    [Theory]
    [InlineData("cccccccc-cccc-cccc-cccc-cccccccccccc")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async global::System.Threading.Tasks.Task GetTask_WhenAbsentOrMalformed_ReturnsObjectNotVisible(
        string taskId)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await client.GetAsync(TasksUrl + "/" + taskId);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTask_EchoesValidCorrelationId()
    {
        var correlationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId.ToString("D"));

        var response = await client.GetAsync(TasksUrl + "/" + TaskId.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId.ToString("D"), response.Headers.GetValues("X-Correlation-ID").Single());
    }

    private static TestServer CreateServer(
        FakeTaskReadStore? readStore,
        bool grantTaskRead = true,
        SessionRequestState sessionState = SessionRequestState.Active,
        Guid? tokenOrganizationId = null,
        ITaskWriteCommandExecutor? writeExecutor = null,
        ITaskAggregateStore? aggregateStore = null)
    {
        var keyMaterial = KeyMaterial.Value;
        var organizationId = tokenOrganizationId ?? OrganizationId;
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
                services.AddSingleton<ISessionRepository>(new FakeSessionRepository(sessionState));
                if (readStore is not null)
                {
                    services.AddSingleton<ITaskReadStore>(readStore);
                }

                if (writeExecutor is not null)
                {
                    services.AddSingleton(writeExecutor);
                    services.AddSingleton<TaskCreateCommandService>();
                    if (aggregateStore is not null)
                    {
                        services.AddSingleton(aggregateStore);
                        services.AddSingleton<TaskUpdateCommandService>();
                    }
                }

                services.AddSingleton<IAuthorizationPolicyStore>(new FakePolicyStore(
                    organizationId,
                    grantTaskRead));
                services.AddSingleton<PermissionDecisionService>();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton(
                    new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyMaterial.PrivateKeyPath}"));
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    var supplied = context.Request.Headers["X-Correlation-ID"].ToString();
                    var correlationId = Guid.TryParseExact(supplied, "D", out var parsed)
                        ? parsed
                        : Guid.NewGuid();
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId.ToString("D");
                    context.Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapTaskEndpoints());
            }));
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        TestServer server,
        Guid organizationId)
    {
        var client = server.CreateClient();
        var issuer = server.Host.Services.GetRequiredService<JwtAccessTokenIssuer>();
        var token = await issuer.IssueAsync(
            new JwtIssuanceRequest(UserId, SessionId, organizationId, 1, 1),
            CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static void AssertTaskContract(JsonElement task)
    {
        Assert.Equal(TaskId.ToString("D"), task.GetProperty("id").GetString());
        Assert.Equal(OrganizationId.ToString("D"), task.GetProperty("organizationId").GetString());
        Assert.Equal(7, task.GetProperty("version").GetInt64());
        Assert.EndsWith("Z", task.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", task.GetProperty("updatedAt").GetString());
        Assert.Equal("Prepare quarterly review", task.GetProperty("title").GetString());
        Assert.Equal(AuthorUserId.ToString("D"), task.GetProperty("authorUserId").GetString());
        Assert.Equal("in_progress", task.GetProperty("status").GetString());
        Assert.Equal("high", task.GetProperty("priority").GetString());
        Assert.EndsWith("Z", task.GetProperty("startAtUtc").GetString());
        Assert.EndsWith("Z", task.GetProperty("deadlineAt").GetString());
        Assert.Empty(task.GetProperty("assigneeIds").EnumerateArray());
        Assert.Empty(task.GetProperty("watcherIds").EnumerateArray());

        foreach (var propertyName in new[]
                 {
                     "projectId", "parentTaskId", "description", "requesterUserId",
                     "primaryCounterpartyObjectId", "scheduledDate", "startTimeLocal",
                     "scheduleTimeZone", "plannedDurationMinutes", "recurrenceSeriesId",
                 })
        {
            Assert.Equal(JsonValueKind.Null, task.GetProperty(propertyName).ValueKind);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<JsonDocument> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = await ReadJsonAsync(response);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            response.Headers.GetValues("X-Correlation-ID").Single(),
            document.RootElement.GetProperty("correlationId").GetString());
        return document;
    }

    private static TestKeyMaterial CreateKeyMaterial()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"task-endpoint-tests-{Guid.NewGuid():N}");
        var signingDirectory = Path.Combine(baseDirectory, "signing");
        var verificationDirectory = Path.Combine(baseDirectory, "verification");
        Directory.CreateDirectory(signingDirectory);
        Directory.CreateDirectory(verificationDirectory);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPath = Path.Combine(signingDirectory, "task-signing.pem");
        File.WriteAllText(privateKeyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            Path.Combine(verificationDirectory, "task-signing.pem"),
            ecdsa.ExportSubjectPublicKeyInfoPem());

        return new TestKeyMaterial(privateKeyPath, verificationDirectory);
    }

    private sealed record TestKeyMaterial(string PrivateKeyPath, string VerificationKeysDirectory);

    private sealed class FakeTaskReadStore : ITaskReadStore
    {
        private readonly List<TaskReadProjection> _tasks;

        public FakeTaskReadStore(TaskReadProjection task)
        {
            _tasks = [task];
            Page = new TaskReadPage([task], null);
        }

        public void Add(TaskReadProjection task)
        {
            _tasks.RemoveAll(existing => existing.OrganizationId == task.OrganizationId && existing.Id == task.Id);
            _tasks.Add(task);
        }

        public TaskReadPage Page { get; set; }

        public bool RejectCursor { get; set; }

        public TaskReadPageRequest? LastPageRequest { get; private set; }

        public (Guid OrganizationId, Guid TaskId)? LastDetailRequest { get; private set; }

        public global::System.Threading.Tasks.Task<TaskReadProjection?> GetByIdAsync(
            Guid organizationId,
            Guid taskId,
            CancellationToken cancellationToken = default)
        {
            LastDetailRequest = (organizationId, taskId);
            var visible = _tasks.Find(task => task.OrganizationId == organizationId && task.Id == taskId);
            return global::System.Threading.Tasks.Task.FromResult(visible);
        }

        public global::System.Threading.Tasks.Task<TaskReadPage> GetPageAsync(
            TaskReadPageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPageRequest = request;
            if (RejectCursor)
            {
                throw new TaskReadCursorException();
            }

            return global::System.Threading.Tasks.Task.FromResult(Page);
        }
    }

    private sealed class FakePolicyStore : IAuthorizationPolicyStore
    {
        private readonly bool _grantTaskRead;

        public FakePolicyStore(Guid userOrganizationId, bool grantTaskRead)
        {
            UserOrganizationId = userOrganizationId;
            _grantTaskRead = grantTaskRead;
        }

        private Guid UserOrganizationId { get; }

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<Guid?>(UserOrganizationId);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PolicyGrantRow> grants =
                _grantTaskRead && permissionCode == TaskPermissionAuthorization.TaskReadBackingPermissionCode
                    ? [new PolicyGrantRow(HasDirectRoleMembership: true)]
                    : [];
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
        private readonly SessionRequestState _requestState;

        public FakeSessionRepository(SessionRequestState requestState)
        {
            _requestState = requestState;
        }

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => null;
        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => null;
        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) => [];
        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) => null;
        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) => _requestState;
        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) { }
        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) => true;
        public void TouchSession(Guid organizationId, Guid sessionId) { }
        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) { }
        public int RevokeAllUserSessions(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            string? reason) => 0;
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
#pragma warning restore ASPDEPR004
