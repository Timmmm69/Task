using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    internal static string InteractionJson(Guid id, Guid target, int version = 7, string participants = "[]") =>
        $$"""{"id":"{{id}}","version":{{version}},"counterpartyObjectId":"{{target}}","interactionType":"call","occurredAt":"2026-10-18T10:12:34.123Z","subject":"Звонок","details":"Обсуждение","nextStep":"Подготовить","nextStepDueAt":"2026-10-19T10:00:00Z","participantObjectIds":{{participants}},"lifecycleState":"active"}""";

    [Fact]
    public async System.Threading.Tasks.Task InteractionsPageUsesOnlySupportedBoundedQueryAndCursor()
    {
        var id = Guid.NewGuid(); var target = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK,
            "{\"items\":[" + InteractionJson(id, target) + "],\"hasMore\":true,\"nextCursor\":\"next+page\"}")));
        var page = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>.Succeeded>(await fixture.Client.GetInteractionsAsync(null));
        Assert.Equal(target, Assert.Single(page.Value).CounterpartyObjectId); Assert.Equal("next+page", page.NextCursor);
        await fixture.Client.GetInteractionsAsync(page.NextCursor);
        Assert.Equal("?limit=50", fixture.Requests[0].Uri.Query);
        Assert.Equal("?limit=50&cursor=next%2Bpage", fixture.Requests[1].Uri.Query);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateUsesStableKeyUtcAndPatchOmitsParticipantsAndCounterparty()
    {
        var id = Guid.NewGuid(); var target = Guid.NewGuid(); var participant = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            var response = Json(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK,
                InteractionJson(id, target, 8, $"[\"{participant}\"]"));
            response.Headers.TryAddWithoutValidation("ETag", "\"v8\"");
            return System.Threading.Tasks.Task.FromResult(response);
        });
        var draft = new DesktopInteraction(Guid.Empty, 0, target, "meeting", new(2026, 10, 18, 15, 0, 0, TimeSpan.FromHours(3)), "Тема", null, null, null, [participant]);
        var key = Guid.NewGuid().ToString("D");
        Assert.IsType<DesktopWorkResult<DesktopInteraction>.Succeeded>(await fixture.Client.SaveInteractionAsync(draft, key));
        Assert.IsType<DesktopWorkResult<DesktopInteraction>.Succeeded>(await fixture.Client.SaveInteractionAsync(draft, key));
        Assert.All(fixture.Requests, r => Assert.Equal(key, r.IdempotencyKey));
        var create = JsonNode.Parse(fixture.Requests[0].Body)!;
        Assert.Equal(target.ToString(), create["counterpartyObjectId"]!.GetValue<string>());
        Assert.EndsWith("Z", create["occurredAt"]!.GetValue<string>());
        Assert.StartsWith("2026-10-18T12:00:00", create["occurredAt"]!.GetValue<string>());
        await fixture.Client.SaveInteractionAsync(draft with { Id = id, Version = 7 }, null);
        var patch = fixture.Requests[2]; Assert.Equal(HttpMethod.Patch, patch.Method); Assert.Equal("\"v7\"", patch.IfMatch);
        Assert.Null(patch.IdempotencyKey);
        var fields = JsonNode.Parse(patch.Body)!.AsObject();
        Assert.False(fields.ContainsKey("participantObjectIds")); Assert.False(fields.ContainsKey("counterpartyObjectId"));
        Assert.Equal(["details", "interactionType", "nextStep", "nextStepDueAt", "occurredAt", "subject"], fields.Select(f => f.Key).Order().ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("W/\"v7\"")]
    [InlineData("\"v6\"")]
    public async System.Threading.Tasks.Task DetailRejectsUnconfirmedVersion(string? tag)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) =>
        {
            var response = Json(HttpStatusCode.OK, InteractionJson(id, Guid.NewGuid()));
            if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag);
            return System.Threading.Tasks.Task.FromResult(response);
        });
        Assert.IsType<DesktopWorkResult<DesktopInteraction>.MalformedResponse>(await fixture.Client.GetInteractionAsync(id));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[\"bad\"]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    public async System.Threading.Tasks.Task MalformedParticipantsNeverEscapeAsUnhandledException(string ids)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK,
            "{\"items\":[" + InteractionJson(id, Guid.NewGuid(), participants: ids) + "],\"hasMore\":false}")));
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>.MalformedResponse>(await fixture.Client.GetInteractionsAsync(null));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(412)]
    [InlineData(422)]
    [InlineData(503)]
    public async System.Threading.Tasks.Task InteractionWriteMapsFailures(int code)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)code, "{}")));
        var value = new DesktopInteraction(Guid.NewGuid(), 7, Guid.NewGuid(), "note", DateTimeOffset.UtcNow, "Тема", null, null, null, []);
        var result = await fixture.Client.SaveInteractionAsync(value, null);
        Assert.True(code switch
        {
            403 => result is DesktopWorkResult<DesktopInteraction>.Forbidden,
            404 => result is DesktopWorkResult<DesktopInteraction>.NotFound,
            412 => result is DesktopWorkResult<DesktopInteraction>.Conflict,
            422 => result is DesktopWorkResult<DesktopInteraction>.ValidationFailure,
            _ => result is DesktopWorkResult<DesktopInteraction>.ServerUnavailable
        });
    }
}
