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
    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithValidBody_Returns201ContractAndUsesServerActor()
    {
        var store = new FakeTaskReadStore(Projection);
        var executor = new FakeTaskWriteCommandExecutor { OnCreated = task => store.Add(ToProjection(task)) };
        using var server = CreateServer(store, writeExecutor: executor);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, """{"title":"  Prepare brief  ","priority":"high"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        Assert.Equal("false", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.NotNull(executor.LastCommand);
        Assert.Equal(OrganizationId, executor.LastCommand.OrganizationId);
        Assert.Equal(UserId, executor.LastCommand.ActorUserId);
        Assert.Equal(["title", "priority"], executor.LastCommand.ChangedFields);
        Assert.DoesNotContain("Prepare brief", executor.LastCommand.SafePayloadJson, StringComparison.Ordinal);

        using var document = await ReadJsonAsync(response);
        var created = document.RootElement;
        Assert.Equal(OrganizationId.ToString("D"), created.GetProperty("organizationId").GetString());
        Assert.Equal(UserId.ToString("D"), created.GetProperty("authorUserId").GetString());
        Assert.Equal("Prepare brief", created.GetProperty("title").GetString());
        Assert.Equal("high", created.GetProperty("priority").GetString());
        Assert.Equal("new", created.GetProperty("status").GetString());
        Assert.Equal(1, created.GetProperty("version").GetInt64());

        var get = await client.GetAsync(TasksUrl + "/" + created.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("\"v1\"", get.Headers.ETag?.Tag);
        using var fetched = await ReadJsonAsync(get);
        Assert.Equal("Prepare brief", fetched.RootElement.GetProperty("title").GetString());
        Assert.Equal("high", fetched.RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithoutPriority_DefaultsToNormal()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, """{"title":"Inbox item"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal("normal", document.RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithMatchingAuthor_Succeeds()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, $$"""{"title":"Owned","authorUserId":"{{UserId:D}}"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithForeignAuthor_Returns403()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, $$"""{"title":"Spoof","authorUserId":"{{AuthorUserId:D}}"}""");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("""{"title":"Dated","startAtUtc":"2026-08-26T08:00:00Z","deadlineAt":"2026-08-26T09:00:00Z"}""", HttpStatusCode.Created, null)]
    [InlineData("""{"title":"Dated","startAtUtc":"2026-08-26T10:00:00Z","deadlineAt":"2026-08-26T09:00:00Z"}""", HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED")]
    [InlineData("""{"title":"Dated","startAtUtc":"2026-08-26T08:00:00+00:00"}""", HttpStatusCode.BadRequest, "VALIDATION_FAILED")]
    public async global::System.Threading.Tasks.Task PostTask_ScheduleValidation(
        string body,
        HttpStatusCode status,
        string? code)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, body);

        if (code is null)
        {
            Assert.Equal(status, response.StatusCode);
            return;
        }

        await AssertProblemAsync(response, status, code);
    }

    [Theory]
    [InlineData("""{"title":"A","description":"no"}""")]
    [InlineData("""{"title":"A","status":"new"}""")]
    [InlineData("""{"title":"A","projectId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""")]
    [InlineData("""{"title":"A","unknown":true}""")]
    public async global::System.Threading.Tasks.Task PostTask_UnknownProperty_Returns400(string body)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(await PostTaskAsync(client, body), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("has space")]
    public async global::System.Threading.Tasks.Task PostTask_InvalidIdempotencyKey_Returns400(string? key)
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(await PostTaskAsync(client, """{"title":"A"}""", key), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_MalformedJson_Returns400()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(await PostTaskAsync(client, "{title:"), HttpStatusCode.BadRequest, "MALFORMED_JSON");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_ExactReplay_ReturnsStoredResponseWithoutNewMutation()
    {
        var executor = new FakeTaskWriteCommandExecutor();
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: executor);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var first = await PostTaskAsync(client, """{"priority":"low","title":"Replay"}""", "replay-key-01");
        var second = await PostTaskAsync(client, """{"title":"Replay","priority":"low"}""", "replay-key-01");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("false", first.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("\"v1\"", second.Headers.ETag?.Tag);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, executor.MutationCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_HashMismatch_Returns409()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        Assert.Equal(HttpStatusCode.Created, (await PostTaskAsync(client, """{"title":"One"}""", "reuse-key-01")).StatusCode);
        await AssertProblemAsync(
            await PostTaskAsync(client, """{"title":"Two"}""", "reuse-key-01"),
            HttpStatusCode.Conflict,
            "IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_InProgress_Returns409WithRetryAfter()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeTaskWriteCommandExecutor
            {
                NextDisposition = TaskWriteCommandDisposition.RequestInProgress,
                RetryAfter = TimeSpan.FromSeconds(3),
            });
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, """{"title":"Busy"}""");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS");
        Assert.Equal("3", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithoutWritePermission_Returns403()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeTaskWriteCommandExecutor(),
            grantTaskWrite: false);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(await PostTaskAsync(client, """{"title":"Denied"}"""), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_CreatePolicyIsIndependentOfReadPolicy()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        var policies = server.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var create = await policies.GetPolicyAsync(TaskPermissionAuthorization.TaskCreatePolicyName);
        var read = await policies.GetPolicyAsync(TaskPermissionAuthorization.TaskReadPolicyName);
        Assert.NotNull(create);
        Assert.NotNull(read);
        Assert.NotEqual(TaskPermissionAuthorization.TaskReadPolicyName, TaskPermissionAuthorization.TaskCreatePolicyName);
        Assert.Equal(
            TaskPermissionAuthorization.TaskCreateBackingPermissionCode,
            Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(Assert.Single(create.Requirements)).Code);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithoutToken_Returns401()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = server.CreateClient();

        await AssertProblemAsync(await PostTaskAsync(client, """{"title":"Anon"}"""), HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WhenExecutorThrows_Returns503WithoutSecrets()
    {
        using var server = CreateServer(
            new FakeTaskReadStore(Projection),
            writeExecutor: new FakeTaskWriteCommandExecutor
            {
                Throw = new InvalidOperationException("Npgsql password=supersecret host=10.0.0.1"),
            });
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, """{"title":"Fail"}""");
        var text = await response.Content.ReadAsStringAsync();

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
        Assert.DoesNotContain("supersecret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.0.0.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_WithoutWriteService_Returns503()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        await AssertProblemAsync(await PostTaskAsync(client, """{"title":"Offline"}"""), HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_CancelledRequest_PropagatesCancellation()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PostTaskAsync(client, """{"title":"Cancelled"}""", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_PasswordProperty_IsRejectedWithoutLeak()
    {
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: new FakeTaskWriteCommandExecutor());
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);

        var response = await PostTaskAsync(client, """{"title":"A","password":"supersecret"}""");
        var text = await response.Content.ReadAsStringAsync();

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.DoesNotContain("supersecret", text, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> PostTaskAsync(
        HttpClient client,
        string body,
        string? idempotencyKey = "create-key-01",
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TasksUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static TaskReadProjection ToProjection(TaskAggregate task) =>
        new(
            task.Metadata.Id,
            task.Metadata.OrganizationId,
            task.Metadata.Version,
            task.Metadata.CreatedAtUtc,
            task.Metadata.UpdatedAtUtc,
            task.Title,
            task.Metadata.CreatedBy,
            task.WorkStatus,
            task.Priority,
            task.Schedule.StartsAtUtc,
            task.Schedule.DeadlineUtc);

    private sealed class FakeTaskWriteCommandExecutor : ITaskWriteCommandExecutor
    {
        private readonly Dictionary<string, (byte[] Hash, TaskWriteHttpResult Result)> _completed = new(StringComparer.Ordinal);

        public TaskWriteCommandDisposition? NextDisposition { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public Exception? Throw { get; set; }

        public Action<TaskAggregate>? OnCreated { get; set; }

        public int MutationCalls { get; private set; }

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

            var mutation = command.Mutation(null);
            MutationCalls++;
            OnCreated?.Invoke(mutation.Aggregate);
            _completed[scope] = (command.RequestHash, mutation.HttpResult);
            return global::System.Threading.Tasks.Task.FromResult(
                new TaskWriteCommandExecutionResult(TaskWriteCommandDisposition.Executed, mutation.HttpResult));
        }
    }
}
