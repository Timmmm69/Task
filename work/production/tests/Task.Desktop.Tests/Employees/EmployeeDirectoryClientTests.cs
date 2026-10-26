using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Task.Application.Security;
using Task.Desktop.Administration;
using Task.Desktop.Security;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Employees;

public sealed class EmployeeDirectoryClientTests
{
    private static string UserJson(Guid id, string status = "active") => JsonSerializer.Serialize(new
    { id, version = 1, displayName = "Иван Иванов", login = "ivan", jobTitle = "Инженер", accountStatus = status });
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async AsyncTask SearchEscapesFilterAndCursorUsesBearerAndDeduplicatesOnePage()
    {
        var id = Guid.NewGuid(); var body = UserJson(id); var count = 0;
        await using var fixture = await Fixture.Create((request, _) =>
        {
            count++; Assert.Equal("AT_initial", request.Headers.Authorization?.Parameter);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/users", request.RequestUri!.AbsolutePath);
            Assert.Contains("filter=" + Uri.EscapeDataString("Иван &+%"), request.RequestUri.Query);
            Assert.Contains("sort=id", request.RequestUri.Query);
            Assert.Contains("cursor=next%2B%2F%3D", request.RequestUri.Query);
            return AsyncTask.FromResult(Json(HttpStatusCode.OK, $$"""{"items":[{{body}},{{body}}],"nextCursor":"last","total":200}"""));
        });
        var result = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded>(await fixture.Client.SearchUsersAsync(" Иван &+% ", "next+/="));
        Assert.Equal(id, Assert.Single(result.Value).Id); Assert.Equal("last", result.NextCursor); Assert.Equal(1, count);
    }

    [Fact]
    public async AsyncTask PreselectionUsesAccountEndpointAndChecksReturnedIdentity()
    {
        var id = Guid.NewGuid(); var wrong = false;
        await using var fixture = await Fixture.Create((request, _) =>
        {
            Assert.Equal("/api/v1/users/" + id.ToString("D"), request.RequestUri!.AbsolutePath);
            return AsyncTask.FromResult(Json(HttpStatusCode.OK, UserJson(wrong ? Guid.NewGuid() : id)));
        });
        Assert.Equal(id, Assert.IsType<DesktopWorkResult<DesktopAdminUser>.Succeeded>(await fixture.Client.GetUserAsync(id)).Value.Id);
        wrong = true; Assert.IsType<DesktopWorkResult<DesktopAdminUser>.MalformedResponse>(await fixture.Client.GetUserAsync(id));
        Assert.IsType<DesktopWorkResult<DesktopAdminUser>.NotFound>(await fixture.Client.GetUserAsync(Guid.Empty));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServerUnavailable")]
    [InlineData(HttpStatusCode.BadRequest, "MalformedResponse")]
    public async AsyncTask HttpErrorsAreControlledForSearchAndPreselection(HttpStatusCode status, string type)
    {
        await using var fixture = await Fixture.Create((_, _) => AsyncTask.FromResult(Json(status, "sensitive server details")));
        Assert.StartsWith(type, (await fixture.Client.SearchUsersAsync("Иван")).GetType().Name);
        Assert.StartsWith(type, (await fixture.Client.GetUserAsync(Guid.NewGuid())).GetType().Name);
    }

    [Fact]
    public async AsyncTask UnauthorizedUsesExistingRefreshAndAuthenticationFailure()
    {
        await using var fixture = await Fixture.Create((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.Unauthorized, "{}")));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.AuthenticationFailure>(await fixture.Client.SearchUsersAsync("Иван"));
        Assert.Equal(1, fixture.Refreshes);
    }

