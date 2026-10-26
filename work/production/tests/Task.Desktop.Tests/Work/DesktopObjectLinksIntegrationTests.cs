using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static string LinksPage(Guid source, int count, string? cursor) => new JsonObject
    {
        ["items"] = new JsonArray(Enumerable.Range(0, count).Select(_ => (JsonNode)new JsonObject
        {
            ["id"] = Guid.NewGuid(),
            ["sourceObjectId"] = source,
            ["targetObjectId"] = Guid.NewGuid(),
            ["linkType"] = "related",
        }).ToArray()),
        ["hasMore"] = cursor is not null,
        ["nextCursor"] = cursor,
    }.ToJsonString();

    [Fact]
    public async AsyncTask LinksReadAllPagesBeyondTwoHundredAndPreserveVersion()
    {
        var source = Guid.NewGuid(); var page = 0;
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            page++;
            Assert.Contains("limit=200", request.RequestUri!.Query);
            if (page > 1) Assert.Contains("cursor=page" + page, request.RequestUri.Query);
            var response = Json(HttpStatusCode.OK, LinksPage(source, page < 3 ? 200 : 51, page < 3 ? "page" + (page + 1) : null));
            response.Headers.ETag = new("\"v7\""); return AsyncTask.FromResult(response);
        });
        var result = Assert.IsType<LinkResult.Succeeded>(await fixture.Client.GetLinksAsync(source));
        Assert.Equal(451, result.Value.Items.Count); Assert.Equal(7, result.Value.SourceVersion);
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("cursor")]
    [InlineData("duplicate")]
    [InlineData("forbidden")]
    [InlineData("expired")]
    public async AsyncTask LaterLinkPageFailureNeverConfirmsPartialResults(string fault)
    {
        var source = Guid.NewGuid(); var page = 0; var first = LinksPage(source, 1, "page2");
        await using var fixture = await Fixture.CreateAsync((_, _) =>
        {
            page++;
            if (page == 2 && fault is "forbidden" or "expired")
                return AsyncTask.FromResult(Json(fault == "forbidden" ? HttpStatusCode.Forbidden : HttpStatusCode.Gone, "{}"));
            var response = Json(HttpStatusCode.OK, page == 1 || fault is "cursor" or "duplicate"
                ? first : LinksPage(source, 1, null));
            if (page == 2 && fault == "duplicate")
            {
                var body = JsonNode.Parse(first)!; body["hasMore"] = false; body["nextCursor"] = null;
                response = Json(HttpStatusCode.OK, body.ToJsonString());
            }
            response.Headers.ETag = new(page == 2 && fault == "version" ? "\"v8\"" : "\"v7\"");
            return AsyncTask.FromResult(response);
        });
        var result = await fixture.Client.GetLinksAsync(source);
        Assert.True(fault switch
        {
            "version" or "expired" => result is LinkResult.Conflict,
            "forbidden" => result is LinkResult.Forbidden,
            _ => result is LinkResult.MalformedResponse,
        });
        Assert.Equal(2, fixture.Requests.Count);
    }

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
    [InlineData(412, "VERSION_CONFLICT", "version")]
    [InlineData(403, "FORBIDDEN", "rights")]
    [InlineData(404, "OBJECT_NOT_VISIBLE", "missing")]
    [InlineData(503, "UNAVAILABLE", "offline")]
    public async AsyncTask LinkFailuresDoNotReadBackOrReportSuccess(int status, string code, string kind)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => AsyncTask.FromResult(Json((HttpStatusCode)status, $$"""{"code":"{{code}}"}""")));
        var result = await fixture.Client.AddLinkAsync(Guid.NewGuid(), 4, Guid.NewGuid(), "task_file");
        Assert.True(kind switch
        {
            "version" => result is LinkResult.Conflict,
            "rights" => result is LinkResult.Forbidden,
            "missing" => result is LinkResult.NotFound,
            _ => result is LinkResult.ServerUnavailable
        });
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
