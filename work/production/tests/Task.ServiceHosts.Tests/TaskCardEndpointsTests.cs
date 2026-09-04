using System.Net;
using Task.Domain;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    [Fact]
    public async System.Threading.Tasks.Task TaskCard_CreateAndReadPreserveDescriptionAndDate()
    {
        var store = new FakeTaskReadStore(Projection);
        var executor = new FakeTaskWriteCommandExecutor { OnCreated = task => store.Add(ToProjection(task)) };
        using var server = CreateServer(store, writeExecutor: executor);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await PostTaskAsync(client, """{"title":"Договор","description":"Согласовать условия","scheduledDate":"2026-09-05","plannedDurationMinutes":45}""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await ReadJsonAsync(response);
        var get = await client.GetAsync(TasksUrl + "/" + created.RootElement.GetProperty("id").GetString());
        using var loaded = await ReadJsonAsync(get);
        Assert.Equal("Согласовать условия", loaded.RootElement.GetProperty("description").GetString());
        Assert.Equal("2026-09-05", loaded.RootElement.GetProperty("scheduledDate").GetString());
        Assert.Equal(45, loaded.RootElement.GetProperty("plannedDurationMinutes").GetInt32());
    }

    [Theory]
    [InlineData("{\"plannedDurationMinutes\":10081}")]
    [InlineData("{\"assigneeIds\":null}")]
    [InlineData("{\"startTimeLocal\":\"09:00:00\"}")]
    [InlineData("{\"description\":false}")]
    public async System.Threading.Tasks.Task TaskCard_InvalidPatchDoesNotMutate(string body)
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await PatchTaskAsync(client, TaskId.ToString("D"), body);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        Assert.Equal(7, executor.Current!.Metadata.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task TaskCard_DescriptionPatchPreservesExistingSchedule()
    {
        var executor = new FakeUpdateExecutor { Current = CurrentTask() };
        var original = executor.Current.Schedule;
        using var server = CreateServer(new FakeTaskReadStore(Projection), writeExecutor: executor,
            aggregateStore: new FakeUpdateAggregateStore(CurrentTask()));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await PatchTaskAsync(client, TaskId.ToString("D"), """{"description":"Готово к проверке","plannedDurationMinutes":30}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Готово к проверке", executor.Current!.Content.Description);
        Assert.Equal(original, executor.Current.Schedule);
        Assert.Equal(8, executor.Current.Metadata.Version);
    }
}
