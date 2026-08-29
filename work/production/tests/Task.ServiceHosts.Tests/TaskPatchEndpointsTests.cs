using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Task.Api.Security;
using Task.Application;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    private static readonly DateTimeOffset TransitionAt = DateTimeOffset.Parse("2026-08-24T10:00:00Z");
    private static readonly DateTimeOffset TransitionAt2 = DateTimeOffset.Parse("2026-08-24T11:00:00Z");

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithSingleField_Returns200WithNewVersionAndEtag()
    {
        var store = new FakeTaskReadStore(Projection);
        var aggregateStore = new FakeUpdateAggregateStore(CurrentTask());
        var executor = new FakeUpdateExecutor { Current = CurrentTask(), OnUpdated = task => store.Add(ToProjection(task)) };
        using var server = CreateServer(store, writeExecutor: executor, aggregateStore: aggregateStore);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"  Renamed quarterly  "}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v8\"", response.Headers.ETag?.Tag);
        Assert.Equal("false", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.NotNull(executor.LastCommand);
        Assert.Equal(OrganizationId, executor.LastCommand.OrganizationId);
        Assert.Equal(UserId, executor.LastCommand.ActorUserId);
        Assert.Equal(["title"], executor.LastCommand.ChangedFields);
        Assert.Equal("task.update", executor.LastCommand.AuditAction);
        Assert.Equal("TaskUpdated", executor.LastCommand.EventType);
        Assert.Equal(8, executor.Current!.Metadata.Version);

        using var document = await ReadJsonAsync(response);
        var patched = document.RootElement;
        Assert.Equal("Renamed quarterly", patched.GetProperty("title").GetString());
        Assert.Equal("in_progress", patched.GetProperty("status").GetString());
        Assert.Equal("high", patched.GetProperty("priority").GetString());
        Assert.Equal(8, patched.GetProperty("version").GetInt64());

        var get = await client.GetAsync(TasksUrl + "/" + TaskId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("\"v8\"", get.Headers.ETag?.Tag);
        using var fetched = await ReadJsonAsync(get);
        Assert.Equal("Renamed quarterly", fetched.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithMultipleFields_IncrementsVersionOnce()
    {
        var aggregateStore = new FakeUpdateAggregateStore(CurrentTask());
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: aggregateStore);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(
            client,
            TaskId.ToString("D"),
            """{"title":"Multi update","priority":"critical","startAtUtc":"2026-08-24T09:00:00Z"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v8\"", response.Headers.ETag?.Tag);
        Assert.Equal(["title", "priority", "startAtUtc"], executor.LastCommand!.ChangedFields);
        Assert.Equal(8, executor.Current!.Metadata.Version);
        Assert.Equal(1, executor.MutationCalls);
        using var document = await ReadJsonAsync(response);
        Assert.Equal("Multi update", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("critical", document.RootElement.GetProperty("priority").GetString());
        Assert.Equal("2026-08-24T09:00:00Z", document.RootElement.GetProperty("startAtUtc").GetString());
        Assert.Equal(8, document.RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithExplicitNull_ClearsScheduleBounds()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"startAtUtc":null,"deadlineAt":null}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["startAtUtc", "deadlineAt"], executor.LastCommand!.ChangedFields);
        using var document = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("startAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("deadlineAt").ValueKind);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithOmittedProperties_KeepsExistingValues()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Only title"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal("high", document.RootElement.GetProperty("priority").GetString());
        Assert.Equal("2026-08-24T07:00:00Z", document.RootElement.GetProperty("startAtUtc").GetString());
        Assert.Equal("2026-08-25T15:00:00Z", document.RootElement.GetProperty("deadlineAt").GetString());
        Assert.Equal("in_progress", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithNoOp_CompletesDurablyAndReplaysWithoutSideEffects()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var first = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Prepare quarterly review"}""");
        var second = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Prepare quarterly review"}""");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("\"v7\"", first.Headers.ETag?.Tag);
        Assert.Equal("\"v7\"", second.Headers.ETag?.Tag);
        Assert.Equal("false", first.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, executor.MutationCalls);
        Assert.Equal(1, executor.CompletedCount);
        Assert.Equal(7, executor.Current!.Metadata.Version);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_NoOpKeyReuseWithDifferentIfMatch_Returns409BeforeVersionCheck()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Prepare quarterly review"}""", idempotencyKey: "noop-reuse-01")).StatusCode);
        await AssertProblemAsync(
            await PatchTaskAsync(
                client,
                TaskId.ToString("D"),
                """{"title":"Prepare quarterly review"}""",
                ifMatch: "\"v8\"",
                idempotencyKey: "noop-reuse-01"),
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_REUSED");
        await AssertProblemAsync(
            await PatchTaskAsync(
                client,
                "cccccccc-cccc-cccc-cccc-cccccccccccc",
                """{"title":"Prepare quarterly review"}""",
                idempotencyKey: "noop-reuse-01"),
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_REUSED");
    }

    [Theory]
    [InlineData("""{"status":"in_progress"}""")]
    [InlineData("""{"description":"not yet"}""")]
    [InlineData("""{"projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""")]
    [InlineData("""{"unknown":true}""")]
    [InlineData("""{}""")]
    public async global::System.Threading.Tasks.Task PatchTask_WithUnsupportedOrEmptyBody_Returns400(string body)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), body),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("""{"title":null}""")]
    [InlineData("""{"title":5}""")]
    [InlineData("""{"priority":null}""")]
    [InlineData("""{"priority":"urgent"}""")]
    [InlineData("""{"startAtUtc":5}""")]
    [InlineData("""{"startAtUtc":"2026-08-24T09:00:00+03:00"}""")]
    [InlineData("""{"deadlineAt":"not-an-instant"}""")]
    public async global::System.Threading.Tasks.Task PatchTask_WithInvalidFieldValues_Returns400(string body)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), body),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("""{"title":"   "}""")]
    public async global::System.Threading.Tasks.Task PatchTask_WithTitleLengthViolation_Returns422(string body)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), body);
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        var tooLong = await PatchTaskAsync(
            client,
            TaskId.ToString("D"),
            $$"""{"title":"{{new string('x', 501)}}"}""");
        await AssertProblemAsync(tooLong, HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithInvalidFinalSchedule_Returns422()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"deadlineAt":"2026-08-20T09:00:00Z"}""");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithoutIfMatch_Returns428()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"New"}""", ifMatch: null),
            HttpStatusCode.PreconditionRequired,
            "PRECONDITION_REQUIRED");
    }

    [Theory]
    [InlineData("v7")]
    [InlineData("W/\"v7\"")]
    [InlineData("*")]
    [InlineData("\"v0\"")]
    [InlineData("\"v-1\"")]
    [InlineData("\"v7.5\"")]
    public async global::System.Threading.Tasks.Task PatchTask_WithMalformedIfMatch_Returns400(string ifMatch)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"New"}""", ifMatch: ifMatch),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithMultipleIfMatchValues_Returns400()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), TasksUrl + "/" + TaskId.ToString("D"))
        {
            Content = new StringContent("""{"title":"New"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v7\"");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v8\"");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "patch-key-01");

        await AssertProblemAsync(await client.SendAsync(request), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("\"v6\"")]
    [InlineData("\"v2147483648\"")]
    public async global::System.Threading.Tasks.Task PatchTask_WithStaleIfMatch_Returns412WithoutOverwriting(string ifMatch)
    {
        var aggregateStore = new FakeUpdateAggregateStore(CurrentTask());
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: executor, aggregateStore: aggregateStore);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Overwrite"}""", ifMatch: ifMatch);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Equal(7, aggregateStore.Task!.Metadata.Version);
        Assert.Equal(0, executor.MutationCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_FromForeignOrganization_ReturnsSame404AsAbsentTask()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            tokenOrganizationId: ForeignOrganizationId,
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, ForeignOrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Sneak"}""");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
    }

    [Theory]
    [InlineData("cccccccc-cccc-cccc-cccc-cccccccccccc")]
    [InlineData("not-a-uuid")]
    public async global::System.Threading.Tasks.Task PatchTask_WhenAbsentOrMalformed_ReturnsObjectNotVisible(string taskId)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, taskId, """{"title":"Ghost"}"""),
            HttpStatusCode.NotFound,
            "OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_OnArchivedTask_Returns409ObjectArchived()
    {
        await AssertLifecycleConflictAsync(ArchivedTask(), "\"v9\"", "OBJECT_ARCHIVED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_OnTrashedTask_Returns409ObjectDeleted()
    {
        await AssertLifecycleConflictAsync(TrashedTask(), "\"v9\"", "OBJECT_DELETED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_OnCompletedTask_Returns409InvalidStateTransition()
    {
        await AssertLifecycleConflictAsync(CompletedTask(), "\"v8\"", "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithoutUpdatePermission_Returns403()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()),
            grantTaskWrite: false);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Denied"}"""),
            HttpStatusCode.Forbidden,
            "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_UpdatePolicyIsIndependentOfReadAndCreatePolicies()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        var policies = server.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var update = await policies.GetPolicyAsync(TaskPermissionAuthorization.TaskUpdatePolicyName);
        Assert.NotNull(update);
        Assert.NotEqual(TaskPermissionAuthorization.TaskReadPolicyName, TaskPermissionAuthorization.TaskUpdatePolicyName);
        Assert.NotEqual(TaskPermissionAuthorization.TaskCreatePolicyName, TaskPermissionAuthorization.TaskUpdatePolicyName);
        Assert.Equal(
            TaskPermissionAuthorization.TaskUpdateBackingPermissionCode,
            Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(Assert.Single(update.Requirements)).Code);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_ExactReplay_ReturnsStoredResponseWithoutNewMutation()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var first = await PatchTaskAsync(
            client,
            TaskId.ToString("D"),
            """{"priority":"critical","title":"Replay me"}""",
            idempotencyKey: "replay-patch-01");
        var second = await PatchTaskAsync(
            client,
            TaskId.ToString("D"),
            """{"title":"Replay me","priority":"critical"}""",
            idempotencyKey: "replay-patch-01");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("false", first.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("\"v8\"", second.Headers.ETag?.Tag);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, executor.MutationCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithReusedKeyAndDifferentHash_Returns409()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        Assert.Equal(
            HttpStatusCode.OK,
            (await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"One"}""", idempotencyKey: "reuse-patch-01")).StatusCode);
        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Two"}""", idempotencyKey: "reuse-patch-01"),
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithActiveDuplicateRequest_Returns409WithRetryAfter()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor
            {
                Current = CurrentTask(),
                NextDisposition = TaskWriteCommandDisposition.RequestInProgress,
                RetryAfter = TimeSpan.FromSeconds(4),
            },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Busy"}""");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS");
        Assert.Equal("4", response.Headers.GetValues("Retry-After").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("has space")]
    public async global::System.Threading.Tasks.Task PatchTask_WithInvalidIdempotencyKey_Returns400(string? key)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"New"}""", idempotencyKey: key),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithoutToken_Returns401()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = CurrentTask() },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = server.CreateClient();

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Anon"}"""),
            HttpStatusCode.Unauthorized,
            "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WhenExecutorThrows_Returns503WithoutSecrets()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor
            {
                Current = CurrentTask(),
                Throw = new Exception("Npgsql password=supersecret host=10.0.0.1"),
            },
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Fail"}""");
        var text = await response.Content.ReadAsStringAsync();

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
        Assert.DoesNotContain("supersecret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.0.0.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchTask_WithoutUpdateService_Returns503()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeUpdateExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Offline"}"""),
            HttpStatusCode.ServiceUnavailable,
            "INTERNAL_ERROR");
    }

    private static async global::System.Threading.Tasks.Task AssertLifecycleConflictAsync(
        TaskAggregate task,
        string ifMatch,
        string expectedCode)
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeUpdateExecutor { Current = task },
            aggregateStore: new FakeUpdateAggregateStore(task));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await PatchTaskAsync(client, TaskId.ToString("D"), """{"title":"Forbidden"}""", ifMatch: ifMatch),
            HttpStatusCode.Conflict,
            expectedCode);
    }

    private static async Task<HttpResponseMessage> PatchTaskAsync(
        HttpClient client,
        string taskId,
        string body,
        string? ifMatch = "\"v7\"",
        string? idempotencyKey = "patch-key-01",
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), TasksUrl + "/" + taskId)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static TaskAggregate CurrentTask()
    {
        var metadata = SyncableEntityMetadata.Reconstitute(
            TaskId,
            OrganizationId,
            AuthorUserId,
            DateTimeOffset.Parse("2026-08-20T08:00:00Z"),
            UserId,
            DateTimeOffset.Parse("2026-08-23T09:30:00Z"),
            7,
            EntityLifecycleState.Active,
            lifecycleStateBeforeTrash: null,
            deletedAtUtc: null,
            deletedBy: null,
            archivedAtUtc: null);
        return TaskAggregate.Reconstitute(
            metadata,
            "Prepare quarterly review",
            TaskWorkStatus.InProgress,
            completedAtUtc: null,
            completedBy: null,
            TaskPriority.High,
            TaskSchedule.Create(
                DateTimeOffset.Parse("2026-08-24T07:00:00Z"),
                DateTimeOffset.Parse("2026-08-25T15:00:00Z")));
    }

    private static TaskAggregate CompletedTask() => CurrentTask().Complete(UserId, TransitionAt);

    private static TaskAggregate CancelledTask() => CurrentTask().Cancel(UserId, TransitionAt);

    private static TaskAggregate ArchivedTask() => CompletedTask().Archive(UserId, TransitionAt2);

    private static TaskAggregate TrashedTask() => CancelledTask().MoveToTrash(UserId, TransitionAt2);

    private sealed class FakeUpdateAggregateStore : ITaskAggregateStore
    {
        public FakeUpdateAggregateStore(TaskAggregate? task)
        {
            Task = task;
        }

        public TaskAggregate? Task { get; set; }

        public TaskAggregate? Get(Guid taskId, Guid organizationId) =>
            Task is not null && Task.Metadata.Id == taskId && Task.Metadata.OrganizationId == organizationId
                ? Task
                : null;

        public void Add(TaskAggregate task) => Task = task;

        public void Save(TaskAggregate task, int expectedVersion) => Task = task;
    }

    private sealed class FakeUpdateExecutor : ITaskWriteCommandExecutor
    {
        private readonly Dictionary<string, (byte[] Hash, TaskWriteHttpResult Result)> _completed = new(StringComparer.Ordinal);

        public TaskAggregate? Current { get; set; }

        public TaskWriteCommandDisposition? NextDisposition { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public Exception? Throw { get; set; }

        public Action<TaskAggregate>? OnUpdated { get; set; }

        public int MutationCalls { get; private set; }

        public int CompletedCount => _completed.Count;

        public TaskWriteCommand? LastCommand { get; private set; }

        public global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
            TaskWriteCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is not null)
            {
                throw Throw;
            }

            if (NextDisposition == TaskWriteCommandDisposition.RequestInProgress)
            {
                return global::System.Threading.Tasks.Task.FromResult(
                    new TaskWriteCommandExecutionResult(
                        TaskWriteCommandDisposition.RequestInProgress,
                        HttpResult: null,
                        RetryAfter ?? TimeSpan.FromSeconds(1)));
            }

            var scope = string.Join('|', command.OrganizationId, command.ActorUserId, command.OperationId, command.IdempotencyKey);
            if (_completed.TryGetValue(scope, out var stored))
            {
                if (!stored.Hash.SequenceEqual(command.RequestHash))
                {
                    return global::System.Threading.Tasks.Task.FromResult(
                        new TaskWriteCommandExecutionResult(TaskWriteCommandDisposition.IdempotencyKeyReused, null));
                }

                return global::System.Threading.Tasks.Task.FromResult(
                    new TaskWriteCommandExecutionResult(TaskWriteCommandDisposition.Replayed, stored.Result));
            }

            var current = Current;
            if (current is null ||
                current.Metadata.Id != command.TaskId ||
                current.Metadata.OrganizationId != command.OrganizationId)
            {
                throw new KeyNotFoundException("The Task aggregate was not found in the command organization.");
            }
            if (command.ExpectedVersion is not null && current.Metadata.Version != command.ExpectedVersion.Value)
            {
                throw new TaskLifecycleConcurrencyException(
                    command.TaskId,
                    command.ExpectedVersion.Value,
                    current.Metadata.Version);
            }

            var mutation = command.Mutation(current);
            MutationCalls++;
            if (mutation.ChangedFields is null || mutation.ChangedFields.Count > 0)
            {
                Current = mutation.Aggregate;
                OnUpdated?.Invoke(mutation.Aggregate);
            }
            _completed[scope] = (command.RequestHash, mutation.HttpResult);
            return global::System.Threading.Tasks.Task.FromResult(
                new TaskWriteCommandExecutionResult(TaskWriteCommandDisposition.Executed, mutation.HttpResult));
        }
    }
}
