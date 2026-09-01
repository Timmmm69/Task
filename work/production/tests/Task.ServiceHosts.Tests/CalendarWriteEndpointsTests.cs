using System.Net;
using System.Text;
using Task.Domain.Calendar;

namespace Task.ServiceHosts.Tests;

public sealed class CalendarWriteEndpointsTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Create_ReturnsTenantScopedEventAndEtag()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]));
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);
        using var request = JsonRequest(HttpMethod.Post, "/api/v1/calendar-events", CreateBody());
        request.Headers.Add("Idempotency-Key", "calendar-create-001");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        Assert.Equal(CalendarEndpointFixture.OrganizationId, json.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal("Planning", json.RootElement.GetProperty("title").GetString());
        var id = json.RootElement.GetProperty("id").GetGuid();
        var read = await client.GetAsync($"/api/v1/calendar-events/{id:D}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Create_RequiresAuthenticationAndCreateCapability()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]), grantCalendarWrites: false);
        using var anonymous = server.CreateClient();
        using var anonymousRequest = JsonRequest(HttpMethod.Post, "/api/v1/calendar-events", CreateBody());
        anonymousRequest.Headers.Add("Idempotency-Key", "calendar-create-002");
        var unauthorized = await anonymous.SendAsync(anonymousRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = await CalendarEndpointFixture.CreateClientAsync(server);
        using var deniedRequest = JsonRequest(HttpMethod.Post, "/api/v1/calendar-events", CreateBody());
        deniedRequest.Headers.Add("Idempotency-Key", "calendar-create-003");
        var forbidden = await client.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(forbidden, "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Patch_AppliesWholeContractAtomicallyAndAdvancesOneVersion()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]), ExistingEvent());
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);
        using var request = JsonRequest(HttpMethod.Patch,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}",
            """
            {
              "title":"Updated planning",
              "description":null,
              "eventDate":"2026-09-03",
              "isAllDay":false,
              "startAtUtc":"2026-09-03T12:00:00Z",
              "endAtUtc":"2026-09-03T13:00:00Z",
              "timeZone":"UTC",
              "status":"cancelled",
              "userAttendees":[{
                "userAccountId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "role":"required",
                "responseStatus":"accepted",
                "respondedAt":"2026-09-01T12:00:00Z"
              }]
            }
            """);
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v2\"", response.Headers.ETag?.Tag);
        using var json = await CalendarEndpointFixture.ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("cancelled", root.GetProperty("status").GetString());
        Assert.Equal("Updated planning", root.GetProperty("title").GetString());
        Assert.Single(root.GetProperty("userAttendees").EnumerateArray());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED")]
    [InlineData("\"v2\"", HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT")]
    [InlineData("W/\"v1\"", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    public async global::System.Threading.Tasks.Task Patch_EnforcesStrongOptimisticConcurrency(
        string? ifMatch,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]), ExistingEvent());
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);
        using var request = JsonRequest(HttpMethod.Patch,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}", "{\"title\":\"Changed\"}");
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(response, expectedCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LifecycleEndpoints_ArchiveUnarchiveTrashAndRestoreWithEtagContract()
    {
        using var server = CalendarEndpointFixture.CreateServer(new([]), ExistingEvent());
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);

        using var archive = JsonRequest(HttpMethod.Post,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}/archive", "{}");
        archive.Headers.Add("Idempotency-Key", "calendar-archive-001");
        archive.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        var archived = await client.SendAsync(archive);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal("\"v2\"", archived.Headers.ETag?.Tag);

        using var unarchive = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}/unarchive");
        unarchive.Headers.TryAddWithoutValidation("If-Match", "\"v2\"");
        var unarchived = await client.SendAsync(unarchive);
        Assert.Equal(HttpStatusCode.OK, unarchived.StatusCode);
        Assert.Equal("\"v3\"", unarchived.Headers.ETag?.Tag);

        using var delete = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}");
        delete.Headers.TryAddWithoutValidation("If-Match", "\"v3\"");
        var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.Accepted, deleted.StatusCode);
        Assert.Equal("\"v4\"", deleted.Headers.ETag?.Tag);
        using (var receipt = await CalendarEndpointFixture.ReadJsonAsync(deleted))
        {
            Assert.Equal("calendar_event", receipt.RootElement.GetProperty("objectType").GetString());
            Assert.Equal(4, receipt.RootElement.GetProperty("version").GetInt32());
        }

        using var restore = JsonRequest(HttpMethod.Post,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}/restore", "{\"expectedVersion\":4}");
        restore.Headers.Add("Idempotency-Key", "calendar-restore-001");
        restore.Headers.TryAddWithoutValidation("If-Match", "\"v4\"");
        var restored = await client.SendAsync(restore);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("\"v5\"", restored.Headers.ETag?.Tag);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Patch_CannotCrossTenantBoundary()
    {
        var hidden = CalendarEvent.Create(
            CalendarEndpointFixture.EventId,
            CalendarEndpointFixture.OtherOrganizationId,
            CalendarEndpointFixture.UserId,
            null,
            "Hidden",
            null,
            CalendarEventTiming.CreateAllDay(new DateOnly(2026, 9, 2), "UTC"),
            DateTimeOffset.Parse("2026-09-01T08:00:00Z"));
        using var server = CalendarEndpointFixture.CreateServer(new([]), hidden);
        using var client = await CalendarEndpointFixture.CreateClientAsync(server);
        using var request = JsonRequest(HttpMethod.Patch,
            $"/api/v1/calendar-events/{CalendarEndpointFixture.EventId:D}", "{\"title\":\"Leaked\"}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await CalendarEndpointFixture.AssertProblemAsync(response, "OBJECT_NOT_VISIBLE");
    }

    private static CalendarEvent ExistingEvent() => CalendarEvent.Create(
        CalendarEndpointFixture.EventId,
        CalendarEndpointFixture.OrganizationId,
        CalendarEndpointFixture.UserId,
        null,
        "Planning",
        "Initial",
        CalendarEventTiming.CreateAllDay(new DateOnly(2026, 9, 2), "UTC"),
        DateTimeOffset.Parse("2026-09-01T08:00:00Z"));

    private static string CreateBody() =>
        """
        {
          "title":"Planning",
          "eventDate":"2026-09-02",
          "isAllDay":true,
          "timeZone":"UTC",
          "userAttendees":[],
          "contactAttendees":[]
        }
        """;

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, string body) =>
        new(method, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
