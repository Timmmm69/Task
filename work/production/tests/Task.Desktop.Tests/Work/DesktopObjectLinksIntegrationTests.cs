using System.Net;
using System.Net.Http;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Theory]
    [InlineData(409, "CONFLICT", "существует")]
    [InlineData(409, "INVALID_STATE_TRANSITION", "Состояние")]
    [InlineData(422, "VALIDATION_FAILED", "недопустим")]
    [InlineData(400, "VALIDATION_FAILED", "недопустим")]
    public async AsyncTask LinkStableCodesUseSafeMessagesWithoutServerDetails(int status, string code, string message)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json((HttpStatusCode)status,
            $$"""{"code":"{{code}}","title":"PRIVATE_SERVER_DETAILS"}""")));
        var failure = Assert.IsType<LinkResult.ValidationFailure>(await fixture.Client.AddLinkAsync(Guid.NewGuid(), 4, Guid.NewGuid(), "task_file"));
        Assert.Contains(message, failure.Message); Assert.DoesNotContain("PRIVATE", failure.Message);
        Assert.Single(fixture.Requests);
    }
    [Theory]
    [InlineData(412, "VERSION_CONFLICT", "version")][InlineData(403, "FORBIDDEN", "rights")]
    [InlineData(404, "OBJECT_NOT_VISIBLE", "missing")][InlineData(503, "UNAVAILABLE", "offline")]
    public async AsyncTask LinkFailuresDoNotReadBackOrReportSuccess(int status, string code, string kind)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json((HttpStatusCode)status, $$"""{"code":"{{code}}"}""")));
        var result = await fixture.Client.AddLinkAsync(Guid.NewGuid(), 4, Guid.NewGuid(), "task_file");
        Assert.True(kind switch { "version" => result is LinkResult.Conflict, "rights" => result is LinkResult.Forbidden,
            "missing" => result is LinkResult.NotFound, _ => result is LinkResult.ServerUnavailable });
        Assert.Single(fixture.Requests);
    }
    [Fact]
    public async AsyncTask SuccessfulPostWithFailedReadBackIsNotConfirmedSuccess()
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => AsyncTask.FromResult(request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, "{}") : Json(HttpStatusCode.ServiceUnavailable, "{}")));
        Assert.IsType<LinkResult.ServerUnavailable>(await fixture.Client.AddLinkAsync(Guid.NewGuid(), 4, Guid.NewGuid(), "task_file"));
        Assert.Equal(2, fixture.Requests.Count); Assert.Equal(HttpMethod.Get, fixture.Requests[1].Method);
    }
}
