using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Task.Desktop.Security;
using Task.Desktop.TaskApi;

namespace Task.Desktop.Tests.TaskApi;

public sealed class DesktopTasksApiClientTests
{
    private static readonly Guid SessionId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2936");
    private static readonly Guid OrganizationId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2937");
    private static readonly Guid TaskId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2938");
    private static readonly Guid AuthorId = Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2939");

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_SendsUrlBearerAndCorrelation_AndParsesPage()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                $$"""{"items":[{{TaskJson()}}],"nextCursor":"next-cursor","total":1}""")));

        var result = await fixture.Client.GetTasksAsync("cursor value/+?");

        var succeeded = Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.Succeeded>(result);
        var task = Assert.Single(succeeded.Value.Items);
        Assert.Equal(TaskId, task.Id);
        Assert.Equal(OrganizationId, task.OrganizationId);
        Assert.Equal(7, task.Version);
        Assert.Equal("Подготовить отчёт", task.Title);
        Assert.Equal(DesktopTaskStatus.InProgress, task.Status);
        Assert.Equal(DesktopTaskPriority.High, task.Priority);
        Assert.Equal(TimeSpan.Zero, task.UpdatedAtUtc?.Offset);
        Assert.Equal("next-cursor", succeeded.Value.NextCursor);
        Assert.Equal(1, succeeded.Value.Total);

        var request = Assert.Single(fixture.TaskRequests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://task.example.test/api/v1/tasks?cursor=cursor%20value%2F%2B%3F",
            request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("AT_initial", request.AuthorizationParameter);
        Assert.True(Guid.TryParseExact(request.CorrelationId, "D", out var correlationId));
        Assert.NotEqual(Guid.Empty, correlationId);
        Assert.DoesNotContain("AT_initial", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("RT_initial", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTaskById_UsesCanonicalRoute_AndParsesDetails()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TaskJson())));

        var result = await fixture.Client.GetTaskByIdAsync(TaskId);

        var succeeded = Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.Succeeded>(result);
        Assert.Equal(TaskId, succeeded.Value.Id);
        Assert.Equal(AuthorId, succeeded.Value.AuthorUserId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T12:00:00Z"), succeeded.Value.StartAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T17:00:00Z"), succeeded.Value.DeadlineAtUtc);
        Assert.Empty(succeeded.Value.AssigneeIds);
        Assert.Empty(succeeded.Value.WatcherIds);
        Assert.Null(succeeded.Value.RecurrenceSeriesId);

        var request = Assert.Single(fixture.TaskRequests);
        Assert.Equal($"/api/v1/tasks/{TaskId:D}", request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Query);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_EmptyPage_IsSuccessful()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                "{\"items\":[],\"nextCursor\":null,\"total\":null}")));

        var result = await fixture.Client.GetTasksAsync();

        var succeeded = Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.Succeeded>(result);
        Assert.Empty(succeeded.Value.Items);
        Assert.Null(succeeded.Value.NextCursor);
        Assert.Null(succeeded.Value.Total);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RequiredOrInvalidTaskFields_ReturnMalformedResponse()
    {
        var valid = TaskJson();
        var bodies = new Queue<string>(
        [
            valid.Replace("\"version\":7", "\"version\":0", StringComparison.Ordinal),
            valid.Replace("\"title\":\"Подготовить отчёт\"", "\"title\":\"\"", StringComparison.Ordinal),
            valid.Replace("\"status\":\"in_progress\"", "\"status\":\"blocked\"", StringComparison.Ordinal),
            valid.Replace(",\"assigneeIds\":[]", string.Empty, StringComparison.Ordinal),
            valid.Replace("2026-08-25T09:30:00Z", "2026-08-25T12:30:00+03:00", StringComparison.Ordinal),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, bodies.Dequeue())));

        for (var index = 0; index < 5; index++)
        {
            var result = await fixture.Client.GetTaskByIdAsync(TaskId);
            Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.MalformedResponse>(result);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task HttpProblems_AreMappedToStableTypedOutcomes()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            ProblemResponse(HttpStatusCode.Forbidden, "AUTHORIZATION_DENIED"),
            ProblemResponse(HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE"),
            ProblemResponse(HttpStatusCode.BadRequest, "SEARCH_CURSOR_INVALID"),
            ProblemResponse(HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR"),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));

        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.Forbidden>(
            await fixture.Client.GetTasksAsync());
        Assert.IsType<DesktopTasksApiResult<DesktopTaskDto>.NotFound>(
            await fixture.Client.GetTaskByIdAsync(TaskId));
        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor>(
            await fixture.Client.GetTasksAsync("invalid"));
        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable>(
            await fixture.Client.GetTasksAsync());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NetworkFailure_ReturnsServerUnavailable()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                new HttpRequestException("synthetic transport failure")));

        var result = await fixture.Client.GetTasksAsync();

        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CallerCancellation_IsPropagated()
    {
        await using var fixture = await Fixture.CreateAsync(async (_, cancellationToken) =>
        {
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.GetTasksAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Unauthorized_PerformsOneRefresh_AndOneGetRetry()
    {
        var taskAttempt = 0;
        await using var fixture = await Fixture.CreateAsync((_, _) =>
        {
            taskAttempt++;
            return global::System.Threading.Tasks.Task.FromResult(taskAttempt == 1
                ? ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")
                : JsonResponse(HttpStatusCode.OK, "{\"items\":[],\"nextCursor\":null,\"total\":null}"));
        });

        var result = await fixture.Client.GetTasksAsync();

        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.Succeeded>(result);
        Assert.Equal(2, taskAttempt);
        var requests = fixture.TaskRequests.ToArray();
        Assert.Equal("AT_initial", requests[0].AuthorizationParameter);
        Assert.Equal("AT_refreshed", requests[1].AuthorizationParameter);
        Assert.Equal(requests[0].CorrelationId, requests[1].CorrelationId);
        Assert.Equal(1, fixture.RefreshRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TerminalRefresh_SignsOutSharedSession_AndReturnsAuthenticationFailure()
    {
        await using var fixture = await Fixture.CreateAsync(
            (_, _) => global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_REVOKED")),
            (_, _) => global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_REVOKED")));
        SessionSignOutReason? signOutReason = null;
        fixture.SessionService.SignedOut += reason => signOutReason = reason;

        var result = await fixture.Client.GetTasksAsync();

        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure>(result);
        Assert.Equal(SessionAuthState.SignedOut, fixture.SessionService.CurrentState);
        Assert.Equal(SessionSignOutReason.SessionRevoked, signOutReason);
        Assert.Null(fixture.SessionService.GetAccessTokenForRequest());
        Assert.Single(fixture.TaskRequests);
        Assert.Equal(1, fixture.RefreshRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RetryableRefreshFailure_KeepsSession_AndDoesNotRetryGet()
    {
        await using var fixture = await Fixture.CreateAsync(
            (_, _) => global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")),
            (_, _) => global::System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                new HttpRequestException("synthetic refresh transport failure")));

        var result = await fixture.Client.GetTasksAsync();

        Assert.IsType<DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable>(result);
        Assert.Equal(SessionAuthState.SignedIn, fixture.SessionService.CurrentState);
        Assert.Equal("AT_initial", fixture.SessionService.GetAccessTokenForRequest());
        Assert.Single(fixture.TaskRequests);
        Assert.Equal(1, fixture.RefreshRequests);
    }

    private static string TaskJson() =>
        $$"""{"id":"{{TaskId:D}}","organizationId":"{{OrganizationId:D}}","version":7,"createdAt":"2026-08-20T08:00:00Z","updatedAt":"2026-08-25T09:30:00Z","projectId":null,"parentTaskId":null,"title":"Подготовить отчёт","description":null,"authorUserId":"{{AuthorId:D}}","requesterUserId":null,"primaryCounterpartyObjectId":null,"status":"in_progress","priority":"high","scheduledDate":null,"startTimeLocal":null,"scheduleTimeZone":null,"startAtUtc":"2026-08-24T12:00:00Z","plannedDurationMinutes":null,"deadlineAt":"2026-08-27T17:00:00Z","assigneeIds":[],"watcherIds":[],"recurrenceSeriesId":null}""";

    private static string TokensJson(string accessToken, string refreshToken) =>
        $$"""{"accessToken":"{{accessToken}}","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"{{refreshToken}}","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(30):O}}","sessionId":"{{SessionId:D}}"}""";

    private static string SessionJson() =>
        $$"""{"userId":"{{AuthorId:D}}","sessionId":"{{SessionId:D}}","organizationId":"{{OrganizationId:D}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":false}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ProblemResponse(HttpStatusCode statusCode, string code) =>
        new(statusCode)
        {
            Content = new StringContent(
                $$"""{"title":"Error","status":{{(int)statusCode}},"code":"{{code}}","correlationId":"test-correlation"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly HttpClient _authHttpClient;
        private readonly HttpClient _tasksHttpClient;

        private Fixture(
            string directory,
            HttpClient authHttpClient,
            HttpClient tasksHttpClient,
            SessionService sessionService,
            DesktopTasksApiClient client,
            RecordingHandler taskHandler,
            Func<int> refreshRequests)
        {
            _directory = directory;
            _authHttpClient = authHttpClient;
            _tasksHttpClient = tasksHttpClient;
            SessionService = sessionService;
            Client = client;
            TaskRequests = taskHandler.Requests;
            _refreshRequests = refreshRequests;
        }

        private readonly Func<int> _refreshRequests;

        public DesktopTasksApiClient Client { get; }

        public SessionService SessionService { get; }

        public ConcurrentQueue<CapturedRequest> TaskRequests { get; }

        public int RefreshRequests => _refreshRequests();

        public static async global::System.Threading.Tasks.Task<Fixture> CreateAsync(
            Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> taskResponder,
            Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>>? refreshResponder = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "TaskDesktopTasksTests", Guid.NewGuid().ToString("N"));
            var refreshRequests = 0;
            var authHandler = new RecordingHandler((request, cancellationToken) =>
            {
                return request.RequestUri?.AbsolutePath switch
                {
                    "/api/v1/auth/login" => global::System.Threading.Tasks.Task.FromResult(
                        JsonResponse(HttpStatusCode.OK, TokensJson("AT_initial", "RT_initial"))),
                    "/api/v1/auth/session" => global::System.Threading.Tasks.Task.FromResult(
                        JsonResponse(HttpStatusCode.OK, SessionJson())),
                    "/api/v1/auth/refresh" => RefreshAsync(request, cancellationToken),
                    _ => global::System.Threading.Tasks.Task.FromResult(
                        ProblemResponse(HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE")),
                };

                global::System.Threading.Tasks.Task<HttpResponseMessage> RefreshAsync(
                    HttpRequestMessage refreshRequest,
                    CancellationToken refreshCancellationToken)
                {
                    Interlocked.Increment(ref refreshRequests);
                    return refreshResponder?.Invoke(refreshRequest, refreshCancellationToken)
                        ?? global::System.Threading.Tasks.Task.FromResult(
                            JsonResponse(HttpStatusCode.OK, TokensJson("AT_refreshed", "RT_refreshed")));
                }
            });
            var authHttpClient = new HttpClient(authHandler);
            var vault = new DesktopCredentialVault(directory);
            var sessionService = new SessionService(
                new DesktopAuthApiClient(authHttpClient, "https://task.example.test"),
                vault,
                "test-device",
                ClientPlatform.Windows,
                "0.1.0");
            var login = await sessionService.LoginAsync(
                "user@example.test",
                "synthetic-password",
                Guid.NewGuid().ToString("D"),
                CancellationToken.None);
            Assert.IsType<LoginResult.Succeeded>(login);

            var taskHandler = new RecordingHandler(taskResponder);
            var tasksHttpClient = new HttpClient(taskHandler);
            var client = new DesktopTasksApiClient(
                tasksHttpClient,
                new Uri("https://task.example.test"),
                sessionService);
            return new Fixture(
                directory,
                authHttpClient,
                tasksHttpClient,
                sessionService,
                client,
                taskHandler,
                () => refreshRequests);
        }

        public ValueTask DisposeAsync()
        {
            SessionService.Dispose();
            _tasksHttpClient.Dispose();
            _authHttpClient.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> _responder;

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("X-Correlation-ID", out var correlationValues);
            Requests.Enqueue(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                correlationValues?.SingleOrDefault()));
            return _responder(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? CorrelationId);
}
