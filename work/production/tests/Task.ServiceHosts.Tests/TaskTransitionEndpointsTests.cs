using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Task.Api.Security;
using Task.Application;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    public static TheoryData<TaskWorkStatus, TaskWorkStatus> AllowedTransitions => new()
    {
        { TaskWorkStatus.New, TaskWorkStatus.InProgress },
        { TaskWorkStatus.InProgress, TaskWorkStatus.Review },
        { TaskWorkStatus.New, TaskWorkStatus.Completed },
        { TaskWorkStatus.InProgress, TaskWorkStatus.Completed },
        { TaskWorkStatus.Review, TaskWorkStatus.Completed },
        { TaskWorkStatus.New, TaskWorkStatus.Cancelled },
        { TaskWorkStatus.InProgress, TaskWorkStatus.Cancelled },
        { TaskWorkStatus.Review, TaskWorkStatus.Cancelled },
    };

    public static TheoryData<TaskWorkStatus, TaskWorkStatus> ForbiddenTransitions
    {
        get
        {
            var data = new TheoryData<TaskWorkStatus, TaskWorkStatus>();
            foreach (var from in Enum.GetValues<TaskWorkStatus>())
                foreach (var target in Enum.GetValues<TaskWorkStatus>())
                {
                    if (!AllowedTransitions.Any(item => item[0].Equals(from) && item[1].Equals(target)))
                    {
                        data.Add(from, target);
                    }
                }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public async global::System.Threading.Tasks.Task TransitionTask_AllAllowedTransitionsSucceed(
        TaskWorkStatus from,
        TaskWorkStatus target)
    {
        var original = TaskInStatus(from);
        var readStore = new FakeTaskReadStore(ToProjection(original));
        var executor = new FakeUpdateExecutor { Current = original, OnUpdated = task => readStore.Add(ToProjection(task)) };
        using var server = CreateServer(
            readStore,
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await TransitionTaskAsync(client, target, original.Metadata.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"\"v{original.Metadata.Version + 1}\"", response.Headers.ETag?.Tag);
        Assert.Equal("false", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(target, executor.Current!.WorkStatus);
        Assert.Equal(original.Metadata.Version + 1, executor.Current.Metadata.Version);
        if (target == TaskWorkStatus.Completed)
        {
            Assert.NotNull(executor.Current.CompletedAtUtc);
            Assert.Equal(UserId, executor.Current.CompletedBy);
        }
        else
        {
            Assert.Null(executor.Current.CompletedAtUtc);
            Assert.Null(executor.Current.CompletedBy);
        }

        Assert.Equal(TaskStatusTransitionCommandService.AuditAction, executor.LastCommand!.AuditAction);
        Assert.Equal(TaskStatusTransitionCommandService.EventType, executor.LastCommand.EventType);
        var mutation = executor.LastCommand.Mutation(original);
        using var payload = JsonDocument.Parse(mutation.SafePayloadJson!);
        Assert.Equal(ToStatusValue(from), payload.RootElement.GetProperty("fromStatus").GetString());
        Assert.Equal(ToStatusValue(target), payload.RootElement.GetProperty("targetStatus").GetString());
        Assert.Equal(original.Metadata.Version + 1, payload.RootElement.GetProperty("aggregateVersion").GetInt32());
        Assert.Equal(UserId, payload.RootElement.GetProperty("actorId").GetGuid());
        Assert.False(payload.RootElement.TryGetProperty("reason", out _));

        var get = await client.GetAsync(TasksUrl + "/" + TaskId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var getJson = await ReadJsonAsync(get);
        Assert.Equal(ToStatusValue(target), getJson.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public async global::System.Threading.Tasks.Task TransitionTask_AllOtherTransitionsReturn409(
        TaskWorkStatus from,
        TaskWorkStatus target)
    {
        var original = TaskInStatus(from);
        var executor = new FakeUpdateExecutor { Current = original };
        using var server = CreateServer(
            new FakeTaskReadStore(ToProjection(original)),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await TransitionTaskAsync(client, target, original.Metadata.Version),
            HttpStatusCode.Conflict,
            "INVALID_STATE_TRANSITION");
        Assert.Same(original, executor.Current);
    }

    [Theory]
    [InlineData("{}", HttpStatusCode.BadRequest)]
    [InlineData("{\"targetStatus\":\"unknown\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"targetStatus\":\"review\",\"extra\":1}", HttpStatusCode.BadRequest)]
    [InlineData("{\"targetStatus\":\"review\",\"reason\":1}", HttpStatusCode.BadRequest)]
    [InlineData("[]", HttpStatusCode.BadRequest)]
    [InlineData("{", HttpStatusCode.BadRequest)]
    public async global::System.Threading.Tasks.Task TransitionTask_InvalidBodyIsRejected(
        string body,
        HttpStatusCode status)
    {
        var task = TaskInStatus(TaskWorkStatus.InProgress);
        using var server = CreateTransitionServer(task);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        await AssertProblemAsync(
            await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, body: body),
            status,
            body == "{" ? "MALFORMED_JSON" : "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransitionTask_ValidatesReasonAndHeaders()
    {
        var task = TaskInStatus(TaskWorkStatus.InProgress);
        using var server = CreateTransitionServer(task);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(
            await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, ifMatch: null),
            (HttpStatusCode)428,
            "PRECONDITION_REQUIRED");
        await AssertProblemAsync(
            await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, ifMatch: "W/\"v7\""),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
        await AssertProblemAsync(
            await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, idempotencyKey: "short"),
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
        await AssertProblemAsync(
            await TransitionTaskAsync(
                client,
                TaskWorkStatus.Review,
                task.Metadata.Version,
                body: JsonSerializer.Serialize(new { targetStatus = "review", reason = new string('x', 2001) })),
            HttpStatusCode.UnprocessableEntity,
            "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransitionTask_StaleForeignArchivedTrashedAndDeniedAreStable()
    {
        var task = TaskInStatus(TaskWorkStatus.InProgress);
        using (var staleServer = CreateTransitionServer(task))
        using (var staleClient = await CreateAuthenticatedClientAsync(staleServer, OrganizationId))
        {
            await AssertProblemAsync(
                await TransitionTaskAsync(staleClient, TaskWorkStatus.Review, task.Metadata.Version - 1),
                HttpStatusCode.PreconditionFailed,
                "VERSION_CONFLICT");
        }

        using (var foreignServer = CreateTransitionServer(task, tokenOrganizationId: ForeignOrganizationId))
        using (var foreignClient = await CreateAuthenticatedClientAsync(foreignServer, ForeignOrganizationId))
        {
            await AssertProblemAsync(
                await TransitionTaskAsync(foreignClient, TaskWorkStatus.Review, task.Metadata.Version),
                HttpStatusCode.NotFound,
                "OBJECT_NOT_VISIBLE");
        }

        await AssertTransitionConflictAsync(ArchivedTask(), "OBJECT_ARCHIVED");
        await AssertTransitionConflictAsync(TrashedTask(), "OBJECT_DELETED");

        using var deniedServer = CreateTransitionServer(task, grantTaskRead: false);
        using var deniedClient = await CreateAuthenticatedClientAsync(deniedServer, OrganizationId);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await TransitionTaskAsync(deniedClient, TaskWorkStatus.Review, task.Metadata.Version)).StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransitionTask_ReplaysAndRejectsKeyReuse()
    {
        var task = TaskInStatus(TaskWorkStatus.InProgress);
        var executor = new FakeUpdateExecutor { Current = task };
        using var server = CreateServer(
            new FakeTaskReadStore(ToProjection(task)),
            writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(task));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var first = await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, idempotencyKey: "transition-replay-01");
        var replay = await TransitionTaskAsync(client, TaskWorkStatus.Review, task.Metadata.Version, idempotencyKey: "transition-replay-01");
        var collision = await TransitionTaskAsync(client, TaskWorkStatus.Completed, task.Metadata.Version, idempotencyKey: "transition-replay-01");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        await AssertProblemAsync(collision, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");
        Assert.Equal(1, executor.MutationCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransitionPolicy_IsIndependentAndBackedByTaskManage()
    {
        using var server = CreateTransitionServer(TaskInStatus(TaskWorkStatus.InProgress));
        var policies = server.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policies.GetPolicyAsync(TaskPermissionAuthorization.TaskChangeStatusPolicyName);
        Assert.NotEqual(TaskPermissionAuthorization.TaskReadPolicyName, TaskPermissionAuthorization.TaskChangeStatusPolicyName);
        Assert.NotEqual(TaskPermissionAuthorization.TaskUpdatePolicyName, TaskPermissionAuthorization.TaskChangeStatusPolicyName);
        Assert.Equal(
            TaskPermissionAuthorization.TaskChangeStatusBackingPermissionCode,
            Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(Assert.Single(policy!.Requirements)).Code);
    }

    private static TestServer CreateTransitionServer(
        TaskAggregate task,
        bool grantTaskRead = true,
        Guid? tokenOrganizationId = null) =>
        CreateServer(
            new FakeTaskReadStore(ToProjection(task)),
            grantTaskRead,
            tokenOrganizationId: tokenOrganizationId,
            writeExecutor: new FakeUpdateExecutor { Current = task },
            aggregateStore: new FakeUpdateAggregateStore(task));

    private static async global::System.Threading.Tasks.Task AssertTransitionConflictAsync(
        TaskAggregate task,
        string expectedCode)
    {
        using var server = CreateTransitionServer(task);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        await AssertProblemAsync(
            await TransitionTaskAsync(client, TaskWorkStatus.Completed, task.Metadata.Version),
            HttpStatusCode.Conflict,
            expectedCode);
    }

    private static async Task<HttpResponseMessage> TransitionTaskAsync(
        HttpClient client,
        TaskWorkStatus target,
        int version,
        string? ifMatch = "default",
        string? idempotencyKey = "transition-key-01",
        string? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TasksUrl + "/" + TaskId.ToString("D") + "/transition")
        {
            Content = new StringContent(
                body ?? JsonSerializer.Serialize(new { targetStatus = ToStatusValue(target), reason = (string?)null }),
                Encoding.UTF8,
                "application/json"),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch == "default" ? $"\"v{version}\"" : ifMatch);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static TaskAggregate TaskInStatus(TaskWorkStatus status)
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
            null,
            null,
            null,
            null);
        return TaskAggregate.Reconstitute(
            metadata,
            "Prepare quarterly review",
            status,
            status == TaskWorkStatus.Completed ? DateTimeOffset.Parse("2026-08-22T09:30:00Z") : null,
            status == TaskWorkStatus.Completed ? UserId : null,
            TaskPriority.High,
            TaskSchedule.Create(null, null));
    }

    private static string ToStatusValue(TaskWorkStatus status) => status switch
    {
        TaskWorkStatus.New => "new",
        TaskWorkStatus.InProgress => "in_progress",
        TaskWorkStatus.Review => "review",
        TaskWorkStatus.Completed => "completed",
        TaskWorkStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
