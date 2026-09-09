using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using Task.Desktop.Projects;
using Task.Desktop.Security;

namespace Task.Desktop.Tests.Projects;

public sealed class DesktopProjectsApiClientTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    [Fact]
    public async System.Threading.Tasks.Task ReadsUseCanonicalRoutesAndStrictPayloads()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, $$"""{"items":[{{ProjectJson()}}],"nextCursor":null,"hasMore":false}"""),
            Json(HttpStatusCode.OK, $$"""[{"id":"{{RoleId:D}}","code":"project_editor","name":"Редактор","isSystem":false,"status":"active","permissionCodes":["project.read"]}]"""),
            WithEtag(Json(HttpStatusCode.OK, $$"""{"items":[{"projectId":"{{ProjectId:D}}","userAccountId":"{{UserId:D}}","projectRoleId":"{{RoleId:D}}","status":"active","version":1,"joinedAt":"2026-09-01T00:00:00Z","removedAt":null}]}"""), "\"v3\"")
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        var page = Assert.IsType<DesktopProjectResult<DesktopProjectPage>.Succeeded>(await fixture.Client.GetProjectsAsync());
        var roles = Assert.IsType<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded>(await fixture.Client.GetRolesAsync());
        var members = Assert.IsType<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded>(await fixture.Client.GetMembersAsync(ProjectId));

        Assert.Single(page.Value.Items); Assert.Equal("Редактор", Assert.Single(roles.Value).Name); Assert.Equal(3, members.EntityVersion);
        Assert.Equal("/api/v1/projects", fixture.Requests[0].Uri.AbsolutePath);
        Assert.Equal("/api/v1/project-roles", fixture.Requests[1].Uri.AbsolutePath);
        Assert.Equal($"/api/v1/projects/{ProjectId:D}/members", fixture.Requests[2].Uri.AbsolutePath);
        Assert.All(fixture.Requests, request => Assert.Equal("AT_initial", request.Bearer));
    }

    [Fact]
    public async System.Threading.Tasks.Task WritesSendStrongEtagAndIdempotencyKey()
    {
        var memberId = Guid.NewGuid();
        var responses = new Queue<HttpResponseMessage>([
            WithEtag(Json(HttpStatusCode.Created, ProjectJson()), "\"v1\""),
            WithEtag(Json(HttpStatusCode.Created, $$"""{"projectId":"{{ProjectId:D}}","userAccountId":"{{memberId:D}}","projectRoleId":"{{RoleId:D}}","status":"active","version":1,"joinedAt":"2026-09-01T00:00:00Z","removedAt":null}"""), "\"v2\"")
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var draft = new DesktopProjectDraft("Проект Альфа", null, UserId, null, DesktopProjectStatus.Active, null, null);

        Assert.IsType<DesktopProjectResult<DesktopProjectDto>.Succeeded>(await fixture.Client.CreateProjectAsync(draft, "create-project-001"));
        var member = Assert.IsType<DesktopProjectResult<DesktopProjectMemberDto>.Succeeded>(await fixture.Client.AddMemberAsync(ProjectId, 1, memberId, RoleId, "add-member-001"));

        Assert.Equal("create-project-001", fixture.Requests[0].IdempotencyKey);
        Assert.Equal("\"v1\"", fixture.Requests[1].IfMatch);
        Assert.Equal("add-member-001", fixture.Requests[1].IdempotencyKey);
        Assert.Contains($"\"userAccountId\":\"{memberId:D}\"", fixture.Requests[1].Body);
        Assert.Equal(2, member.EntityVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task MalformedAndConflictResponsesAreControlled()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, "{\"items\":[{\"id\":\"not-a-guid\"}],\"hasMore\":false}"),
            Json(HttpStatusCode.PreconditionFailed, "{\"title\":\"Conflict\"}")
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        Assert.IsType<DesktopProjectResult<DesktopProjectPage>.MalformedResponse>(await fixture.Client.GetProjectsAsync());
        Assert.IsType<DesktopProjectResult<DesktopProjectDto>.VersionConflict>(await fixture.Client.UpdateProjectAsync(
            ProjectId, 1, new("Проект", null, UserId, null, DesktopProjectStatus.Active, null, null)));
    }

    private static string ProjectJson() => $$"""{"id":"{{ProjectId:D}}","organizationId":"{{OrganizationId:D}}","version":1,"name":"Проект Альфа","description":null,"ownerUserId":"{{UserId:D}}","managerUserId":null,"status":"active","startDate":null,"plannedEndDate":null,"actualEndAt":null,"defaultTimeZone":"Europe/Minsk","colorCode":"#336699","lifecycleState":"active"}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage WithEtag(HttpResponseMessage response, string etag) { response.Headers.TryAddWithoutValidation("ETag", etag); return response; }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory; private readonly HttpClient _auth; private readonly HttpClient _http; private readonly SessionService _session;
        private Fixture(string directory, HttpClient auth, HttpClient http, SessionService session, DesktopProjectsApiClient client, List<CapturedRequest> requests)
        { _directory = directory; _auth = auth; _http = http; _session = session; Client = client; Requests = requests; }
        public DesktopProjectsApiClient Client { get; }
        public List<CapturedRequest> Requests { get; }
        public static async System.Threading.Tasks.Task<Fixture> CreateAsync(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TaskProjectsClientTests", Guid.NewGuid().ToString("N"));
            var auth = new HttpClient(new Handler((request, _) => System.Threading.Tasks.Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/auth/login" => Json(HttpStatusCode.OK, $$"""{"accessToken":"AT_initial","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"RT_initial","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(1):O}}","sessionId":"{{SessionId:D}}"}"""),
                "/api/v1/auth/session" => Json(HttpStatusCode.OK, $$"""{"userId":"{{UserId:D}}","sessionId":"{{SessionId:D}}","organizationId":"{{OrganizationId:D}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":false}"""),
                _ => Json(HttpStatusCode.NotFound, "{}")
            })));
            var session = new SessionService(new DesktopAuthApiClient(auth, "https://task.example.test"), new DesktopCredentialVault(directory), "test", ClientPlatform.Windows, "1.0.0");
            Assert.IsType<LoginResult.Succeeded>(await session.LoginAsync("user@example.test", "password", Guid.NewGuid().ToString("D"), CancellationToken.None));
            var requests = new List<CapturedRequest>();
            var http = new HttpClient(new Handler(async (request, token) =>
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(token);
                requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter,
                    request.Headers.IfMatch.SingleOrDefault()?.ToString(), request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null, body));
                return await responder(request, token);
            }));
            return new(directory, auth, http, session, new(http, new Uri("https://task.example.test"), session), requests);
        }
        public ValueTask DisposeAsync() { _session.Dispose(); _http.Dispose(); _auth.Dispose(); try { Directory.Delete(_directory, true); } catch { } return ValueTask.CompletedTask; }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken); }
    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Bearer, string? IfMatch, string? IdempotencyKey, string Body);
}
