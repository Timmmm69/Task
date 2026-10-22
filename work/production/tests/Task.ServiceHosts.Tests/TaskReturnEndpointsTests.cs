using System.Net;
using Task.Application;
using Task.Application.Security;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task TaskWrite_CreateAndPatchHandleSessionRevokedDuringWrite(bool patch)
    {
        var original = TaskInStatus(TaskWorkStatus.Review);
        var executor = new FakeUpdateExecutor
        { Current = original, Throw = new TaskWriteSessionException(SessionRequestState.SessionRevoked) };
        using var server = CreateServer(new FakeTaskReadStore(ToProjection(original)),
            writeExecutor: executor, aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        using var request = new HttpRequestMessage(patch ? HttpMethod.Patch : HttpMethod.Post,
            patch ? $"/api/v1/tasks/{original.Metadata.Id:D}" : "/api/v1/tasks")
        { Content = new StringContent("{\"title\":\"Session guard\"}", System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", "audit-session-guard-01");
        if (patch) request.Headers.TryAddWithoutValidation("If-Match", "\"v7\"");
        await AssertProblemAsync(await client.SendAsync(request), HttpStatusCode.Unauthorized, "SESSION_REVOKED");
        Assert.Same(original, executor.Current);
        Assert.Equal(0, executor.MutationCalls);
    }

    [Theory]
    [InlineData(SessionRequestState.SessionRevoked, 401, "SESSION_REVOKED")]
    [InlineData(SessionRequestState.SessionExpired, 401, "SESSION_EXPIRED")]
    [InlineData(SessionRequestState.VersionMismatch, 401, "SESSION_EXPIRED")]
    [InlineData(SessionRequestState.AccountBlocked, 423, "ACCOUNT_BLOCKED")]
    public async System.Threading.Tasks.Task ReturnTask_SessionInvalidatedAfterAuthentication(
        SessionRequestState state, int expectedStatus, string code)
    {
        var original = TaskInStatus(TaskWorkStatus.Review);
        var executor = new FakeUpdateExecutor { Current = original, Throw = new TaskWriteSessionException(state) };
        using var server = CreateServer(new FakeTaskReadStore(ToProjection(original)),
            writeExecutor: executor, aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        await AssertProblemAsync(await TransitionTaskAsync(client, TaskWorkStatus.InProgress, 7), (HttpStatusCode)expectedStatus, code);
        Assert.Same(original, executor.Current);
        Assert.Equal(0, executor.MutationCalls);
    }

    [Theory]
    [InlineData("{\"targetStatus\":\"in_progress\"}")]
    [InlineData("{\"targetStatus\":\"in_progress\",\"reason\":null}")]
    [InlineData("{\"targetStatus\":\"in_progress\",\"reason\":\"Please revise\"}")]
    public async System.Threading.Tasks.Task ReturnTask_OptionalReasonAndExactReplay(string body)
    {
        var original = TaskInStatus(TaskWorkStatus.Review);
        var executor = new FakeUpdateExecutor { Current = original };
        using var server = CreateServer(new FakeTaskReadStore(ToProjection(original)),
            writeExecutor: executor, aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var first = await TransitionTaskAsync(client, TaskWorkStatus.InProgress, 7, body: body);
        var replay = await TransitionTaskAsync(client, TaskWorkStatus.InProgress, 7, body: body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("\"v8\"", first.Headers.ETag?.Tag);
        Assert.Equal(first.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(TaskWorkStatus.InProgress, executor.Current!.WorkStatus);
        Assert.Equal(8, executor.Current.Metadata.Version);
        Assert.Equal(1, executor.MutationCalls);
        Assert.Equal(1, executor.CompletedCount);

        await AssertProblemAsync(await TransitionTaskAsync(client, TaskWorkStatus.InProgress, 7,
            body: """{"targetStatus":"in_progress","reason":"Changed intent"}"""),
            HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");
        Assert.Equal(1, executor.MutationCalls);
    }

    [Theory]
    [InlineData(null, "return-header-01", 428, "PRECONDITION_REQUIRED")]
    [InlineData("W/\"v7\"", "return-header-01", 400, "VALIDATION_FAILED")]
    [InlineData("\"v7\"", null, 400, "VALIDATION_FAILED")]
    [InlineData("\"v7\"", "short", 400, "VALIDATION_FAILED")]
    public async System.Threading.Tasks.Task ReturnTask_RequiresVersionAndIdempotencyHeaders(
        string? ifMatch, string? key, int status, string code)
    {
        var original = TaskInStatus(TaskWorkStatus.Review);
        var executor = new FakeUpdateExecutor { Current = original };
        using var server = CreateServer(new FakeTaskReadStore(ToProjection(original)),
            writeExecutor: executor, aggregateStore: new FakeUpdateAggregateStore(original));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        await AssertProblemAsync(await TransitionTaskAsync(client, TaskWorkStatus.InProgress, 7,
            ifMatch: ifMatch, idempotencyKey: key), (HttpStatusCode)status, code);
        Assert.Same(original, executor.Current);
        Assert.Equal(0, executor.MutationCalls);
    }

    [Theory]
    [InlineData("denied", HttpStatusCode.Forbidden, "FORBIDDEN")]
    [InlineData("stale", HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT")]
    [InlineData("foreign", HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE")]
    [InlineData("missing", HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE")]
    [InlineData("archived", HttpStatusCode.Conflict, "OBJECT_ARCHIVED")]
    [InlineData("trashed", HttpStatusCode.Conflict, "OBJECT_DELETED")]
    [InlineData("revoked", HttpStatusCode.Unauthorized, "SESSION_REVOKED")]
    [InlineData("expired", HttpStatusCode.Unauthorized, "SESSION_EXPIRED")]
    [InlineData("anonymous", HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED")]
    public async System.Threading.Tasks.Task ReturnTask_RejectedWithoutMutation(
        string scenario, HttpStatusCode expectedStatus, string expectedCode)
    {
        var original = scenario switch
        {
            "archived" => ArchivedTask(),
            "trashed" => TrashedTask(),
            _ => TaskInStatus(TaskWorkStatus.Review),
        };
        var executor = new FakeUpdateExecutor { Current = scenario == "missing" ? null : original };
        var sessionState = scenario switch
        {
            "revoked" => SessionRequestState.SessionRevoked,
            "expired" => SessionRequestState.SessionExpired,
            _ => SessionRequestState.Active,
        };
        using var server = CreateServer(new FakeTaskReadStore(ToProjection(original)),
            sessionState: sessionState,
            tokenOrganizationId: scenario == "foreign" ? ForeignOrganizationId : null,
            writeExecutor: executor, aggregateStore: new FakeUpdateAggregateStore(original),
            grantTaskWrite: scenario != "denied");
        using var client = scenario == "anonymous" ? server.CreateClient() :
            await CreateAuthenticatedClientAsync(server, scenario == "foreign" ? ForeignOrganizationId : OrganizationId);

        await AssertProblemAsync(await TransitionTaskAsync(client, TaskWorkStatus.InProgress,
            scenario == "stale" ? original.Metadata.Version - 1 : original.Metadata.Version),
            expectedStatus, expectedCode);

        Assert.Equal(0, executor.MutationCalls);
        Assert.Equal(0, executor.CompletedCount);
        Assert.Same(scenario == "missing" ? null : original, executor.Current);
    }
}
