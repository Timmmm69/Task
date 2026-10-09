using System.Net;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static string ReminderJson(Guid id, Guid occurrence, int version = 4) => new JsonObject
    {
        ["id"] = id, ["version"] = version, ["targetObjectId"] = ItemId, ["targetTitle"] = "Task",
        ["triggerType"] = "before_deadline", ["offsetMinutes"] = 37,
        ["nextTriggerAt"] = DateTimeOffset.UtcNow.AddHours(1), ["status"] = "delivered",
        ["currentOccurrence"] = new JsonObject
        { ["id"] = occurrence, ["version"] = 7, ["reminderId"] = id, ["dueAt"] = DateTimeOffset.UtcNow, ["status"] = "delivered" }
    }.ToJsonString();

    [Fact]
    public async System.Threading.Tasks.Task ReminderClient_UsesOwnRecipientRuleEtagAndIndependentOccurrenceEtag()
    {
        var id = Guid.NewGuid(); var occurrence = Guid.NewGuid();
        var payload = ReminderJson(id, occurrence);
        await using var fixture = await Fixture.CreateAsync((request, _) =>
            System.Threading.Tasks.Task.FromResult(Json(request.Method == System.Net.Http.HttpMethod.Delete ? HttpStatusCode.Accepted
                : request.RequestUri!.AbsolutePath.EndsWith("/dismiss") ? HttpStatusCode.NoContent
                : request.Method == System.Net.Http.HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v1/reminders" ? HttpStatusCode.Created : HttpStatusCode.OK, payload)));
        var client = fixture.Client;
        var rule = Assert.IsType<DesktopWorkResult<DesktopReminder>.Succeeded>(await client.SaveReminderAsync(ItemId,"before_deadline",37,null,null)).Value;
        Assert.Equal(7, rule.CurrentOccurrence?.Version);
        var body = JsonNode.Parse(fixture.Requests[0].Body)!;
        Assert.Equal(UserId.ToString(), body["recipientUserId"]!.GetValue<string>());
        Assert.Contains("includeOccurrence=true", fixture.Requests[0].Uri.Query);
        Assert.IsType<DesktopWorkResult<DesktopReminder>.Succeeded>(await client.SaveReminderAsync(ItemId,"before_deadline",38,null,rule));
        Assert.Equal("\"v4\"", fixture.Requests[1].IfMatch);
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await client.ActOnReminderAsync(id,7,DateTimeOffset.UtcNow.AddMinutes(15)));
        Assert.Equal("\"v7\"", fixture.Requests[2].IfMatch);
        Assert.Equal(7, JsonNode.Parse(fixture.Requests[2].Body)!["expectedVersion"]!.GetValue<int>());
        Assert.NotNull(fixture.Requests[2].IdempotencyKey);
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await client.ActOnReminderAsync(id,7,null));
        Assert.Null(fixture.Requests[3].IdempotencyKey);
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await client.CancelReminderAsync(rule));
        Assert.Equal("\"v4\"", fixture.Requests[4].IfMatch);
        Assert.IsType<DesktopWorkResult<bool>.Succeeded>(await client.RestoreReminderAsync(rule));
        Assert.NotNull(fixture.Requests[5].IdempotencyKey);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReminderClient_ReadsAllPagesAndRejectsPartialOrMismatchedOccurrence()
    {
        var id = Guid.NewGuid(); var occurrence = Guid.NewGuid();
        var responses = new Queue<System.Net.Http.HttpResponseMessage>([
            Json(HttpStatusCode.OK, "{\"items\":[" + ReminderJson(id,occurrence) + "],\"nextCursor\":\"next\"}"),
            Json(HttpStatusCode.OK, "{\"items\":[" + ReminderJson(Guid.NewGuid(),Guid.NewGuid()) + "],\"nextCursor\":null}"),
            Json(HttpStatusCode.OK, "{\"items\":[" + ReminderJson(id,occurrence) + "],\"nextCursor\":\"next\"}"),
            Json(HttpStatusCode.ServiceUnavailable, "{}"),
            Json(HttpStatusCode.OK, ReminderJson(id,occurrence).Replace("\"reminderId\":\"" + id, "\"reminderId\":\"" + Guid.NewGuid()))
        ]);
        await using var fixture = await Fixture.CreateAsync((_,_) => System.Threading.Tasks.Task.FromResult(responses.Dequeue()));
        Assert.Equal(2, Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopReminder>>.Succeeded>(await fixture.Client.GetRemindersAsync()).Value.Count);
        Assert.Contains("cursor=next",fixture.Requests[1].Uri.Query);
        Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopReminder>>.ServerUnavailable>(await fixture.Client.GetRemindersAsync());
        Assert.IsType<DesktopWorkResult<DesktopReminder>.MalformedResponse>(await fixture.Client.GetReminderAsync(id));
    }
}