    [Fact]
    public async AsyncTask NetworkFailureAndCancellationAreControlled()
    {
        await using var offline = await Fixture.Create((_, _) => throw new HttpRequestException("private network detail"));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.ServerUnavailable>(await offline.Client.SearchUsersAsync("Иван"));
        using var source = new CancellationTokenSource();
        var started = new System.Threading.Tasks.TaskCompletionSource();
        await using var fixture = await Fixture.Create(async (_, token) => { started.SetResult(); await AsyncTask.Delay(Timeout.Infinite, token); return Json(HttpStatusCode.OK, "{}"); });
        var pending = fixture.Client.SearchUsersAsync("Иван", cancellationToken: source.Token);
        await started.Task; source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[],\"nextCursor\":false,\"total\":0}")]
    [InlineData("{\"items\":[],\"nextCursor\":null,\"total\":null}")]
    [InlineData("{\"items\":[],\"nextCursor\":null,\"total\":-1}")]
    [InlineData("{\"items\":[{\"id\":\"bad\"}],\"nextCursor\":null,\"total\":1}")]
    public async AsyncTask MalformedResponsesDoNotEscape(string body)
    {
        await using var fixture = await Fixture.Create((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse>(await fixture.Client.SearchUsersAsync("test"));
        Assert.IsType<DesktopWorkResult<DesktopAdminUser>.MalformedResponse>(await fixture.Client.GetUserAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("blocked", true)]
    [InlineData("pending_activation", true)]
    [InlineData("deactivated", true)]
    [InlineData("unknown", false)]
    public async AsyncTask ClientRecognizesOnlyExistingAccountStatuses(string status, bool valid)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.Create((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.OK, UserJson(id, status))));
        var result = await fixture.Client.GetUserAsync(id);
        Assert.Equal(valid, result is DesktopWorkResult<DesktopAdminUser>.Succeeded);
    }

    [Fact]
    public async AsyncTask OversizedPageAndRepeatedCursorAreRejected()
    {
        var body = "{\"items\":[" + string.Join(',', Enumerable.Range(0, 101).Select(_ => UserJson(Guid.NewGuid()))) + "],\"nextCursor\":null,\"total\":101}";
        await using var fixture = await Fixture.Create((_, _) => AsyncTask.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse>(await fixture.Client.SearchUsersAsync(""));
        body = "{\"items\":[],\"nextCursor\":\"same\",\"total\":0}";
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse>(await fixture.Client.SearchUsersAsync("", "same"));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.ValidationFailure>(await fixture.Client.SearchUsersAsync(new string('x', 2001)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "TaskEmployeePickerTests", Guid.NewGuid().ToString("N"));
        private HttpClient _auth = null!, _http = null!;
        private SessionService _session = null!;
        public DesktopAdministrationApiClient Client { get; private set; } = null!;
        public int Refreshes { get; private set; }
        public static async System.Threading.Tasks.Task<Fixture> Create(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> respond)
        {
            var fixture = new Fixture(); var sessionId = Guid.NewGuid();
            fixture._auth = new HttpClient(new Handler((request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/v1/auth/refresh") fixture.Refreshes++;
                return AsyncTask.FromResult(path switch
                {
                    "/api/v1/auth/login" => Json(HttpStatusCode.OK, $$"""{"accessToken":"AT_initial","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"RT_initial","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(1):O}}","sessionId":"{{sessionId:D}}"}"""),
                    "/api/v1/auth/session" => Json(HttpStatusCode.OK, $$"""{"userId":"{{Guid.NewGuid():D}}","sessionId":"{{sessionId:D}}","organizationId":"{{Guid.NewGuid():D}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":false}"""),
                    _ => Json(HttpStatusCode.Unauthorized, "{\"code\":\"SESSION_REVOKED\"}"),
                });
            }));
            fixture._session = new(new DesktopAuthApiClient(fixture._auth, "https://task.example.test"),
                new DesktopCredentialVault(fixture._directory), "employee-test", ClientPlatform.Windows, "1.0.0");
            Assert.IsType<LoginResult.Succeeded>(await fixture._session.LoginAsync("user", "password", Guid.NewGuid().ToString("D"), CancellationToken.None));
            fixture._http = new HttpClient(new Handler(respond));
            fixture.Client = new(fixture._http, new Uri("https://task.example.test"), fixture._session);
            return fixture;
        }
        public ValueTask DisposeAsync()
        {
            _session.Dispose(); _http.Dispose(); _auth.Dispose();
            if (System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
