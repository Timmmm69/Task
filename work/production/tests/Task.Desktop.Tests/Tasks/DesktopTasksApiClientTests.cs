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

    [Fact]
    public async global::System.Threading.Tasks.Task Writes_SendCanonicalContracts_AndReturnValidatedMetadata()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            WriteSuccess(HttpStatusCode.Created, replayed: false),
            WriteSuccess(HttpStatusCode.OK, replayed: true),
            WriteSuccess(HttpStatusCode.OK, replayed: false),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        var create = new DesktopCreateTaskCommand(
            "  New task  ", DesktopTaskPriority.High,
            DateTimeOffset.Parse("2026-08-27T08:00:00Z"), DateTimeOffset.Parse("2026-08-28T10:00:00Z"));
        var patch = new DesktopPatchTaskCommand(
            TaskId, 6, DesktopTaskField<string>.From("Updated"),
            DesktopTaskField<DesktopTaskPriority>.From(DesktopTaskPriority.Critical),
            DesktopTaskField<DateTimeOffset?>.From(null),
            DesktopTaskField<DateTimeOffset?>.From(DateTimeOffset.Parse("2026-08-28T10:00:00Z")));
        var transition = new DesktopTransitionTaskCommand(TaskId, 6, DesktopTaskStatus.Completed, "  done  ");

        var created = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await fixture.Client.CreateTaskAsync(create));
        var patched = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await fixture.Client.PatchTaskAsync(patch));
        _ = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await fixture.Client.TransitionTaskAsync(transition));
        Assert.Equal(7, created.Version);
        Assert.False(created.WasReplayed);
        Assert.True(patched.WasReplayed);

        var requests = fixture.TaskRequests.ToArray();
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal("/api/v1/tasks", requests[0].Uri.AbsolutePath);
        Assert.Equal("{\"title\":\"New task\",\"priority\":\"high\",\"startAtUtc\":\"2026-08-27T08:00:00Z\",\"deadlineAt\":\"2026-08-28T10:00:00Z\"}", requests[0].Body);
        Assert.Null(requests[0].IfMatch);
        Assert.Equal(HttpMethod.Patch, requests[1].Method);
        Assert.Equal($"/api/v1/tasks/{TaskId:D}", requests[1].Uri.AbsolutePath);
        Assert.Equal("\"v6\"", requests[1].IfMatch);
        Assert.Equal("{\"title\":\"Updated\",\"priority\":\"critical\",\"startAtUtc\":null,\"deadlineAt\":\"2026-08-28T10:00:00Z\"}", requests[1].Body);
        Assert.Equal($"/api/v1/tasks/{TaskId:D}/transition", requests[2].Uri.AbsolutePath);
        Assert.Equal("{\"targetStatus\":\"completed\",\"reason\":\"done\"}", requests[2].Body);
        Assert.All(requests, request => Assert.True(Guid.TryParseExact(request.IdempotencyKey, "D", out _)));
        Assert.All(requests, request => Assert.Equal("application/json", request.ContentType));
        Assert.All(requests, request => Assert.Equal("utf-8", request.CharSet));
        Assert.All(requests, request => Assert.Equal(
            ["application/json", "application/problem+json"], request.Accept));
        Assert.Equal(3, requests.Select(request => request.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Write401_ReusesBodyHeadersAndKey_ForExactlyOneRetry()
    {
        var attempt = 0;
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(++attempt == 1
                ? ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")
                : WriteSuccess(HttpStatusCode.Created, replayed: false)));
        var command = new DesktopCreateTaskCommand("Retry", DesktopTaskPriority.Normal);

        _ = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await fixture.Client.CreateTaskAsync(command));

        var requests = fixture.TaskRequests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(requests[0].Body, requests[1].Body);
        Assert.Equal(requests[0].BodyBytes, requests[1].BodyBytes);
        Assert.Equal(requests[0].CorrelationId, requests[1].CorrelationId);
        Assert.Equal(requests[0].IdempotencyKey, requests[1].IdempotencyKey);
        Assert.Equal(command.IdempotencyKey, requests[0].IdempotencyKey);
        Assert.Equal("AT_initial", requests[0].AuthorizationParameter);
        Assert.Equal("AT_refreshed", requests[1].AuthorizationParameter);
        Assert.Equal(1, fixture.RefreshRequests);
        Assert.NotEqual(command.IdempotencyKey, new DesktopCreateTaskCommand("Retry", DesktopTaskPriority.Normal).IdempotencyKey);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ConcurrentWrite401s_UseSingleFlightRefresh_AndStableCommands()
    {
        var initialRequests = 0;
        var bothInitialRequests = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(async (request, cancellationToken) =>
        {
            if (request.Headers.Authorization?.Parameter == "AT_initial")
            {
                if (Interlocked.Increment(ref initialRequests) == 2)
                    bothInitialRequests.TrySetResult(true);

                await bothInitialRequests.Task.WaitAsync(cancellationToken);
                return ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
            }

            return WriteSuccess(HttpStatusCode.Created, replayed: false);
        });
        var first = new DesktopCreateTaskCommand("First", DesktopTaskPriority.Normal);
        var second = new DesktopCreateTaskCommand("Second", DesktopTaskPriority.Normal);

        var results = await global::System.Threading.Tasks.Task.WhenAll(
            fixture.Client.CreateTaskAsync(first), fixture.Client.CreateTaskAsync(second));

        Assert.All(results, result => Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(result));
        Assert.Equal(1, fixture.RefreshRequests);
        Assert.Equal(4, fixture.TaskRequests.Count);
        Assert.All(
            fixture.TaskRequests.GroupBy(request => request.IdempotencyKey),
            requests =>
            {
                Assert.Equal(2, requests.Count());
                Assert.Single(requests.Select(request => request.CorrelationId).Distinct());
                Assert.Single(requests.Select(request => Convert.ToHexString(request.BodyBytes!)).Distinct());
            });
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Write401Failures_AreTyped_AndNeverSendMoreThanOneRetry()
    {
        await using (var rejected = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED"))))
        {
            Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure>(
                await rejected.Client.CreateTaskAsync(
                    new DesktopCreateTaskCommand("Rejected", DesktopTaskPriority.Normal)));
            Assert.Equal(2, rejected.TaskRequests.Count);
            Assert.Equal(1, rejected.RefreshRequests);
        }

        await using var unavailable = await Fixture.CreateAsync(
            (_, _) => global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")),
            (_, _) => global::System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                new HttpRequestException("synthetic refresh transport failure")));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable>(
            await unavailable.Client.CreateTaskAsync(
                new DesktopCreateTaskCommand("Unavailable", DesktopTaskPriority.Normal)));
        Assert.Single(unavailable.TaskRequests);
        Assert.Equal(1, unavailable.RefreshRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WriteProblems_MapToExhaustiveSafeOutcomes()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            ProblemResponse(HttpStatusCode.Forbidden, "FORBIDDEN"),
            ProblemResponse(HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE"),
            ProblemResponse(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED"),
            ProblemResponse(HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT"),
            ProblemResponse((HttpStatusCode)428, "PRECONDITION_REQUIRED"),
            ProblemResponse(HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED"),
            ProblemResponse(HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS"),
            ProblemResponse(HttpStatusCode.Conflict, "INVALID_STATE_TRANSITION"),
            ProblemResponse(HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR"),
            WriteResponse(HttpStatusCode.Created, "{", "\"v7\"", "false"),
            WriteSuccess(HttpStatusCode.Created, replayed: false, entityTag: "\"v8\""),
            WriteResponse(HttpStatusCode.Created, TaskJson(), "\"v+7\"", "false"),
            WriteResponse(HttpStatusCode.Created, TaskJson(), "\"v7\"", "True"),
            ProblemResponse(HttpStatusCode.Conflict, "UNKNOWN_CONFLICT"),
        ]);
        await using var fixture = await Fixture.CreateAsync((_, _) =>
            global::System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> Send() =>
            fixture.Client.CreateTaskAsync(new DesktopCreateTaskCommand("Safe", DesktopTaskPriority.Normal));

        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Forbidden>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.NotFound>(await Send());
        var validation = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await Send());
        Assert.Equal("Проверьте введённые данные.", validation.Message);
        Assert.Equal("Required", Assert.Single(validation.FieldErrors["title"]));
        Assert.DoesNotContain("AT_initial", validation.Message, StringComparison.Ordinal);
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse>(await Send());
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse>(await Send());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WriteBoundary_RejectsInvalidDto_AndPropagatesCancellation()
    {
        await using var fixture = await Fixture.CreateAsync(async (_, cancellationToken) =>
        {
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        Assert.Throws<ArgumentException>(() =>
            new DesktopCreateTaskCommand(" ", DesktopTaskPriority.Normal));
        Assert.Throws<ArgumentException>(() =>
            new DesktopCreateTaskCommand("Task", (DesktopTaskPriority)999));
        Assert.Throws<ArgumentException>(() => new DesktopCreateTaskCommand(
            "Task",
            DesktopTaskPriority.Normal,
            DateTimeOffset.Parse("2026-08-27T10:00:00+03:00")));
        Assert.Throws<ArgumentException>(() => new DesktopCreateTaskCommand(
            "Task",
            DesktopTaskPriority.Normal,
            DateTimeOffset.Parse("2026-08-27T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-27T09:00:00Z")));
        Assert.Throws<ArgumentException>(() =>
            new DesktopPatchTaskCommand(TaskId, 1));
        Assert.Throws<ArgumentException>(() => new DesktopPatchTaskCommand(
            TaskId, 1, title: DesktopTaskField<string>.From(null)));
        Assert.Throws<ArgumentException>(() =>
            new DesktopTransitionTaskCommand(TaskId, 0, DesktopTaskStatus.Completed));
        Assert.Throws<ArgumentException>(() => new DesktopTransitionTaskCommand(
            TaskId, 1, DesktopTaskStatus.Completed, new string('x', 2001)));
        using var client = new HttpClient();
        Assert.Throws<ArgumentException>(() => new DesktopTasksApiClient(
            client, new Uri("/relative", UriKind.Relative), fixture.SessionService));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.CreateTaskAsync(
            new DesktopCreateTaskCommand("Cancelled", DesktopTaskPriority.Normal), cancellation.Token));
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
                $$$"""{"title":"Error AT_initial","status":{{{(int)statusCode}}},"code":"{{{code}}}","correlationId":"test-correlation","errors":{"title":["Required"]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    private static HttpResponseMessage WriteSuccess(
        HttpStatusCode statusCode,
        bool replayed,
        string entityTag = "\"v7\"") =>
        WriteResponse(statusCode, TaskJson(), entityTag, replayed ? "true" : "false");

    private static HttpResponseMessage WriteResponse(
        HttpStatusCode statusCode,
        string body,
        string? entityTag,
        string? replayed)
    {
        var response = JsonResponse(statusCode, body);
        if (entityTag is not null)
            response.Headers.TryAddWithoutValidation("ETag", entityTag);
        if (replayed is not null)
            response.Headers.TryAddWithoutValidation("Idempotency-Replayed", replayed);
        return response;
    }

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

        protected override async global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("X-Correlation-ID", out var correlationValues);
            request.Headers.TryGetValues("Idempotency-Key", out var idempotencyValues);
            var bodyBytes = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Enqueue(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                correlationValues?.SingleOrDefault(),
                request.Headers.IfMatch.SingleOrDefault()?.ToString(),
                idempotencyValues?.SingleOrDefault(),
                bodyBytes is null ? null : Encoding.UTF8.GetString(bodyBytes),
                bodyBytes,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content?.Headers.ContentType?.CharSet,
                request.Headers.Accept.Select(value => value.MediaType).ToArray()));
            return await _responder(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? CorrelationId,
        string? IfMatch,
        string? IdempotencyKey,
        string? Body,
        byte[]? BodyBytes,
        string? ContentType,
        string? CharSet,
        IReadOnlyList<string?> Accept);
}
