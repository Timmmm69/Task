using System.Text.Json.Nodes;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Theory]
    [InlineData("project.create", "created")]
    [InlineData("TaskCreated", "created")]
    [InlineData("TaskStatusChanged", "state_changed")]
    [InlineData("project.patch", "updated")]
    [InlineData("TaskArchived", "archived")]
    [InlineData("project.unarchive", "restored")]
    [InlineData("TaskRestored", "restored")]
    [InlineData("TaskMovedToTrash", "trashed")]
    [InlineData("project.purge", "purged")]
    public void History_CanonicalMetadataAndLegacyClientFields(string eventType, string changeType)
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var project = db.Call("projects", "create", $$"""{"name":"History contract","ownerUserId":"{{db.User}}"}""");
        var id = Id(project);
        db.Sql("UPDATE governance.domain_events SET event_type=$2,changed_fields=ARRAY['name'],payload='{\"private\":\"do not expose\"}'::jsonb WHERE aggregate_id=$1", id, eventType);
        var page = db.Call("projects", "history", id: id).Body!;
        var row = Assert.Single(page["items"]!.AsArray())!.AsObject();

        Assert.Equal(id, row["objectId"]!.GetValue<Guid>());
        Assert.Equal(1, row["objectVersion"]!.GetValue<int>());
        Assert.Equal(changeType, row["changeType"]!.ToString());
        Assert.NotEqual(Guid.Empty, row["id"]!.GetValue<Guid>());
        Assert.NotEqual(Guid.Empty, row["correlationId"]!.GetValue<Guid>());
        Assert.EndsWith("Z", row["changedAt"]!.ToString());
        Assert.Equal(DateTimeOffset.Parse(row["changedAt"]!.ToString()), DateTimeOffset.Parse(row["occurredAt"]!.ToString()));
        Assert.Equal(row["objectVersion"]!.ToString(), row["version"]!.ToString());
        Assert.Equal(eventType, row["action"]!.ToString());
        Assert.Equal(db.User, row["actorUserId"]!.GetValue<Guid>());
        Assert.IsType<JsonArray>(row["changedFields"]);
        Assert.DoesNotContain("private", row.ToJsonString());
        Assert.DoesNotContain("payload", row.Select(field => field.Key));

        if (Environment.GetEnvironmentVariable("TASK_RETURN_CONTRACT_EVIDENCE") is { Length: > 0 } evidence)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, $"project-history-{eventType}.json"), page.ToJsonString());
        }
    }
}
