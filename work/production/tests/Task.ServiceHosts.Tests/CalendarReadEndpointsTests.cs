using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
using Task.Domain.Calendar;

namespace Task.ServiceHosts.Tests;

#pragma warning disable ASPDEPR004

public sealed class CalendarScheduleEndpointsTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task GetSchedule_ReturnsCanonicalBoundedPageAndForwardsTenantFilters()
    {
        var eventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var taskId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var userId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var projectId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var store = new CalendarEndpointFixture.FakeScheduleStore([
            new(eventId, ScheduleItemType.CalendarEvent, "Event", new DateOnly(2026, 8, 20), false,
                DateTimeOffset.Parse("2026-08-20T09:00:00Z"), DateTimeOffset.Parse("2026-08-20T10:00:00Z"),
                "UTC", projectId, "scheduled", null),
            new(taskId, ScheduleItemType.Task, "Task", null, false,
                DateTimeOffset.Parse("2026-08-20T08:00:00Z"), null, null, null, "new", ScheduleItemPriority.High),
        ]);
        using var server = CalendarEndpointFixture.CreateServer(store);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(
            $"/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z" +
            $"&users={userId:D}&projects={projectId:D}&status=scheduled&timezone=UTC");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CalendarEndpointFixture.OrganizationId, store.LastCall?.OrganizationId);
        Assert.Equal(userId, Assert.Single(store.LastCall!.Users!));
        Assert.Equal(projectId, Assert.Single(store.LastCall.Projects!));
        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.Equal("2026-08-20T00:00:00Z", root.GetProperty("rangeStart").GetString());
        Assert.Equal("2026-08-21T00:00:00Z", root.GetProperty("rangeEnd").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nextCursor").ValueKind);
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("task", items[0].GetProperty("itemType").GetString());
        Assert.Equal("high", items[0].GetProperty("priority").GetString());
        Assert.Equal("calendar_event", items[1].GetProperty("itemType").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("priority").ValueKind);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSchedule_LimitsResponseToContractMaximum()
    {
        var rows = Enumerable.Range(1, 501).Select(index => new ScheduleItemRow(
            Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
            ScheduleItemType.Task,
            "Task",
            null,
            false,
            DateTimeOffset.Parse("2026-08-20T09:00:00Z"),
            null,
            null,
            null,
            "new",
            ScheduleItemPriority.Normal)).ToArray();
        using var server = CalendarEndpointFixture.CreateServer(new(rows));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(CalendarEndpointFixture.ValidScheduleUrl);

        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        Assert.Equal(500, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("/api/v1/calendar")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-19T00:00:00Z")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00%2B03:00&to=2026-08-21T00:00:00Z")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&timezone=NoSuchZone")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&departments=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&cursor=opaque")]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&unknown=x")]
    public async global::System.Threading.Tasks.Task GetSchedule_InvalidQuery_ReturnsStableValidationProblem(string url)
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(url);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        await CalendarEndpointFixture.AssertProblemAsync(response, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z")]
    [InlineData("/api/v1/calendar-events/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/api/v1/calendar/conflicts?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z")]
    public async global::System.Threading.Tasks.Task CalendarReads_WithoutToken_Return401(string url)
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]));
        using var client = server.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(response, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSchedule_WithoutCalendarRead_Returns403()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]), grantCalendarRead: false);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(CalendarEndpointFixture.ValidScheduleUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(response, "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSchedule_WhenStoreFails_ReturnsSafeRetryable503()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([], throwOnRead: true));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(CalendarEndpointFixture.ValidScheduleUrl);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INTERNAL_ERROR", body);
        Assert.DoesNotContain("secret-connection", body);
    }
}

public sealed class CalendarEventDetailsEndpointsTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task GetEvent_ReturnsCanonicalDetailsAttendeesAndEtag()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var contactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var timing = CalendarEventTiming.CreateTimed(
            new DateOnly(2026, 8, 20),
            DateTimeOffset.Parse("2026-08-20T09:00:00Z"),
            DateTimeOffset.Parse("2026-08-20T10:00:00Z"),
            "UTC");
        var calendarEvent = CalendarEvent.Create(
            CalendarEndpointFixture.EventId,
            CalendarEndpointFixture.OrganizationId,
            CalendarEndpointFixture.UserId,
            projectId: null,
            "Review",
            "Quarterly review",
            timing,
            DateTimeOffset.Parse("2026-08-19T08:00:00Z"),
            [EventAttendee.Create(userId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Accepted,
                DateTimeOffset.Parse("2026-08-19T09:00:00Z"))],
            [ContactAttendee.Create(contactId, CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Pending, null)]);
        using var server = CalendarEndpointFixture.CreateServer(new([]), calendarEvent);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync($"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.Equal(CalendarEndpointFixture.OrganizationId, root.GetProperty("organizationId").GetGuid());
        Assert.Equal("2026-08-20T09:00:00Z", root.GetProperty("startAtUtc").GetString());
        Assert.Equal("scheduled", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("lifecycleState", out _));
        var user = Assert.Single(root.GetProperty("userAttendees").EnumerateArray());
        Assert.Equal("required", user.GetProperty("role").GetString());
        Assert.Equal("accepted", user.GetProperty("responseStatus").GetString());
        var contact = Assert.Single(root.GetProperty("contactAttendees").EnumerateArray());
        Assert.Equal(contactId, contact.GetProperty("contactId").GetGuid());
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("cccccccc-cccc-cccc-cccc-cccccccccccc")]
    public async global::System.Threading.Tasks.Task GetEvent_InvalidOrMissing_ReturnsObjectNotVisible(string eventId)
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync("/api/v1/calendar-events/" + eventId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(response, "OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetEvent_CannotReadAnotherTenant()
    {
        var calendarEvent = CalendarEvent.Create(
            CalendarEndpointFixture.EventId,
            CalendarEndpointFixture.OtherOrganizationId,
            CalendarEndpointFixture.UserId,
            null,
            "Hidden",
            null,
            CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 20), "UTC"),
            DateTimeOffset.Parse("2026-08-19T08:00:00Z"));
        using var server = CalendarEndpointFixture.CreateServer(new([]), calendarEvent);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync($"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class CalendarConflictsEndpointsTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task GetConflicts_ReturnsCanonicalOrderedConflictsAndForwardsFilters()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var third = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var fourth = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var user = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var store = new CalendarEndpointFixture.FakeScheduleStore([
            Event(first, "2026-08-20T09:00:00Z", "2026-08-20T09:40:00Z"),
            Event(second, "2026-08-20T09:20:00Z", "2026-08-20T10:00:00Z"),
            Event(third, "2026-08-20T10:10:00Z", "2026-08-20T11:10:00Z"),
            Event(fourth, "2026-08-20T10:30:00Z", "2026-08-20T11:00:00Z"),
        ]);
        using var server = CalendarEndpointFixture.CreateServer(store);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(
            $"/api/v1/calendar/conflicts?from=2026-08-20T08:00:00Z&to=2026-08-20T11:00:00Z&userIds={user:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CalendarEndpointFixture.OrganizationId, store.LastCall?.OrganizationId);
        Assert.Equal(user, Assert.Single(store.LastCall!.Users!));
        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        var conflicts = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, conflicts.Length);
        Assert.Equal("warning", conflicts[0].GetProperty("severity").GetString());
        Assert.Equal("blocking", conflicts[1].GetProperty("severity").GetString());
        Assert.Equal("2026-08-20T09:20:00Z", conflicts[0].GetProperty("overlapStart").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetConflicts_ExcludeObjectRemovesEveryPair()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var server = CalendarEndpointFixture.CreateServer(new([
            Event(first, "2026-08-20T09:00:00Z", "2026-08-20T10:00:00Z"),
            Event(second, "2026-08-20T09:30:00Z", "2026-08-20T10:30:00Z"),
        ]));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(
            CalendarEndpointFixture.ValidConflictsUrl + $"&excludeObjectId={first:D}");

        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Theory]
    [InlineData("/api/v1/calendar/conflicts")]
    [InlineData("/api/v1/calendar/conflicts?from=2026-08-20T00:00:00Z&to=2027-08-22T00:00:00Z")]
    [InlineData("/api/v1/calendar/conflicts?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&userIds=bad")]
    [InlineData("/api/v1/calendar/conflicts?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z&timezone=UTC")]
    public async global::System.Threading.Tasks.Task GetConflicts_InvalidQuery_ReturnsValidationProblem(string url)
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        var response = await client.GetAsync(url);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
        await CalendarEndpointFixture.AssertProblemAsync(response, "VALIDATION_FAILED");
    }

    private static ScheduleItemRow Event(Guid id, string start, string end) => new(
        id,
        ScheduleItemType.CalendarEvent,
        "Event",
        new DateOnly(2026, 8, 20),
        false,
        DateTimeOffset.Parse(start),
        DateTimeOffset.Parse(end),
        "UTC",
        null,
        "scheduled",
        null);
}

