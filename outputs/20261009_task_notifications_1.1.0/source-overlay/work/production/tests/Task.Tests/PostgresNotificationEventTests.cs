using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application;
using Task.Domain;
using Npgsql;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public async System.Threading.Tasks.Task NotificationProjectionFailureRollsBackTaskEventAndNotificationTogether()
    {
        using var db = Database.Create(); if (db is null) return;
        db.Sql("CREATE FUNCTION public.fail_test_notice() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'notification projection fixture'; END $$; CREATE TRIGGER fail_notice BEFORE INSERT ON notify.notifications FOR EACH ROW EXECUTE FUNCTION public.fail_test_notice();");
        var id = Guid.NewGuid();
        var command = new TaskWriteCommand(db.Organization, db.User, db.Session, "POST_api_v1_tasks", Guid.NewGuid(), "notification-failure-001",
            TaskWriteRequestHasher.ComputeSha256("{}"), id, null, "task.create", "TaskCreated", ["assigneeIds"], "{}",
            _ => { var task = TaskAggregate.Create(id, db.Organization, db.User, "Atomic", DateTimeOffset.UtcNow, content: new TaskCardContent { AssigneeIds = [db.OtherUser] }); return new(task, new TaskWriteHttpResult(201, new Dictionary<string,string>(), "{}", id)); });
        await Assert.ThrowsAsync<PostgresException>(() => db.Runtime.CreateTaskWriteCommandExecutor().ExecuteAsync(command));
        Assert.Null(db.Runtime.CreateTaskStore().Get(id, db.Organization));
        Assert.Equal(0, db.Count("notify.notifications")); Assert.Equal(0, db.Count("governance.domain_events"));
    }
    [Fact]
    public async System.Threading.Tasks.Task CorporateTaskAssignmentCommentCompletionAndProjectInvitationProduceAuthorizedDurableNotifications()
    {
        using var db = Database.Create(); if (db is null) return;
        var id = Guid.NewGuid();
        TaskWriteHttpResult Response(TaskAggregate task) => new(200, new Dictionary<string, string>(), JsonSerializer.Serialize(new { id, version = task.Metadata.Version }), id);
        var create = new TaskWriteCommand(db.Organization, db.User, db.Session, "POST_api_v1_tasks", Guid.NewGuid(), "notification-create-001",
            TaskWriteRequestHasher.ComputeSha256("{\"title\":\"Task\"}"), id, null, "task.create", "TaskCreated", ["title", "assigneeIds"], "{}",
            _ => { var task = TaskAggregate.Create(id, db.Organization, db.User, "Назначенная задача", DateTimeOffset.UtcNow, content: new TaskCardContent { AssigneeIds = [db.OtherUser] }); return new(task, Response(task)); });
        var executor = db.Runtime.CreateTaskWriteCommandExecutor();
        await executor.ExecuteAsync(create); await executor.ExecuteAsync(create);
        JsonArray Notices() => db.Call("notifications", "list", user: db.OtherUser, session: db.OtherSession, admin: false).Body!["items"]!.AsArray();
        var first = Assert.Single(Notices())!;
        Assert.Equal("task.assigned", first["notificationType"]!.ToString()); Assert.Equal(id.ToString(), first["sourceObjectId"]!.ToString());
        Assert.Equal("Назначенная задача", first["body"]!.ToString()); Assert.Equal(1, db.Count("notify.notifications"));
        var comment = db.Call("tasks", "task-comment-add", "{\"body\":\"Проверьте результат\"}", id, 1, key: "notify-comment-001");
        db.Call("tasks", "task-comment-add", "{\"body\":\"Проверьте результат\"}", id, 1, key: "notify-comment-001");
        Assert.Equal(2, Notices().Count);
        Assert.Single(Notices(), n => n!["notificationType"]!.ToString() == "task.comment");
        var complete = new TaskWriteCommand(db.Organization, db.User, db.Session, "POST_api_v1_tasks_id_transition", Guid.NewGuid(), "notify-complete-001",
            TaskWriteRequestHasher.ComputeSha256("{\"status\":\"completed\"}"), id, comment.Version, "task.change_status", "TaskStatusChanged", ["status"], "{\"targetStatus\":\"completed\"}",
            old => { var task = old!.Complete(db.User, DateTimeOffset.UtcNow); return new(task, Response(task)); });
        await executor.ExecuteAsync(complete);
        Assert.Single(Notices(), n => n!["notificationType"]!.ToString() == "task.completed");
        db.Call("notifications", "read", "{}", Guid.Parse(first["id"]!.ToString()), user: db.OtherUser, session: db.OtherSession, admin: false);
        Assert.Single(Notices(), n => n!["status"]!.ToString() == "read");
        db.Sql("UPDATE work.tasks SET card_content=jsonb_set(card_content,'{assigneeIds}','[]') WHERE id=$1;", id);
        Assert.Empty(Notices()); // current source authorization also hides already-produced history
        var role = Guid.NewGuid(); db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name) VALUES($1,$2,'notification_member','Member');", role, db.Organization);
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Проект", ["ownerUserId"] = db.User }.ToJsonString());
        db.Call("projects", "member-add", new JsonObject { ["userAccountId"] = db.OtherUser, ["projectRoleId"] = role }.ToJsonString(), Id(project), 1);
        Assert.Equal("project.invitation", Assert.Single(Notices())!["notificationType"]!.ToString());
    }
}
