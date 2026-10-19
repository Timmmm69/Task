using System.Net;
using Task.Desktop.Projects;

namespace Task.Desktop.Tests.Projects;

public sealed partial class DesktopProjectsApiClientTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[{}]}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("not json")]
    public async System.Threading.Tasks.Task History_StrictPayloadValidation(string body)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, body)));
        Assert.IsType<DesktopProjectResult<DesktopProjectHistoryPage>.MalformedResponse>(await fixture.Client.GetHistoryAsync(ProjectId, 1));
    }

    [Fact]
    public async System.Threading.Tasks.Task History_CanonicalAuthenticatedReadAndFieldsOnly()
    {
        var eventId = Guid.NewGuid();
        var body = $$"""{"items":[{"id":"{{eventId}}","action":"project.patch","version":3,"actorUserId":null,"occurredAt":"2026-10-19T10:00:00Z","changedFields":["name"]}]}""";
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, body)));
        var page = Assert.IsType<DesktopProjectResult<DesktopProjectHistoryPage>.Succeeded>(await fixture.Client.GetHistoryAsync(ProjectId, 2));
        var row = Assert.Single(page.Value.Items); Assert.Equal(eventId, row.Id); Assert.Equal(3, row.Version);
        Assert.Null(row.ActorUserId); Assert.Equal(new[] { "name" }, row.ChangedFields);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal($"/api/v1/projects/{ProjectId:D}/history?limit=50&page=2", request.Uri.PathAndQuery);
        Assert.Equal("AT_initial", request.Bearer); Assert.Null(request.IfMatch); Assert.Null(request.IdempotencyKey);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.GetHistoryAsync(ProjectId, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.GetHistoryAsync(ProjectId, 10001));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.GetHistoryAsync(ProjectId, 1, 201));
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "NotFound")]
    [InlineData(503, "ServerUnavailable")]
    public async System.Threading.Tasks.Task History_MapsReadFailures(int status, string expected)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)status, "{}")));
        Assert.Equal(expected, (await fixture.Client.GetHistoryAsync(ProjectId, 1)).GetType().Name);
    }
}
