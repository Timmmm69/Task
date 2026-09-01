using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using Task.Desktop.Calendar;
using Task.Desktop.Security;

namespace Task.Desktop.Tests.Calendar;

public sealed class DesktopCalendarApiClientTests
{
    private static readonly Guid SessionId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2936");
    private static readonly Guid OrganizationId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2937");
    private static readonly Guid UserId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2938");
    private static readonly Guid EventId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2939");

    [Fact]
    public async global::System.Threading.Tasks.Task ReadEndpoints_UseCanonicalUrisAndStrictlyMapPayloads()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, $$"""{"items":[{"objectId":"{{EventId:D}}","itemType":"calendar_event","title":"Планёрка","localDate":"2026-09-01","startAtUtc":"2026-09-01T07:00:00Z","endAtUtc":"2026-09-01T08:00:00Z","isAllDay":false,"projectId":null,"status":"scheduled","priority":null}],"nextCursor":null,"rangeStart":"2026-08-30T21:00:00Z","rangeEnd":"2026-09-06T21:00:00Z"}"""),
            WithEtag(Json(HttpStatusCode.OK, EventJson()), "\"v3\""),
            Json(HttpStatusCode.OK, $$"""[{"leftObjectId":"{{EventId:D}}","rightObjectId":"{{Guid.NewGuid():D}}","overlapStart":"2026-09-01T07:30:00Z","overlapEnd":"2026-09-01T08:00:00Z","severity":"blocking"}]"""),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        var page = Assert.IsType<DesktopCalendarResult<DesktopSchedulePage>.Succeeded>(await fixture.Client.GetScheduleAsync(
            DateTimeOffset.Parse("2026-08-30T21:00:00Z"), DateTimeOffset.Parse("2026-09-06T21:00:00Z"), "Europe/Minsk", CancellationToken.None));
        var details = Assert.IsType<DesktopCalendarResult<DesktopCalendarEvent>.Succeeded>(await fixture.Client.GetEventAsync(EventId, CancellationToken.None));
        var conflicts = Assert.IsType<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded>(await fixture.Client.GetConflictsAsync(
            DateTimeOffset.Parse("2026-08-30T21:00:00Z"), DateTimeOffset.Parse("2026-09-06T21:00:00Z"), CancellationToken.None));

        Assert.Single(page.Value.Items); Assert.Equal(3, details.Value.Version); Assert.Single(conflicts.Value);
        Assert.Contains("timezone=Europe%2FMinsk", fixture.Requests[0].Uri.Query);
        Assert.EndsWith($"/api/v1/calendar-events/{EventId:D}", fixture.Requests[1].Uri.AbsolutePath);
        Assert.EndsWith("/api/v1/calendar/conflicts", fixture.Requests[2].Uri.AbsolutePath);
        Assert.All(fixture.Requests, request => Assert.Equal("AT_initial", request.Bearer));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Writes_SendIdempotencyAndStrongIfMatch()
    {
        var responses = new Queue<HttpResponseMessage>([
            WithEtag(Json(HttpStatusCode.Created, EventJson()), "\"v3\""),
            WithEtag(Json(HttpStatusCode.OK, EventJson(4)), "\"v4\""),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var command = new DesktopCalendarEventCommand(null, "Планёрка", "Описание", new DateOnly(2026, 9, 1), false,
            DateTimeOffset.Parse("2026-09-01T07:00:00Z"), DateTimeOffset.Parse("2026-09-01T08:00:00Z"), "Europe/Minsk");

        Assert.IsType<DesktopCalendarResult<DesktopCalendarEvent>.Succeeded>(await fixture.Client.CreateEventAsync(command, CancellationToken.None));
        Assert.IsType<DesktopCalendarResult<DesktopCalendarEvent>.Succeeded>(await fixture.Client.UpdateEventAsync(EventId, 3, command, CancellationToken.None));

        Assert.Equal(HttpMethod.Post, fixture.Requests[0].Method);
        Assert.NotNull(fixture.Requests[0].IdempotencyKey);
        Assert.Equal(HttpMethod.Patch, fixture.Requests[1].Method);
        Assert.Equal("\"v3\"", fixture.Requests[1].IfMatch);
        Assert.Contains("\"startAtUtc\":\"2026-09-01T07:00:00Z\"", fixture.Requests[1].Body);
        Assert.DoesNotContain("AT_initial", fixture.Requests[1].Body);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ProtocolAndHttpFailures_AreControlled()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, "{\"items\":[],\"nextCursor\":\"unsupported\",\"rangeStart\":\"2026-09-01T00:00:00Z\",\"rangeEnd\":\"2026-09-08T00:00:00Z\"}"),
            Json(HttpStatusCode.Forbidden, "{\"title\":\"Forbidden\"}"),
            Json(HttpStatusCode.PreconditionFailed, "{\"title\":\"Conflict\"}"),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var from = DateTimeOffset.Parse("2026-09-01T00:00:00Z"); var to = from.AddDays(7);

        Assert.IsType<DesktopCalendarResult<DesktopSchedulePage>.MalformedResponse>(await fixture.Client.GetScheduleAsync(from, to, "UTC", CancellationToken.None));
        Assert.IsType<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Forbidden>(await fixture.Client.GetConflictsAsync(from, to, CancellationToken.None));
        Assert.IsType<DesktopCalendarResult<DesktopCalendarEvent>.VersionConflict>(await fixture.Client.UpdateEventAsync(EventId, 3,
            new(null, "Планёрка", null, new DateOnly(2026, 9, 1), true, null, null, "UTC"), CancellationToken.None));
    }

    private static string EventJson(int version = 3) => $$"""{"id":"{{EventId:D}}","organizationId":"{{OrganizationId:D}}","version":{{version}},"createdAt":"2026-08-01T00:00:00Z","updatedAt":"2026-08-02T00:00:00Z","projectId":null,"title":"Планёрка","description":"Описание","eventDate":"2026-09-01","isAllDay":false,"startAtUtc":"2026-09-01T07:00:00Z","endAtUtc":"2026-09-01T08:00:00Z","timeZone":"Europe/Minsk","status":"scheduled","userAttendees":[],"contactAttendees":[]}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage WithEtag(HttpResponseMessage response, string etag) { response.Headers.TryAddWithoutValidation("ETag", etag); return response; }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory; private readonly HttpClient _auth; private readonly HttpClient _calendar; private readonly SessionService _session;
        private Fixture(string directory, HttpClient auth, HttpClient calendar, SessionService session, DesktopCalendarApiClient client, List<CapturedRequest> requests)
        { _directory = directory; _auth = auth; _calendar = calendar; _session = session; Client = client; Requests = requests; }
        public DesktopCalendarApiClient Client { get; }
        public List<CapturedRequest> Requests { get; }
        public static async global::System.Threading.Tasks.Task<Fixture> CreateAsync(Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> responder)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TaskCalendarClientTests", Guid.NewGuid().ToString("N"));
            var auth = new HttpClient(new Handler((request, _) => global::System.Threading.Tasks.Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/auth/login" => Json(HttpStatusCode.OK, $$"""{"accessToken":"AT_initial","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"RT_initial","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(1):O}}","sessionId":"{{SessionId:D}}"}"""),
                "/api/v1/auth/session" => Json(HttpStatusCode.OK, $$"""{"userId":"{{UserId:D}}","sessionId":"{{SessionId:D}}","organizationId":"{{OrganizationId:D}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":false}"""),
                _ => Json(HttpStatusCode.NotFound, "{}"),
            })));
            var session = new SessionService(new DesktopAuthApiClient(auth, "https://task.example.test"), new DesktopCredentialVault(directory), "test", ClientPlatform.Windows, "1.0.0");
            Assert.IsType<LoginResult.Succeeded>(await session.LoginAsync("user@example.test", "password", Guid.NewGuid().ToString("D"), CancellationToken.None));
            var requests = new List<CapturedRequest>();
            var calendar = new HttpClient(new Handler(async (request, token) =>
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(token);
                requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter,
                    request.Headers.IfMatch.SingleOrDefault()?.ToString(), request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null, body));
                return await responder(request, token);
            }));
            return new(directory, auth, calendar, session, new(calendar, new Uri("https://task.example.test"), session), requests);
        }
        public ValueTask DisposeAsync() { _session.Dispose(); _calendar.Dispose(); _auth.Dispose(); try { Directory.Delete(_directory, true); } catch { } return ValueTask.CompletedTask; }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken); }
    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Bearer, string? IfMatch, string? IdempotencyKey, string Body);
}