internal static class CalendarEndpointFixture
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    public const string ValidScheduleUrl = "/api/v1/calendar?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z";
    public const string ValidConflictsUrl = "/api/v1/calendar/conflicts?from=2026-08-20T00:00:00Z&to=2026-08-21T00:00:00Z";
    public static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid OtherOrganizationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Lazy<KeyMaterial> Keys = new(CreateKeyMaterial);

    public static TestServer CreateServer(
        FakeScheduleStore scheduleStore,
        CalendarEvent? calendarEvent = null,
        bool grantCalendarRead = true)
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
                services.AddSingleton<IAuthorizationPolicyStore>(new FakePolicyStore(grantCalendarRead));
                services.AddSingleton<PermissionDecisionService>();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton(new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keys.PrivateKeyPath}"));
                services.AddSingleton<IScheduleStore>(scheduleStore);
                services.AddSingleton<ScheduleQueryService>();
                services.AddSingleton<ICalendarEventStore>(new FakeCalendarEventStore(calendarEvent));
                services.AddSingleton<CalendarEventQueryService>();
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
                app.UseEndpoints(endpoints => endpoints.MapCalendarEndpoints());
            }));
    }

    public static async global::System.Threading.Tasks.Task<HttpClient> CreateClientAsync(TestServer server)
    {
        var client = server.CreateClient();
        var issuer = server.Host.Services.GetRequiredService<JwtAccessTokenIssuer>();
        var token = await issuer.IssueAsync(
            new JwtIssuanceRequest(UserId, SessionId, OrganizationId, 1, 1),
            CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async global::System.Threading.Tasks.Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static async global::System.Threading.Tasks.Task AssertProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJsonAsync(response);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    public sealed class FakeScheduleStore : IScheduleStore
    {
        private readonly IReadOnlyList<ScheduleItemRow> _rows;
        private readonly bool _throwOnRead;

        public FakeScheduleStore(IReadOnlyList<ScheduleItemRow> rows, bool throwOnRead = false)
        {
            _rows = rows;
            _throwOnRead = throwOnRead;
        }

        public QueryCall? LastCall { get; private set; }

        public IReadOnlyList<ScheduleItemRow> QuerySchedule(
            Guid organizationId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IReadOnlyList<Guid>? users,
            IReadOnlyList<Guid>? projects,
            string? status)
        {
            if (_throwOnRead)
            {
                throw new InvalidOperationException("secret-connection");
            }

            LastCall = new(organizationId, fromUtc, toUtc, users, projects, status);
            return _rows;
        }
    }

    public sealed record QueryCall(
        Guid OrganizationId,
        DateTimeOffset From,
        DateTimeOffset To,
        IReadOnlyList<Guid>? Users,
        IReadOnlyList<Guid>? Projects,
        string? Status);

    private sealed class FakeCalendarEventStore : ICalendarEventStore
    {
        private readonly CalendarEvent? _event;

        public FakeCalendarEventStore(CalendarEvent? calendarEvent) => _event = calendarEvent;

        public CalendarEvent? Get(Guid eventId, Guid organizationId) =>
            _event is not null && _event.Metadata.Id == eventId && _event.Metadata.OrganizationId == organizationId
                ? _event
                : null;

        public void Add(CalendarEvent calendarEvent) => throw new NotSupportedException();

        public void Save(CalendarEvent calendarEvent, int expectedVersion) => throw new NotSupportedException();
    }

    private sealed class FakePolicyStore : IAuthorizationPolicyStore
    {
        private readonly bool _grant;

        public FakePolicyStore(bool grant) => _grant = grant;

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(Guid userId, CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<Guid?>(OrganizationId);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyGrantRow>>(
                _grant && permissionCode == TaskPermissionAuthorization.TaskReadBackingPermissionCode
                    ? [new PolicyGrantRow(true)]
                    : []);

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyDenyRow>>([]);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionRequestState GetSessionRequestState(Guid organizationId, Guid sessionId, long credentialVersion,
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
        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(Guid organizationId, Guid userId, Guid? exceptSessionId,
            CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(0);
        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset olderThanUtc, int maxCount,
            CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(0);
        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(DateTimeOffset olderThanUtc, int maxCount,
            CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.FromResult(0);
    }

    private static KeyMaterial CreateKeyMaterial()
    {
        var root = Path.Combine(Path.GetTempPath(), $"task-calendar-tests-{Guid.NewGuid():N}");
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
