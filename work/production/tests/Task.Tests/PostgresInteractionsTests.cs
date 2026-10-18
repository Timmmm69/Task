using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    private static string InteractionBody(Guid target, params Guid[] participants) => new JsonObject
    {
        ["counterpartyObjectId"] = target, ["interactionType"] = "call", ["occurredAt"] = "2026-10-18T10:00:00Z",
        ["subject"] = "CRM regression", ["details"] = "Details", ["nextStep"] = "Manual next step", ["nextStepDueAt"] = "2026-10-19T10:00:00Z",
        ["participantObjectIds"] = new JsonArray(participants.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray())
    }.ToJsonString();

    [RequiresPostgresFact]
    public void Interactions_CreateReplayPatchCasAndParticipantsRoundTrip()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var target = Id(db.Call("contacts", "create", """{"firstName":"Anna","displayName":"Anna"}"""));
        var company = Id(db.Call("companies", "create", """{"name":"Company"}"""));
        var body = InteractionBody(target, target, company);
        var created = db.Call("interactions", "create", body, key: "crm-interaction-12-4");
        var id = Id(created); Assert.Equal(201, created.Status); Assert.Equal(1, created.Version);
        Assert.Equal(id, Id(db.Call("interactions", "create", body, key: "crm-interaction-12-4")));
        Assert.Equal(1, db.Count("crm.interactions"));
        var patched = db.Call("interactions", "patch", """{"subject":"Changed","nextStep":null,"nextStepDueAt":null}""", id, 1);
        Assert.Equal(2, patched.Version); Assert.Equal(target.ToString(), patched.Body!["counterpartyObjectId"]!.ToString());
        Assert.True(JsonNode.DeepEquals(created.Body!["participantObjectIds"], patched.Body["participantObjectIds"]));
        Assert.Null(patched.Body["nextStep"]); Assert.Null(patched.Body["nextStepDueAt"]);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("interactions", "patch", """{"subject":"Stale"}""", id, 1)).Status);
        Assert.Equal("Changed", db.Call("interactions", "get", id: id).Body!["subject"]!.ToString());
        var companyInteraction = db.Call("interactions", "create", InteractionBody(company)); Assert.Equal(201, companyInteraction.Status);
        Assert.Equal(2, db.Call("interactions", "list", query: new() { ["limit"] = "50" }).Body!["items"]!.AsArray().Count);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("interactions", "list", query: new() { ["counterpartyObjectId"] = target.ToString() })).Status);
    }

    [RequiresPostgresFact]
    public void Interactions_ObjectScopeForeignTargetsAndDeletedCounterpartiesAreEnforced()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var target = Id(db.Call("contacts", "create", """{"firstName":"Private","displayName":"Private"}"""));
        var value = db.Call("interactions", "create", InteractionBody(target)); var id = Id(value);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("interactions", "get", id: id, user: db.OtherUser, session: db.OtherSession, admin: false)).Status);
        Assert.Empty(db.Call("interactions", "list", user: db.OtherUser, session: db.OtherSession, admin: false).Body!["items"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("interactions", "patch", """{"subject":"No access"}""", id, 1, user: db.OtherUser, session: db.OtherSession, admin: false)).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("interactions", "create", InteractionBody(target), user: db.OtherUser, session: db.OtherSession, admin: false)).Status);
        using var foreign = Database.Create(); Assert.NotNull(foreign);
        var foreignTarget = Id(foreign.Call("companies", "create", """{"name":"Foreign"}"""));
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("interactions", "create", InteractionBody(foreignTarget))).Status);
        db.Call("contacts", "trash", "{}", target, 1);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("interactions", "create", InteractionBody(target))).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("interactions", "patch", """{"subject":"Deleted target"}""", id, 1)).Status);
        Assert.Equal(1, db.Count("crm.interactions"));
    }
}
