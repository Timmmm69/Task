using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Task.Desktop.Administration;
using Task.Desktop.Security;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Administration;

public sealed class DesktopUserManagementApiClientTests
{
    private static readonly Guid UserId = Guid.Parse("00000001-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private const string Key = "user-operation-001";

    [Fact]
    public async System.Threading.Tasks.Task PagesPreserveOpaqueCursorFilterAndServerTotal()
    {
        var nextId = Guid.Parse("00000002-0000-0000-0000-000000000002");
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(UserId.ToString("D"))).TrimEnd('=');
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, Page(UserJson(), cursor, 2)),
            Json(HttpStatusCode.OK, Page(UserJson(nextId), null, 2))]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var first = Ok(await fixture.Client.GetUserPageAsync(new("Анна & +/=?")));
        var second = Ok(await fixture.Client.GetUserPageAsync(new("Анна & +/=?", first.Value.NextCursor, 99)));
        Assert.Equal(2, first.Value.Total);
        Assert.Equal(cursor, first.Value.NextCursor);
        Assert.Null(second.Value.NextCursor);
        Assert.Equal(2, first.Value.Items.Concat(second.Value.Items).Select(u => u.Id).Distinct().Count());
        Assert.Contains("filter=" + Uri.EscapeDataString("Анна & +/=?"), fixture.Requests[1].Uri.Query);
        Assert.Contains("cursor=" + Uri.EscapeDataString(cursor), fixture.Requests[1].Uri.Query);
        Assert.DoesNotContain("page=", fixture.Requests[1].Uri.Query);
        Assert.Contains("sort=id", fixture.Requests[0].Uri.Query);
        Assert.Contains("page=1", fixture.Requests[0].Uri.Query);
        Assert.All(fixture.Requests, r => Assert.Equal("AT_initial", r.Bearer));
    }

    [Fact]
    public async System.Threading.Tasks.Task ReadsFullPermittedCardAndConfirmedEtag()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse()));
        var result = Ok(await fixture.Client.GetUserDetailsAsync(UserId));
        Assert.Equal(UserId, result.Value.Id);
        Assert.Equal(OrganizationId, result.Value.OrganizationId);
        Assert.Equal(7, result.EntityVersion);
        Assert.Equal("Анна", result.Value.FirstName);
        Assert.Equal("Крылова", result.Value.LastName);
        Assert.Equal("anna.k", result.Value.Login);
        Assert.Equal("anna@example.test", result.Value.WorkEmail);
        Assert.Equal(UserId, result.Value.DepartmentId);
        Assert.Equal("Руководитель", result.Value.JobTitle);
        Assert.Equal("active", result.Value.AccountStatus);
        Assert.NotNull(result.Value.CreatedAt);
        Assert.Equal($"/api/v1/users/{UserId:D}", fixture.Requests.Single().Uri.AbsolutePath);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateUsesOnlyCreateFieldsAndReturnsServerValues()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse(status: HttpStatusCode.Created)));
        var result = Ok(await fixture.Client.CreateUserAsync(new("Анна", "Крылова", "anna.k"), Key));
        Assert.Equal("Анна Крылова", result.Value.DisplayName);
        Assert.Equal(7, result.EntityVersion);
        var request = fixture.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/users", request.Uri.AbsolutePath);
        Assert.Equal(Key, request.Key);
        Assert.Null(request.IfMatch);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal(3, body.Count);
        Assert.Equal("Анна", body["firstName"]!.GetValue<string>());
        Assert.False(body.ContainsKey("accountStatus"));
        Assert.False(body.ContainsKey("version"));
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateAndPatchPreserveAllSpecifiedOptionalValues()
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            UserResponse(status: request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)));
        Ok(await fixture.Client.CreateUserAsync(new("Анна", "Крылова", "anna.k", "Отображение",
            "anna@example.test", UserId, "Руководитель"), Key));
        Ok(await fixture.Client.UpdateUserAsync(UserId, 6, new()
        {
            DisplayName = new(true, "Отображение"), FirstName = new(true, "Анна"), LastName = new(true, "Крылова"),
            Login = new(true, "anna.k"), WorkEmail = new(true, "anna@example.test"), DepartmentId = new(true, UserId),
            JobTitle = new(true, "Руководитель"),
        }, Key));
        var created = JsonNode.Parse(fixture.Requests[0].Body)!.AsObject();
        var patched = JsonNode.Parse(fixture.Requests[1].Body)!.AsObject();
        Assert.Equal(7, created.Count); Assert.Equal(7, patched.Count);
        Assert.True(JsonNode.DeepEquals(created, patched));
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingRequiredCardFieldFailsClosed()
    {
        var body = JsonNode.Parse(UserJson())!.AsObject(); body.Remove("firstName");
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse(body.ToJsonString())));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.MalformedResponse>(await fixture.Client.GetUserDetailsAsync(UserId));
    }

    [Fact]
    public async System.Threading.Tasks.Task PatchOmitsUnchangedFieldsAndExplicitlyClearsNullableFields()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse()));
        var patch = new DesktopUserPatch { DisplayName = new(true, "Изменено"), WorkEmail = new(true, null),
            DepartmentId = new(true, null), JobTitle = new(true, null) };
        var result = Ok(await fixture.Client.UpdateUserAsync(UserId, 6, patch, Key));
        var request = fixture.Requests.Single();
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("\"v6\"", request.IfMatch);
        Assert.Equal(Key, request.Key);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal(4, body.Count);
        Assert.True(body.ContainsKey("workEmail")); Assert.Null(body["workEmail"]);
        Assert.True(body.ContainsKey("departmentId")); Assert.Null(body["departmentId"]);
        Assert.True(body.ContainsKey("jobTitle")); Assert.Null(body["jobTitle"]);
        Assert.False(body.ContainsKey("login")); Assert.False(body.ContainsKey("firstName"));
        Assert.Equal(7, result.EntityVersion);
        Assert.Equal("Анна Крылова", result.Value.DisplayName);
    }

    [Theory]
    [InlineData(DesktopUserTransition.Activate, "activate", null, true)]
    [InlineData(DesktopUserTransition.Block, "block", "Причина", true)]
    [InlineData(DesktopUserTransition.Deactivate, "deactivate", "Причина", true)]
    [InlineData(DesktopUserTransition.Reactivate, "reactivate", null, false)]
    [InlineData(DesktopUserTransition.Unblock, "unblock", null, false)]
    public async System.Threading.Tasks.Task TransitionsUseExistingBodyAndKeyRules(
        DesktopUserTransition transition, string route, string? reason, bool hasBody)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse()));
        Ok(await fixture.Client.TransitionUserAsync(UserId, 6, transition, hasBody ? Key : null, reason));
        var request = fixture.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/api/v1/users/{UserId:D}/{route}", request.Uri.AbsolutePath);
        Assert.Equal("\"v6\"", request.IfMatch);
        Assert.Equal(hasBody ? Key : null, request.Key);
        if (hasBody)
        {
            var body = JsonNode.Parse(request.Body)!.AsObject();
            Assert.Equal(6, body["expectedVersion"]!.GetValue<long>());
            Assert.Equal(reason, body["reason"]?.GetValue<string>());
            Assert.Equal(reason is null ? 1 : 2, body.Count);
        }
        else Assert.Equal("", request.Body);
    }

    [Theory]
    [InlineData(400, "VALIDATION_FAILED", "ValidationFailure")]
    [InlineData(422, "VALIDATION_FAILED", "ValidationFailure")]
    [InlineData(403, "FORBIDDEN", "Forbidden")]
    [InlineData(404, "OBJECT_NOT_VISIBLE", "NotFound")]
    [InlineData(409, "DUPLICATE_RESOURCE", "Conflict")]
    [InlineData(409, "INVALID_STATE_TRANSITION", "Conflict")]
    [InlineData(409, "IDEMPOTENCY_KEY_REUSED", "Conflict")]
    [InlineData(409, "REQUEST_IN_PROGRESS", "Conflict")]
    [InlineData(412, "VERSION_CONFLICT", "VersionConflict")]
    [InlineData(428, "PRECONDITION_REQUIRED", "PreconditionRequired")]
    [InlineData(503, "INTERNAL_ERROR", "ServerUnavailable")]
    public async System.Threading.Tasks.Task ErrorsRetainStableCodesWithoutExposingProblemBodies(int status, string code, string type)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(
            Json((HttpStatusCode)status, $$"""{"code":"{{code}}","title":"private server details","retryable":true,"retryAfterSeconds":3}""")));
        var result = await fixture.Client.UpdateUserAsync(UserId, 6, new() { Login = new(true, "anna.k") }, Key);
        Assert.Equal(type, result.GetType().Name);
        Assert.Equal(code, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal(3, result.RetryAfterSeconds);
        Assert.True(Guid.TryParse(result.CorrelationId, out _));
        Assert.DoesNotContain("private server details", result.ToString());
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("broken")]
    [InlineData("{\"code\":42}")]
    public async System.Threading.Tasks.Task MalformedProblemStillHasStatusCategory(string body)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Forbidden, body)));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.Forbidden>(await fixture.Client.GetUserDetailsAsync(UserId));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"items\":[],\"total\":0}")]
    [InlineData("{\"items\":[],\"nextCursor\":null,\"total\":-1}")]
    [InlineData("{\"items\":[],\"nextCursor\":\"same\",\"total\":1}")]
    [InlineData("{\"items\":[null],\"nextCursor\":null,\"total\":1}")]
    public async System.Threading.Tasks.Task InvalidPageShapesFailClosed(string body)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<DesktopUserResult<DesktopUserPage>.MalformedResponse>(await fixture.Client.GetUserPageAsync(new()));
    }

    [Fact]
    public async System.Threading.Tasks.Task DuplicateRowsRepeatedCursorAndOversizedPageAreRejected()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, Page(UserJson() + "," + UserJson(), null, 2)),
            Json(HttpStatusCode.OK, Page(UserJson(), "same", 2)),
            Json(HttpStatusCode.OK, Page(string.Join(",", Enumerable.Repeat(UserJson(), 101)), null, 101))]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        for (var i = 0; i < 3; i++)
            Assert.IsType<DesktopUserResult<DesktopUserPage>.MalformedResponse>(await fixture.Client.GetUserPageAsync(new(Cursor: "same")));
    }

    [Theory]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("id", "\"00000002-0000-0000-0000-000000000002\"")]
    [InlineData("version", "0")]
    [InlineData("version", "\"7\"")]
    [InlineData("firstName", "null")]
    [InlineData("accountStatus", "\"unknown\"")]
    [InlineData("departmentId", "42")]
    [InlineData("workEmail", "42")]
    [InlineData("createdAt", "\"invalid-date\"")]
    public async System.Threading.Tasks.Task MalformedCardCannotBeReportedAsSuccess(string field, string value)
    {
        var body = JsonNode.Parse(UserJson())!.AsObject(); body[field] = JsonNode.Parse(value);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse(body.ToJsonString())));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.MalformedResponse>(await fixture.Client.GetUserDetailsAsync(UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("W/\"v7\"")]
    [InlineData("\"v6\"")]
    public async System.Threading.Tasks.Task MissingWeakOrMismatchedEtagRejectsMutationReceipt(string? etag)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse(etag: etag)));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.MalformedResponse>(await fixture.Client.UpdateUserAsync(UserId, 6, new() { Login = new(true, "anna.k") }, Key));
    }

    [Fact]
    public async System.Threading.Tasks.Task NullableReadFieldsAndMissingOptionalTimestampsAreSupported()
    {
        var body = JsonNode.Parse(UserJson())!.AsObject();
        body["workEmail"] = null; body["departmentId"] = null; body["jobTitle"] = null;
        body.Remove("createdAt"); body.Remove("updatedAt");
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(UserResponse(body.ToJsonString())));
        var result = Ok(await fixture.Client.GetUserDetailsAsync(UserId));
        Assert.Null(result.Value.WorkEmail); Assert.Null(result.Value.DepartmentId); Assert.Null(result.Value.JobTitle);
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidInputsAndEmptyPatchNeverSendRequests()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new InvalidOperationException("Must not send"));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.CreateUserAsync(new("", "Фамилия", "abc"), Key));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.CreateUserAsync(new("Имя", "Фамилия", "abc"), "short"));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.UpdateUserAsync(UserId, 1, new(), Key));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.UpdateUserAsync(UserId, 1, new() { Login = new(true, null) }, Key));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.UpdateUserAsync(UserId, 0, new() { Login = new(true, "abc") }, Key));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.TransitionUserAsync(UserId, 1, DesktopUserTransition.Block, Key));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ValidationFailure>(await fixture.Client.TransitionUserAsync(UserId, 1, (DesktopUserTransition)99));
        Assert.IsType<DesktopUserResult<DesktopUserPage>.ValidationFailure>(await fixture.Client.GetUserPageAsync(new(Page: 0)));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task RefreshReplaysExactlyTheSameMutationAndReturnsReplayReceipt()
    {
        var calls = 0;
        await using var fixture = await Fixture.CreateAsync((_, _) =>
        {
            if (++calls == 1) return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}"));
            var response = UserResponse(); response.Headers.Add("Idempotency-Replayed", "true");
            return System.Threading.Tasks.Task.FromResult(response);
        }, refreshSucceeds: true);
        var result = Ok(await fixture.Client.UpdateUserAsync(UserId, 6, new() { WorkEmail = new(true, null) }, Key));
        Assert.True(result.Replayed);
        Assert.Equal(1, fixture.Refreshes()); Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal(fixture.Requests[0] with { Bearer = "AT_refreshed" }, fixture.Requests[1]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async System.Threading.Tasks.Task UnauthorizedStopsAfterAtMostOneRefresh(bool refreshSucceeds, int expectedRequests)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")), refreshSucceeds);
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.AuthenticationFailure>(await fixture.Client.GetUserDetailsAsync(UserId));
        Assert.Equal(1, fixture.Refreshes()); Assert.Equal(expectedRequests, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task NetworkFailureOrTimeoutDoesNotRetryAnUncertainWrite(bool timeout)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => timeout
            ? System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new TaskCanceledException())
            : System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new HttpRequestException()));
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ServerUnavailable>(await fixture.Client.CreateUserAsync(new("Анна", "Крылова", "anna.k"), Key));
        Assert.Single(fixture.Requests); Assert.Equal(0, fixture.Refreshes());
    }

    [Fact]
    public async System.Threading.Tasks.Task OfflineBlocksWritesUntilReadConfirmsRecovery()
    {
        var calls = 0;
        await using var fixture = await Fixture.CreateAsync((_, _) => ++calls == 1
            ? System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new HttpRequestException())
            : System.Threading.Tasks.Task.FromResult(UserResponse()));
        await fixture.Client.GetUserDetailsAsync(UserId);
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.ServerUnavailable>(await fixture.Client.UpdateUserAsync(UserId, 6, new() { Login = new(true, "abc") }, Key));
        Assert.Single(fixture.Requests);
        Ok(await fixture.Client.GetUserDetailsAsync(UserId));
        Ok(await fixture.Client.UpdateUserAsync(UserId, 6, new() { Login = new(true, "abc") }, Key));
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task SignedOutSessionNeverSendsAnAuthenticatedRequest()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new InvalidOperationException("Must not send"));
        await fixture.Session.LogoutAsync();
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.AuthenticationFailure>(await fixture.Client.CreateUserAsync(new("Анна", "Крылова", "anna.k"), Key));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task PasswordChangeRequiredReadinessCannotMutateUsers()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new InvalidOperationException("Must not send"), passwordChangeRequired: true);
        Assert.Equal(SessionReadinessState.PasswordChangeRequired, fixture.Session.CurrentReadiness);
        Assert.IsType<DesktopUserResult<DesktopManagedUser>.AuthenticationFailure>(await fixture.Client.CreateUserAsync(new("Анна", "Крылова", "anna.k"), Key));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationPropagatesWithoutRetryOrSuccess()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(async (_, token) =>
        { entered.SetResult(); await System.Threading.Tasks.Task.Delay(Timeout.Infinite, token); return UserResponse(); });
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Client.UpdateUserAsync(UserId, 6, new() { Login = new(true, "abc") }, Key, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.GetUserPageAsync(new(), cancellation.Token));
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task RoleDefinitionsKeepAllPublishedEffectsWithoutTreatingThemAsAssignments()
    {
        var role = $$"""{"items":[{"id":"{{Guid.NewGuid():D}}","code":"reader","displayName":"Reader","isSystem":true,"permissions":[{"code":"User.Read","effect":"grant"},{"code":"User.Update","effect":"deny"},{"code":"Task.Read","effect":"grant"},{"code":"Project.Read","effect":"grant"},{"code":"Role.Read","effect":"grant"}]}]}""";
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, role)));
        var definitions = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>.Succeeded>(await fixture.Client.GetRolesAsync()).Value;
        var definition = Assert.Single(definitions);
        Assert.Equal(5, definition.Permissions.Count);
        Assert.Contains("User.Update (запрет)", definition.PermissionSummary);
        Assert.Contains("Role.Read", definition.PermissionSummary);
        Assert.Equal(HttpMethod.Get, Assert.Single(fixture.Requests).Method);
    }

    [Fact]
    public async System.Threading.Tasks.Task LegacyAdministrationReadsRemainCompatible()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.OK, Page(UserJson(), null, 1)),
            Json(HttpStatusCode.OK, "{\"items\":[]}"), Json(HttpStatusCode.OK, "{\"items\":[]}")]);
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        Assert.Single(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded>(await fixture.Client.GetUsersAsync()).Value);
        Assert.Empty(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>.Succeeded>(await fixture.Client.GetRolesAsync()).Value);
        Assert.Empty(Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.Succeeded>(await fixture.Client.GetResourcesAsync()).Value);
    }

    private static DesktopUserResult<T>.Succeeded Ok<T>(DesktopUserResult<T> result) => Assert.IsType<DesktopUserResult<T>.Succeeded>(result);
    private static string Page(string items, string? cursor, long total) => new JsonObject
    { ["items"] = JsonNode.Parse("[" + items + "]"), ["nextCursor"] = cursor, ["total"] = total }.ToJsonString();
    private static string UserJson(Guid? id = null) => $$"""{"id":"{{id ?? UserId:D}}","organizationId":"{{OrganizationId:D}}","version":7,"displayName":"Анна Крылова","firstName":"Анна","lastName":"Крылова","login":"anna.k","workEmail":"anna@example.test","departmentId":"{{UserId:D}}","jobTitle":"Руководитель","accountStatus":"active","createdAt":"2026-09-01T00:00:00Z","updatedAt":"2026-10-17T00:00:00Z"}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage UserResponse(string? body = null, HttpStatusCode status = HttpStatusCode.OK, string? etag = "\"v7\"")
    { var response = Json(status, body ?? UserJson()); if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag); return response; }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly HttpClient _auth;
        private readonly HttpClient _http;
        public SessionService Session { get; }
        public DesktopAdministrationApiClient Client { get; }
        public List<CapturedRequest> Requests { get; }
        public Func<int> Refreshes { get; }

        private Fixture(string directory, HttpClient auth, HttpClient http, SessionService session,
            List<CapturedRequest> requests, Func<int> refreshes)
        {
            _directory = directory; _auth = auth; _http = http; Session = session; Requests = requests; Refreshes = refreshes;
            Client = new(http, new Uri("https://task.example.test"), session, new DesktopConnectivityService());
        }

        public static async System.Threading.Tasks.Task<Fixture> CreateAsync(
            Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder,
            bool refreshSucceeds = false, bool passwordChangeRequired = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TaskUserManagementTests", Guid.NewGuid().ToString("N"));
            var refreshes = 0;
            string Tokens(string suffix) => $$"""{"accessToken":"AT_{{suffix}}","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"RT_{{suffix}}","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(1):O}}","sessionId":"{{SessionId:D}}"}""";
            var auth = new HttpClient(new Handler((request, _) =>
            {
                var response = request.RequestUri!.AbsolutePath switch
                {
                    "/api/v1/auth/login" => Json(HttpStatusCode.OK, Tokens("initial")),
                    "/api/v1/auth/session" => Json(HttpStatusCode.OK, $$"""{"userId":"{{UserId:D}}","sessionId":"{{SessionId:D}}","organizationId":"{{OrganizationId:D}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":{{(passwordChangeRequired ? "true" : "false")}}}"""),
                    "/api/v1/auth/refresh" => Refresh(),
                    _ => Json(HttpStatusCode.NotFound, "{}"),
                };
                return System.Threading.Tasks.Task.FromResult(response);
                HttpResponseMessage Refresh()
                {
                    refreshes++;
                    return refreshSucceeds ? Json(HttpStatusCode.OK, Tokens("refreshed")) : new(HttpStatusCode.Unauthorized)
                    { Content = new StringContent("""{"type":"https://task.local/errors/session_expired","title":"Error","status":401,"code":"SESSION_EXPIRED","traceId":"t-1","correlationId":"019fa078-3f10-7ec1-99e2-7c1cba4ee3d4","fieldErrors":[]}""", Encoding.UTF8, "application/problem+json") };
                }
            }));
            var session = new SessionService(new DesktopAuthApiClient(auth, "https://task.example.test"), new DesktopCredentialVault(directory), "test", ClientPlatform.Windows, "1.0.0");
            Assert.IsType<LoginResult.Succeeded>(await session.LoginAsync("user@example.test", "password", Guid.NewGuid().ToString("D"), CancellationToken.None));
            var requests = new List<CapturedRequest>();
            var http = new HttpClient(new Handler(async (request, token) =>
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
                requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter,
                    request.Headers.IfMatch.SingleOrDefault()?.ToString(), request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                    request.Headers.GetValues("X-Correlation-ID").Single(), body));
                return await responder(request, token);
            }));
            return new(directory, auth, http, session, requests, () => refreshes);
        }

        public ValueTask DisposeAsync()
        {
            Session.Dispose(); _http.Dispose(); _auth.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> responder) : HttpMessageHandler
    { protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken); }
    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Bearer, string? IfMatch, string? Key, string Correlation, string Body);
}
