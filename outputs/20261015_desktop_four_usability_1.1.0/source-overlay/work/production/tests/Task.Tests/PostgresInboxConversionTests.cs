using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task Inbox_ConcurrentReplayCreatesExactlyOneTask()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var source = db.Call("inbox-items", "create", "{\"itemType\":\"task\",\"title\":\"Concurrent\"}");
        var responses = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 2).Select(_ => System.Threading.Tasks.Task.Run(() =>
            db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", Id(source), 1, key: "concurrent-convert-001"))));
        Assert.Equal(Id(responses[0]), Id(responses[1])); Assert.Equal(1, db.Count("work.tasks"));
        Assert.Single(responses, response => response.WasReplayed);
    }

    [RequiresPostgresFact]
    public void Inbox_FailedConversionRollsBackTaskEventsAndSourceAndCanRetry()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var source = db.Call("inbox-items", "create", "{\"itemType\":\"task\",\"title\":\"Atomic\"}");
        var eventCount = db.Count("governance.domain_events");
        db.Sql("CREATE FUNCTION work.fail_conversion_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test rollback' USING ERRCODE='23514'; END $$; CREATE TRIGGER fail_conversion_test BEFORE UPDATE ON work.inbox_items FOR EACH ROW EXECUTE FUNCTION work.fail_conversion_test();");
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", Id(source), 1, key: "atomic-convert-001")).Status);
        Assert.Equal(0, db.Count("work.tasks")); Assert.Equal(eventCount, db.Count("governance.domain_events"));
        Assert.Equal("unprocessed", db.Call("inbox-items", "get", id: Id(source)).Body!["status"]!.GetValue<string>());
        db.Sql("DROP TRIGGER fail_conversion_test ON work.inbox_items; DROP FUNCTION work.fail_conversion_test();");
        Assert.Equal(201, db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", Id(source), 1, key: "atomic-convert-001").Status);
        Assert.Equal(1, db.Count("work.tasks"));
    }

    [RequiresPostgresFact]
    public void Inbox_UpgradeFromSixteenPreservesLegacyCaptureWithoutCloningItsTask()
    {
        using var db = Database.Create(); Assert.NotNull(db); DeadlineRoles(db);
        db.Sql("DROP TABLE work.inbox_items; DELETE FROM infrastructure.schema_migrations WHERE version=17;");
        var legacy = DeadlineTask(db, null);
        DeadlineTask(db, DateTimeOffset.UtcNow.AddDays(1));
        db.Runtime.CreateMigrator().ApplyPending(); db.Runtime.CreateMigrator().ApplyPending();
        var source = Assert.Single(db.Call("inbox-items", "list").Body!["items"]!.AsArray())!;
        Assert.Null(source["legacyTaskId"]);
        var converted = db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", Guid.Parse(source["id"]!.GetValue<string>()), 1);
        Assert.Equal(legacy, Id(converted)); Assert.Equal(2, db.Count("work.tasks"));
        Assert.Empty(db.Call("inbox-items", "list").Body!["items"]!.AsArray());
    }
    [RequiresPostgresFact]
    public void Inbox_TitleOnlyConversionIsDurableAtomicAndIdempotentWithoutDeadline()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var source = db.Call("inbox-items", "create", "{\"itemType\":\"task\",\"title\":\"Without deadline\"}", key: "inbox-create-001");
        var id = Id(source);
        Assert.Equal(201, source.Status);
        Assert.Null(source.Body!["legacyTaskId"]);
        Assert.Single(db.Call("inbox-items", "list").Body!["items"]!.AsArray());
        Assert.Equal(0, db.Count("work.tasks"));
        var converted = db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", id, 1, key: "inbox-convert-001");
        Assert.Equal(201, converted.Status); Assert.Null(converted.Body!["deadlineAt"]);
        Assert.Equal("new", converted.Body["status"]!.GetValue<string>());
        Assert.Equal("Without deadline", converted.Body["title"]!.GetValue<string>());
        Assert.Equal(1, db.Count("work.tasks"));
        Assert.Equal(id.ToString(), DeadlineValue<string>(db, "SELECT metadata->>'sourceObjectId' FROM governance.audit_entries WHERE object_id=$1 AND object_type='task' LIMIT 1", Id(converted)));
        Assert.Empty(db.Call("inbox-items", "list").Body!["items"]!.AsArray());
        var after = db.Call("inbox-items", "get", id: id);
        Assert.Equal("converted", after.Body!["status"]!.GetValue<string>());
        Assert.Equal(Id(converted), Guid.Parse(after.Body["convertedObjectId"]!.GetValue<string>()));
        var replay = db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", id, 1, key: "inbox-convert-001");
        Assert.True(replay.WasReplayed); Assert.Equal(Id(converted), Id(replay));
        Assert.Equal(1, db.Count("work.tasks"));
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", id, 1, key: "different-conversion-key")).Status);
    }

    [RequiresPostgresFact]
    public void Inbox_OwnerBoundaryVersionsAndValidationDoNotLeakOrPartiallyConvert()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var source = db.Call("inbox-items", "create", "{\"itemType\":\"task\",\"title\":\"Private\"}");
        var id = Id(source);
        Assert.Empty(db.Call("inbox-items", "list", user: db.OtherUser).Body!["items"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "get", id: id, user: db.OtherUser)).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", id, 1, user: db.OtherUser)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":2}", id, 1)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "patch", "{\"title\":null}", id, 1)).Status);
        Assert.Equal(0, db.Count("work.tasks"));
        var updated = db.Call("inbox-items", "patch", "{\"title\":\"Updated\"}", id, 1);
        Assert.Equal(2, updated.Version);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", "{\"expectedInboxVersion\":1}", id, 1)).Status);
        Assert.Single(db.Call("inbox-items", "list").Body!["items"]!.AsArray());
        Assert.Equal(0, db.Count("work.tasks"));
    }

    [RequiresPostgresFact]
    public void Inbox_ProjectAndScheduledDateUseCanonicalFieldsAndConversionReplayRechecksAccess()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var project = db.Call("projects", "create", $$"""{"name":"Project","ownerUserId":"{{db.User}}"}""");
        var source = db.Call("inbox-items", "create", "{\"itemType\":\"note\",\"title\":\"Task\",\"content\":\"Description\"}");
        var body = $$"""{"expectedInboxVersion":1,"projectId":"{{Id(project)}}","scheduledDate":"2026-10-20"}""";
        var converted = db.Call("inbox-items", "convert-to-task", body, Id(source), 1, key: "project-conversion-001");
        Assert.Equal("Description", converted.Body!["description"]!.GetValue<string>());
        Assert.Equal("2026-10-20", converted.Body["scheduledDate"]!.GetValue<string>());
        Assert.Equal(Id(project).ToString(), converted.Body["projectId"]!.GetValue<string>());
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("inbox-items", "convert-to-task", body, Id(source), 1, key: "project-conversion-001", admin: false, permissions: ["Inbox.ManageOwn"])).Status);
    }
}
