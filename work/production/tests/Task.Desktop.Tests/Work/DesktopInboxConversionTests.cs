using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static string InboxJson() => $$"""{"id":"{{ItemId}}","organizationId":"{{OrganizationId}}","version":1,"ownerUserId":"{{UserId}}","itemType":"task","title":"Inbox title","status":"unprocessed","content":null}""";
    private static JsonObject ConvertedTask() => JsonNode.Parse($$"""{"id":"{{LocationId}}","organizationId":"{{OrganizationId}}","version":1,"authorUserId":"{{UserId}}","title":"Inbox title","status":"new","priority":"normal","assigneeIds":[],"watcherIds":[],"startAtUtc":null,"deadlineAt":null,"createdAt":"2026-10-15T00:00:00Z","updatedAt":"2026-10-15T00:00:00Z"}""")!.AsObject();
    private static HttpResponseMessage InboxWrite(string body, int version = 1, bool taskPatch = false)
    {
        var response = Json(taskPatch ? HttpStatusCode.OK : HttpStatusCode.Created, body);
        response.Headers.ETag = new($"\"v{version}\""); response.Headers.Add("Idempotency-Replayed", "false"); return response;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Inbox_CanonicalTitleOnlyAndOptionalDeadlineCloseSourceAndSurviveReload(bool deadline)
    {
        var converted = false; var task = ConvertedTask();
        await using var fixture = await Fixture.CreateAsync(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/options")) return Json(HttpStatusCode.OK, "{}");
            if (path.EndsWith("/inbox-items")) return Json(HttpStatusCode.OK, "{\"items\":[" + (converted ? "" : InboxJson()) + "],\"nextCursor\":null}");
            if (request.Method == HttpMethod.Patch && path.Contains("/inbox-items/"))
                return InboxWrite(InboxJson().Replace("\"version\":1", "\"version\":2"), 2, true);
            if (path.EndsWith("/convert-to-task"))
            {
                Assert.Equal("{\"expectedInboxVersion\":2}", await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("\"v2\"", request.Headers.IfMatch.Single().ToString());
                converted = true; return InboxWrite(task.ToJsonString());
            }
            if (request.Method == HttpMethod.Patch)
            {
                var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                task["deadlineAt"] = patch["deadlineAt"]!.DeepClone(); task["version"] = 2;
                return InboxWrite(task.ToJsonString(), 2, true);
            }
            return Json(HttpStatusCode.OK, task.ToJsonString());
        });
        using var model = new InboxViewModel(fixture.Inbox, ["Inbox.ReadOwn", "Inbox.ManageOwn", "Task.Read", "Task.Update"]);
        await model.ActivateAsync(); await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
        if (deadline) model.Conversion!.DeadlineText = "21.10.2026 14:30";
        var editor = model.Conversion;
        model.Deactivate();
        await model.ActivateAsync();
        Assert.Same(editor, model.Conversion);
        Assert.True(model.CanSaveConversion);
        await model.SaveConversionCommand.ExecuteAsync();
        Assert.Null(model.Conversion); Assert.True(model.ShowInboxZero);
        Assert.Single(fixture.Requests, r => r.Uri.AbsolutePath.EndsWith("/convert-to-task"));
        Assert.Equal(deadline, task["deadlineAt"] is not null);
        Assert.Equal(deadline ? 1 : 0, fixture.Requests.Count(r => r.Method == HttpMethod.Patch && r.Uri.AbsolutePath.Contains("/tasks/")));
        using var reopened = new InboxViewModel(fixture.Inbox, ["Inbox.ReadOwn", "Inbox.ManageOwn"]);
        await reopened.ActivateAsync(); Assert.True(reopened.ShowInboxZero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Inbox_LostResponseRetriesSameConversionOrTaskPatchWithoutDuplicates(bool loseTaskPatch)
    {
        var converted = false; var attempts = 0; var patches = 0; var task = ConvertedTask();
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/options")) return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{}"));
            if (path.EndsWith("/inbox-items")) return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[" + (converted ? "" : InboxJson()) + "]}"));
            if (request.Method == HttpMethod.Patch && path.Contains("/inbox-items/"))
                return System.Threading.Tasks.Task.FromResult(InboxWrite(InboxJson().Replace("\"version\":1", "\"version\":2"), 2, true));
            if (path.EndsWith("/convert-to-task"))
            {
                converted = true;
                if (attempts++ == 0 && !loseTaskPatch) throw new HttpRequestException("Lost conversion response");
                return System.Threading.Tasks.Task.FromResult(InboxWrite(task.ToJsonString()));
            }
            if (request.Method == HttpMethod.Patch)
            {
                if (patches++ == 0) throw new HttpRequestException("Lost task patch response");
                task["title"] = "Edited title"; task["version"] = 2;
                return System.Threading.Tasks.Task.FromResult(InboxWrite(task.ToJsonString(), 2, true));
            }
            return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, task.ToJsonString()));
        });
        using var model = new InboxViewModel(fixture.Inbox, ["Inbox.ReadOwn", "Inbox.ManageOwn", "Task.Read", "Task.Update"]);
        await model.ActivateAsync(); await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
        var editor = model.Conversion!;
        if (loseTaskPatch) editor.Title = "Edited title";
        await model.SaveConversionCommand.ExecuteAsync();
        Assert.Same(editor, model.Conversion);
        var revision = editor.Revision;
        var version = editor.SourceVersion;
        model.Deactivate();
        await model.ActivateAsync();
        Assert.Same(editor, model.Conversion);
        Assert.Equal(revision, editor.Revision);
        Assert.Equal(version, editor.SourceVersion);
        model.UpdateConnectivity(true);
        await model.SaveConversionCommand.ExecuteAsync();
        Assert.Null(model.Conversion); Assert.True(model.ShowInboxZero);
        var writes = fixture.Requests.Where(r => loseTaskPatch ? r.Method == HttpMethod.Patch && r.Uri.AbsolutePath.Contains("/tasks/") : r.Uri.AbsolutePath.EndsWith("/convert-to-task")).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
        Assert.Equal(writes[0].Body, writes[1].Body);
        Assert.Equal(loseTaskPatch ? 1 : 2, attempts);
    }

    [Fact]
    public async System.Threading.Tasks.Task Inbox_InvalidRequiredTitleOrReadOnlyCannotConvert()
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath.EndsWith("/options") ? "{}" : "{\"items\":[" + InboxJson() + "]}")));
        using var model = new InboxViewModel(fixture.Inbox, ["Inbox.ReadOwn", "Inbox.ManageOwn"]);
        await model.ActivateAsync(); await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
        model.Conversion!.Title = ""; Assert.False(model.CanSaveConversion);
        await model.SaveConversionCommand.ExecuteAsync();
        model.Conversion.Title = "Valid"; model.UpdateConnectivity(false);
        Assert.False(model.CanSaveConversion);
        model.UpdateConnectivity(true); model.UpdateCapabilities(["Inbox.ReadOwn"]);
        Assert.False(model.CanSaveConversion);
        Assert.DoesNotContain(fixture.Requests, r => r.Method != HttpMethod.Get);
    }
}
