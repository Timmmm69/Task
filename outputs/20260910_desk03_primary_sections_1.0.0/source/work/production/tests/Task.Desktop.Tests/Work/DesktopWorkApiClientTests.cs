using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Task.Desktop.Security;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class DesktopWorkApiClientTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly Guid NotificationId = Guid.NewGuid();

    [Fact]
    public async System.Threading.Tasks.Task ReadScenariosUseCanonicalRoutesAndStrictlyMapPayloads()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{ItemId:D}}","version":1,"name":"Plan","itemType":"file_reference","description":null,"fileExtension":".docx","lifecycleState":"active"}],"hasMore":false}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{Guid.NewGuid():D}}","version":1,"displayName":"Anna Smith","firstName":"Anna","lastName":"Smith","notes":null,"status":"active","lifecycleState":"active"}],"hasMore":false}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{NotificationId:D}}","version":1,"notificationType":"task.due","title":"Deadline","body":"Today","severity":"warning","status":"delivered","sourceObjectId":"{{ItemId:D}}","notBefore":"2026-09-09T08:00:00Z"}],"hasMore":false}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"objectId":"{{ItemId:D}}","objectType":"catalog_item","title":"Plan","parentObjectId":null,"version":1,"updatedAt":"2026-09-09T08:00:00Z"}],"nextCursor":null,"tookMs":2}"""),
            Json(HttpStatusCode.OK, $$"""{"catalogItemId":"{{ItemId:D}}","status":"requires_client_check","physicalOperationPerformed":false,"location":{"id":"{{LocationId:D}}","version":1,"locationType":"local_path","rawPath":"C:\\Work\\plan.docx","canOpenOnDevice":true} }""")
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded>(await fixture.Client.GetCatalogAsync()).Value);
        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded>(await fixture.Client.GetContactsAsync()).Value);
        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded>(await fixture.Client.GetNotificationsAsync()).Value);
        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded>(await fixture.Client.SearchAsync("Plan")).Value);
        var location = Assert.IsType<DesktopWorkResult<DesktopFileLocation?>.Succeeded>(await fixture.Client.ResolveLocationAsync(ItemId)).Value;

        Assert.NotNull(location); Assert.Equal(@"C:\Work\plan.docx", location.RawPath);
        Assert.Equal(["/api/v1/catalog-items", "/api/v1/contacts", "/api/v1/notifications", "/api/v1/search", $"/api/v1/catalog-items/{ItemId:D}/resolve-location"], fixture.Requests.Select(r => r.Uri.AbsolutePath));
        Assert.All(fixture.Requests, request => Assert.Equal("AT_initial", request.Bearer));
    }

    [Fact]
    public async System.Threading.Tasks.Task WritesSendEtagKeysAndBoundedNotificationIds()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.Created, "{}"),
            Json(HttpStatusCode.OK, "{\"updatedCount\":1,\"latestVersion\":2}")
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.AddLocationAsync(ItemId, 3, @"C:\Work\plan.docx"));
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.MarkAllNotificationsReadAsync([NotificationId]));

        Assert.Equal("\"v3\"", fixture.Requests[0].IfMatch);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Requests[0].IdempotencyKey));
        Assert.Contains("local_path", fixture.Requests[0].Body);
        Assert.Contains(NotificationId.ToString("D"), fixture.Requests[1].Body);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Requests[1].IdempotencyKey));
    }

    [Fact]
    public async System.Threading.Tasks.Task MalformedAndForbiddenResponsesAreControlled()
    {
        var responses = new Queue<HttpResponseMessage>([Json(HttpStatusCode.OK, "{\"items\":[{\"id\":\"bad\"}]}"), Json(HttpStatusCode.Forbidden, "{}")]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.MalformedResponse>(await fixture.Client.GetCatalogAsync());
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopContact>>.Forbidden>(await fixture.Client.GetContactsAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task LifecycleAndSettingsUseCanonicalRoutesVersionsAndStrictPayloads()
    {
        var archivedId = Guid.NewGuid(); var trashedId = Guid.NewGuid();
        const string user = """{"version":1,"language":"ru-RU","timeFormat":"24h","firstDayOfWeek":1,"workdayStart":"09:00:00","workdayEnd":"18:00:00","weekendDays":[6,7],"defaultTaskDurationMinutes":60,"defaultReminderOffsetMinutes":15,"autostartEnabled":true,"allowLocalPaths":true,"confirmCatalogDelete":true,"missingFileBehavior":"show_actions"}""";
        const string preferences = """{"version":1,"enabled":true,"desktopEnabled":true,"soundEnabled":true,"defaultSnoozeMinutes":15,"quietHoursStart":null,"quietHoursEnd":null,"quietHoursTimeZone":null}""";
        const string organization = """{"version":1,"trashRetentionDays":30,"historyRetentionDays":1095,"changeFeedRetentionDays":90,"recurrenceHorizonDays":90,"recurrenceMinInstances":20,"defaultWorkdayStart":"09:00:00","defaultWorkdayEnd":"18:00:00","firstDayOfWeek":1,"maxRequestBytes":1048576}""";
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, $$"""{"items":[{"objectId":"{{archivedId:D}}","objectType":"project","title":"Plan","version":3,"lifecycleState":"archived","updatedAt":"2026-09-10T08:00:00Z","archivedAt":"2026-09-10T08:00:00Z"}]}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"objectId":"{{trashedId:D}}","objectType":"task","title":"Old task","version":4,"lifecycleState":"trashed","updatedAt":"2026-09-10T09:00:00Z","deletedAt":"2026-09-10T09:00:00Z","purgeAfter":"2026-10-10T09:00:00Z"}]}"""),
            Json(HttpStatusCode.OK, user), Json(HttpStatusCode.OK, preferences), Json(HttpStatusCode.OK, organization),
            Json(HttpStatusCode.OK, "{}"), Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, user.Replace("\"version\":1", "\"version\":2")),
            Json(HttpStatusCode.OK, preferences.Replace("\"version\":1", "\"version\":2")),
            Json(HttpStatusCode.OK, organization.Replace("\"version\":1", "\"version\":2"))
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>.Succeeded>(await fixture.Client.GetArchiveAsync()).Value);
        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>.Succeeded>(await fixture.Client.GetTrashAsync()).Value);
        var userSettings = Assert.IsType<DesktopWorkResult<DesktopUserSettings>.Succeeded>(await fixture.Client.GetUserSettingsAsync()).Value;
        var notificationSettings = Assert.IsType<DesktopWorkResult<DesktopNotificationPreferences>.Succeeded>(await fixture.Client.GetNotificationPreferencesAsync()).Value;
        var organizationSettings = Assert.IsType<DesktopWorkResult<DesktopOrganizationSettings>.Succeeded>(await fixture.Client.GetOrganizationSettingsAsync()).Value;
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.RestoreArchiveAsync(archivedId, 3));
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await fixture.Client.RestoreTrashAsync(trashedId, 4));
        Assert.IsType<DesktopWorkResult<DesktopUserSettings>.Succeeded>(await fixture.Client.UpdateUserSettingsAsync(userSettings));
        Assert.IsType<DesktopWorkResult<DesktopNotificationPreferences>.Succeeded>(await fixture.Client.UpdateNotificationPreferencesAsync(notificationSettings));
        Assert.IsType<DesktopWorkResult<DesktopOrganizationSettings>.Succeeded>(await fixture.Client.UpdateOrganizationSettingsAsync(organizationSettings));

        Assert.Equal(["/api/v1/archive", "/api/v1/trash", "/api/v1/settings/me", "/api/v1/notifications/preferences", "/api/v1/settings/organization"], fixture.Requests.Take(5).Select(r => r.Uri.AbsolutePath));
        Assert.Equal(["\"v3\"", "\"v4\"", "\"v1\"", "\"v1\"", "\"v1\""], fixture.Requests.Skip(5).Select(r => r.IfMatch));
        Assert.Null(fixture.Requests[5].IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Requests[6].IdempotencyKey));
        Assert.Equal(HttpMethod.Patch, fixture.Requests[7].Method);
        Assert.Equal(HttpMethod.Put, fixture.Requests[8].Method);
        Assert.Contains("trashRetentionDays", fixture.Requests[9].Body);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory; private readonly HttpClient _auth; private readonly HttpClient _http; private readonly SessionService _session;
        private Fixture(string directory, HttpClient auth, HttpClient http, SessionService session, DesktopWorkApiClient client, List<CapturedRequest> requests) { _directory = directory; _auth = auth; _http = http; _session = session; Client = client; Requests = requests; }
        public DesktopWorkApiClient Client { get; }
        public List<CapturedRequest> Requests { get; }
        public static async System.Threading.Tasks.Task<Fixture> CreateAsync(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TaskWorkClientTests", Guid.NewGuid().ToString("N"));
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
                requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, request.Headers.IfMatch.SingleOrDefault()?.ToString(), request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null, body));
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
