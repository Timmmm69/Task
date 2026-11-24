using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task RecurrenceDocuments_PostgresCopiesCleanStepsAndAllowedLinksAcrossHistoryAndRetry()
    {
        using var db = Database.Create()!;
        GrantRecurrenceDocumentPermissions(db);
        var taskStore = db.Runtime.CreateTaskStore();
        var source = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Monthly work", DateTimeOffset.UtcNow,
            content: new() { Description = "Use the current working instructions.", PlanningMonth = "2027-12" });
        taskStore.Add(source);

        var folder = Id(db.Call("catalog-items", "create", """{"name":"Disconnected folder","itemType":"folder_reference"}"""));
        db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Task-R4-fixture\\disconnected"}""", folder, 1);
        var denied = Id(db.Call("catalog-items", "create", """{"name":"Restricted folder","itemType":"folder_reference"}"""));
        var deleted = Id(db.Call("catalog-items", "create", """{"name":"Deleted file","itemType":"file_reference"}"""));
        var id = source.Metadata.Id;
        var version = 1;
        foreach (var target in new[] { folder, denied, deleted })
            db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = target, ["linkType"] = "task_file" }.ToJsonString(), id, version++);
        var firstStep = Id(db.Call("tasks", "task-check-add", """{"text":"First step"}""", id, version++));
        db.Call("tasks", "task-check-add", """{"text":"Second step"}""", id, version++);
        db.Call("tasks", "task-check-patch", """{"isCompleted":true}""", id, version, child: firstStep);

        // The runtime role uses the real authorization functions and write grants.
        var service = new RecurrenceService(db.ApiRuntime.CreateRecurrenceStore());
        var definition = MonthSeries(db, id);
        var body = JsonSerializer.Serialize(definition, RecurrenceService.JsonOptions);
        RecurrenceReply Create() => new RecurrenceService(db.ApiRuntime.CreateRecurrenceStore()).Create(db.Organization, db.User, "monthly-documents", body);
        var replies = await System.Threading.Tasks.Task.WhenAll(
            System.Threading.Tasks.Task.Run(Create), System.Threading.Tasks.Task.Run(Create));
        var series = Assert.Single(service.List(db.Organization, db.User));
        Assert.All(replies, reply =>
        {
            using var json = JsonDocument.Parse(reply.Json);
            Assert.Equal(series.Id, json.RootElement.GetProperty("id").GetGuid());
            Assert.DoesNotContain("catalogItemIds", reply.Json);
        });
        var occurrences = service.GetOccurrences(db.Organization, series.Id, db.User).OrderBy(o => o.LocalDate).ToArray();
        Assert.Equal(new[] { new DateOnly(2027, 12, 1), new DateOnly(2028, 1, 1), new DateOnly(2028, 2, 1) }, occurrences.Select(o => o.LocalDate));
        var original = db.Call("tasks", "task-workspace", id: id).Body!;
        var stepIds = original["checklist"]!.AsArray().Select(step => step!["id"]!.ToString()).ToHashSet();
        Assert.True(original["checklist"]![0]!["isCompleted"]!.GetValue<bool>());
        foreach (var occurrence in occurrences) Check(occurrence, 3);
        Assert.Equal(0, db.Count("calendar.task_deadline_preferences"));
        Assert.Equal(0, db.Count("calendar.reminders"));

        var completed = taskStore.Get(occurrences[0].TaskId, db.Organization)!;
        taskStore.Save(completed.Complete(db.User, DateTimeOffset.UtcNow), completed.Metadata.Version);
        var local = service.ApplyChange(db.Organization, db.User, series.Id, series.Version, "feb-exception", occurrences[2].LocalDate,
            (int)occurrences[2].TaskVersion, RecurrenceChangeScope.ThisOccurrence, "Manual February", "normal", null);
        var edited = service.ApplyChange(db.Organization, db.User, series.Id, local.Version, "future-edit", occurrences[1].LocalDate,
            (int)occurrences[1].TaskVersion, RecurrenceChangeScope.ThisAndFuture, "Future title", "normal", null);

        // The catalog objects previously supplied by the snapshot are rechecked for each occurrence.
        var department = Guid.NewGuid();
        var denyRole = Guid.NewGuid();
        db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES($1,$2,'department',$3,$3,now(),now());",
            department, db.Organization, db.User);
        db.Sql("UPDATE org.employee_profiles SET department_id=$1 WHERE id=(SELECT employee_profile_id FROM iam.user_accounts WHERE id=$2);", department, db.OtherUser);
        db.Sql("UPDATE core.objects SET created_by=$1 WHERE id=$2;", db.OtherUser, denied);
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name,is_system) VALUES($1,$2,'target_deny','Target deny',false);", denyRole, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'filecatalog.read','deny');", denyRole);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id,granted_by,department_id) VALUES($1,$2,$1,$3);", db.User, denyRole, department);
        db.Call("catalog-items", "archive", id: deleted, version: 1);

        var generate = service.Generate(db.Organization, db.User, series.Id, edited.Version, "extend-horizon", new(2028, 3, 31));
        var restarted = new RecurrenceService(db.ApiRuntime.CreateRecurrenceStore());
        Assert.Equal(generate.Version, restarted.Generate(db.Organization, db.User, series.Id, edited.Version, "extend-horizon", new(2028, 3, 31)).Version);
        var all = restarted.GetOccurrences(db.Organization, series.Id, db.User).OrderBy(o => o.LocalDate).ToArray();
        Assert.Equal(new[] { "Monthly work", "Future title", "Manual February", "Future title" }, all.Select(o => o.Title));
        Assert.Equal("completed", all[0].Status);
        Check(all[3], 1);
        Assert.Equal(folder.ToString("D"), db.Call("tasks", "task-workspace", id: all[3].TaskId).Body!["files"]![0]!["targetId"]!.ToString());
        // The public form can round-trip the template without seeing the internal snapshot.
        var patchedTemplate = all[3].Template! with { Description = "Updated instructions" };
        var patched = restarted.Patch(db.Organization, db.User, series.Id, generate.Version, "instruction-edit",
            JsonSerializer.Serialize(new { Template = patchedTemplate }, RecurrenceService.JsonOptions));
        var updated = taskStore.Get(all[3].TaskId, db.Organization)!;
        Assert.Equal("Updated instructions", updated.Content.Description);
        Assert.Equal(source.Content.Description, taskStore.Get(all[0].TaskId, db.Organization)!.Content.Description);
        Assert.Equal(source.Content.Description, taskStore.Get(all[2].TaskId, db.Organization)!.Content.Description);
        var march = db.Call("tasks", "task-workspace", id: all[3].TaskId).Body!;
        Assert.All(march["checklist"]!.AsArray(), step => Assert.Contains(step!["id"]!.ToString(), stepIds));
        restarted.Generate(db.Organization, db.User, series.Id, patched.Version, "next-horizon", new(2028, 4, 30));
        var april = Assert.Single(restarted.GetOccurrences(db.Organization, series.Id, db.User), o => o.LocalDate.Month == 4);
        var aprilCard = db.Call("tasks", "task-workspace", id: april.TaskId).Body!;
        Assert.Equal(2, aprilCard["checklist"]!.AsArray().Count);
        Assert.Equal(folder.ToString("D"), Assert.Single(aprilCard["files"]!.AsArray())!["targetId"]!.ToString());

        Assert.Equal(version + 1, taskStore.Get(id, db.Organization)!.Metadata.Version);
        Assert.True(db.Call("tasks", "task-workspace", id: id).Body!["checklist"]![0]!["isCompleted"]!.GetValue<bool>());

        void Check(RecurrenceOccurrenceDetails occurrence, int links)
        {
            var task = taskStore.Get(occurrence.TaskId, db.Organization)!;
            Assert.NotEqual(id, occurrence.TaskId);
            Assert.Equal(occurrence.LocalDate.ToString("yyyy-MM"), task.Content.PlanningMonth);
            Assert.Equal(source.Content.Description, task.Content.Description);
            Assert.Null(task.Content.ScheduledDate);
            Assert.Null(task.Content.DeadlineDate);
            Assert.Null(task.Schedule.StartsAtUtc);
            Assert.Null(task.Schedule.DeadlineUtc);
            var card = db.Call("tasks", "task-workspace", id: occurrence.TaskId).Body!;
            var steps = card["checklist"]!.AsArray();
            Assert.Equal(new[] { "First step", "Second step" }, steps.Select(step => step!["text"]!.ToString()));
            Assert.All(steps, step =>
            {
                Assert.False(step!["isCompleted"]!.GetValue<bool>());
                Assert.True(stepIds.Add(step["id"]!.ToString()));
            });
            Assert.Equal(links, card["files"]!.AsArray().Count);
            Assert.Null(occurrence.Template!.CatalogItemIds);
        }
    }

    [RequiresPostgresFact]
    public void RecurrenceDocuments_PostgresYearlyMonthOnlyAndDatedRulesKeepCalendarAndReminderSemantics()
    {
        using var db = Database.Create()!;
        GrantRecurrenceDocumentPermissions(db);
        var taskStore = db.Runtime.CreateTaskStore();
        var source = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Yearly work", DateTimeOffset.UtcNow,
            schedule: TaskSchedule.Create(null, new(2028, 2, 29, 10, 0, 0, TimeSpan.Zero)),
            content: new() { Description = "Yearly instructions", DeadlineReminderOffsetMinutes = 2880 });
        taskStore.Add(source);
        var id = source.Metadata.Id;
        db.Call("tasks", "task-check-add", """{"text":"Annual check"}""", id, 1);
        var folder = Id(db.Call("catalog-items", "create", """{"name":"Annual folder","itemType":"folder_reference"}"""));
        db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = folder, ["linkType"] = "task_file" }.ToJsonString(), id, 2);
        var service = new RecurrenceService(db.ApiRuntime.CreateRecurrenceStore());
        var yearly = MonthSeries(db, id) with { Frequency = "yearly", MonthOfYear = 2, OccurrenceStartDate = new(2028, 2, 1) };
        service.Create(db.Organization, db.User, "yearly-documents", JsonSerializer.Serialize(yearly, RecurrenceService.JsonOptions));
        var series = Assert.Single(service.List(db.Organization, db.User));
        service.Generate(db.Organization, db.User, series.Id, series.Version, "yearly-horizon", new(2029, 2, 28));
        var occurrences = service.GetOccurrences(db.Organization, series.Id, db.User).OrderBy(o => o.LocalDate).ToArray();
        Assert.Equal(new[] { new DateOnly(2028, 2, 1), new DateOnly(2029, 2, 1) }, occurrences.Select(o => o.LocalDate));
        foreach (var row in occurrences)
        {
            var task = taskStore.Get(row.TaskId, db.Organization)!;
            Assert.Equal(row.LocalDate.ToString("yyyy-MM"), task.Content.PlanningMonth);
            Assert.Equal("Yearly instructions", task.Content.Description);
            Assert.Null(task.Schedule.DeadlineUtc);
            Assert.Null(task.Schedule.StartsAtUtc);
            var card = db.Call("tasks", "task-workspace", id: row.TaskId).Body!;
            Assert.Equal("Annual check", Assert.Single(card["checklist"]!.AsArray())!["text"]!.ToString());
            Assert.Equal(folder.ToString("D"), Assert.Single(card["files"]!.AsArray())!["targetId"]!.ToString());
        }
        Assert.Equal(1, db.Count("calendar.task_deadline_preferences"));
        var leapYear = DateTime.UtcNow.Year + 1;
        while (!DateTime.IsLeapYear(leapYear)) leapYear++;
        var dated = yearly with
        {
            MonthOnly = false,
            MonthDays = [29],
            LocalStartTime = new(9, 0),
            OccurrenceStartDate = new(leapYear, 2, 29),
            Template = yearly.Template with { Title = "Dated yearly work", DeadlineOffsetMinutes = 60 }
        };
        service.Create(db.Organization, db.User, "dated-yearly", JsonSerializer.Serialize(dated, RecurrenceService.JsonOptions));
        var datedSeries = Assert.Single(service.List(db.Organization, db.User), s => s.Id != series.Id);
        service.Generate(db.Organization, db.User, datedSeries.Id, datedSeries.Version, "dated-horizon", new(leapYear + 1, 2, 28));
        var leap = Assert.Single(service.GetOccurrences(db.Organization, datedSeries.Id, db.User));
        var leapTask = taskStore.Get(leap.TaskId, db.Organization)!;
        Assert.Equal(new DateTimeOffset(leapYear, 2, 29, 9, 0, 0, TimeSpan.Zero), leapTask.Schedule.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(leapYear, 2, 29, 10, 0, 0, TimeSpan.Zero), leapTask.Schedule.DeadlineUtc);
        using var reminder = db.Source.CreateCommand("""
            SELECT r.next_trigger_at FROM calendar.reminders r
            JOIN calendar.task_deadline_preferences p ON p.organization_id=r.organization_id AND p.task_id=r.target_object_id
            WHERE r.target_object_id=$1 AND p.offset_minutes=2880 AND r.managed_source='task_deadline';
            """);
        reminder.Parameters.AddWithValue(leap.TaskId);
        Assert.Equal(new DateTime(leapYear, 2, 27, 10, 0, 0, DateTimeKind.Utc), reminder.ExecuteScalar());
    }

    [RequiresPostgresFact]
    public void RecurrenceDocuments_PostgresRollsBackTaskStepsLinksAndReplayRecordOnOccurrenceFailure()
    {
        using var db = Database.Create()!;
        GrantRecurrenceDocumentPermissions(db);
        var source = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Rollback", DateTimeOffset.UtcNow);
        db.Runtime.CreateTaskStore().Add(source);
        var id = source.Metadata.Id;
        db.Call("tasks", "task-check-add", """{"text":"Step"}""", id, 1);
        var folder = Id(db.Call("catalog-items", "create", """{"name":"Folder","itemType":"folder_reference"}"""));
        db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = folder, ["linkType"] = "task_file" }.ToJsonString(), id, 2);
        db.Sql("""
            CREATE FUNCTION calendar.reject_test_occurrence() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'test rollback'; END $$;
            CREATE TRIGGER reject_test_occurrence BEFORE INSERT ON calendar.recurrence_occurrences
            FOR EACH ROW EXECUTE FUNCTION calendar.reject_test_occurrence();
            """);
        var service = new RecurrenceService(db.ApiRuntime.CreateRecurrenceStore());
        var body = JsonSerializer.Serialize(MonthSeries(db, id), RecurrenceService.JsonOptions);
        Assert.Throws<PostgresException>(() => service.Create(db.Organization, db.User, "rollback-command", body));
        foreach (var table in new[] { "work.tasks", "work.task_checklist", "core.object_links" }) Assert.Equal(1, db.Count(table));
        foreach (var table in new[] { "calendar.recurrence_series", "calendar.recurrence_occurrences", "calendar.recurrence_commands" }) Assert.Equal(0, db.Count(table));
        db.Sql("DROP TRIGGER reject_test_occurrence ON calendar.recurrence_occurrences;");
        Assert.Equal(201, service.Create(db.Organization, db.User, "rollback-command", body).Status);
        foreach (var table in new[] { "work.tasks", "work.task_checklist", "core.object_links" }) Assert.Equal(4, db.Count(table));
    }

    private static RecurrenceDefinition MonthSeries(Database db, Guid sourceId) => new()
    {
        Frequency = "monthly",
        Interval = 1,
        MonthDays = [1],
        MonthOnly = true,
        OccurrenceStartDate = new(2027, 12, 1),
        SourceTaskId = sourceId,
        TimeZone = "UTC",
        Template = new() { AuthorUserId = db.User, Title = "Monthly work" }
    };

    private static void GrantRecurrenceDocumentPermissions(Database db)
    {
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name,is_system) VALUES($1,$2,'recurrence_test','Test role',false);", role, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code) SELECT $1,code FROM iam.permissions;", role);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.User, role);
    }
}
