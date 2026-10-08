using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task SimilarSource_ArchiveAndTrashRespectExistingReadBoundary(bool trash)
    {
        using var db = Database.Create(); if (db is null) return;
        var source = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Source", DateTimeOffset.UtcNow);
        var store = db.Runtime.CreateTaskStore(); store.Add(source);
        var terminal = source.Cancel(db.User, DateTimeOffset.UtcNow);
        store.Save(terminal, source.Metadata.Version); source = terminal;
        var changed = trash ? source.MoveToTrash(db.User, DateTimeOffset.UtcNow) : source.Archive(db.User, DateTimeOffset.UtcNow);
        store.Save(changed, source.Metadata.Version);
        // Task.Create does not broaden source reading or restore the source as a side effect.
        Assert.Null(await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, source.Metadata.Id, db.User));
        Assert.Equal(changed.Metadata.LifecycleState, store.Get(source.Metadata.Id, db.Organization)!.Metadata.LifecycleState);
    }

    [Fact]
    public void SimilarTaskOptions_UseEffectiveCreatePermissionAndActiveRelations()
    {
        using var db = Database.Create(); if (db is null) return;
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name) VALUES($1,$2,'similar_member','Member');", role, db.Organization);
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Read only", ["ownerUserId"] = db.User }.ToJsonString());
        db.Call("projects", "member-add", new JsonObject { ["userAccountId"] = db.OtherUser.ToString(), ["projectRoleId"] = role.ToString() }.ToJsonString(), Id(project), 1);
        ProductApiResponse Choices(bool create) => db.Call("tasks", "task-options", user: db.OtherUser, admin: false,
            permissions: create ? ["Task.Read", "Task.Create", "Project.Read", "Employee.Read"] : ["Task.Read", "Project.Read", "Employee.Read"]);
        Assert.Single(Choices(false).Body!["projects"]!.AsArray());
        Assert.Empty(Choices(true).Body!["projects"]!.AsArray());
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'task.create','grant');", role);
        Assert.Single(Choices(true).Body!["projects"]!.AsArray());
        db.Sql("UPDATE projects.members SET permission_overrides='{\"allow\":[],\"deny\":[\"task.create\"]}'::jsonb WHERE project_id=$1 AND user_account_id=$2;", Id(project), db.OtherUser);
        Assert.Empty(Choices(true).Body!["projects"]!.AsArray());
        db.Sql("UPDATE projects.members SET permission_overrides='{}'::jsonb WHERE project_id=$1 AND user_account_id=$2;", Id(project), db.OtherUser);
        db.Sql("UPDATE core.objects SET lifecycle_state='archived',archived_at=statement_timestamp(),updated_at=statement_timestamp() WHERE id=$1;", Id(project));
        Assert.Empty(Choices(true).Body!["projects"]!.AsArray());
        db.Sql("UPDATE iam.user_accounts SET account_status='deactivated' WHERE id=$1;", db.User);
        Assert.DoesNotContain(Choices(true).Body!["people"]!.AsArray(), n => n!["id"]!.ToString() == db.User.ToString());
    }

    [Fact]
    public async System.Threading.Tasks.Task TaskCard_RealPostgres_ReadWriteChildrenAndCompletion()
    {
        using var db = Database.Create(); if (db is null) return;
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Внедрение", ["ownerUserId"] = db.User }.ToJsonString());
        var contact = db.Call("contacts", "create", "{\"firstName\":\"Анна\",\"displayName\":\"Анна\"}");
        var file = db.Call("catalog-items", "create", "{\"name\":\"Договор\",\"itemType\":\"file_reference\"}");
        var card = new TaskCardContent { Description = "Полный контекст", ProjectId = Id(project), RequesterUserId = db.OtherUser, PrimaryCounterpartyObjectId = Id(contact), AssigneeIds = [db.OtherUser], WatcherIds = [db.User], ScheduledDate = new(2026, 9, 4), PlannedDurationMinutes = 30 };
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Подготовить договор", DateTimeOffset.UtcNow, content: card);
        db.Runtime.CreateTaskStore().Add(task);
        var read = await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, task.Metadata.Id, db.OtherUser);
        Assert.NotNull(read); Assert.Equal(card.ToJson(), read.Content!.ToJson());
        Assert.Single((await db.Runtime.CreateTaskReadStore().GetPageAsync(new(db.Organization, db.OtherUser, 1))).Items);
        var id = task.Metadata.Id;
        var added = db.Call("tasks", "task-check-add", "{\"text\":\"Согласовать\"}", id, 1, key: "checklist-0001");
        var replay = db.Call("tasks", "task-check-add", "{\"text\":\"Согласовать\"}", id, 1, key: "checklist-0001");
        Assert.Equal(added.Body!.ToJsonString(), replay.Body!.ToJsonString()); Assert.Equal(2, added.Version);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("tasks", "task-check-add", "{\"text\":\"Конфликт\"}", id, 1)).Status);
        db.Call("tasks", "task-check-patch", "{\"isCompleted\":true}", id, 2, child: Id(added));
        db.Call("tasks", "task-comment-add", "{\"body\":\"Готово к проверке\"}", id, 3);
        db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = Id(file), ["linkType"] = "task_file" }.ToJsonString(), id, 4);
        var predecessor = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Собрать данные", DateTimeOffset.UtcNow);
        db.Runtime.CreateTaskStore().Add(predecessor);
        db.Call("tasks", "task-dependency-add", new JsonObject { ["predecessorId"] = predecessor.Metadata.Id }.ToJsonString(), id, 5);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("tasks", "task-dependency-add", new JsonObject { ["predecessorId"] = id }.ToJsonString(), predecessor.Metadata.Id, 1)).Status);
        var child = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Подзадача", DateTimeOffset.UtcNow, content: new() { ParentTaskId = id });
        db.Runtime.CreateTaskStore().Add(child);
        var workspace = db.Call("tasks", "task-workspace", id: id);
        Assert.True(workspace.Body!["checklist"]![0]!["isCompleted"]!.GetValue<bool>());
        Assert.Single(workspace.Body["comments"]!.AsArray()); Assert.Single(workspace.Body["files"]!.AsArray());
        Assert.Single(workspace.Body["dependencies"]!.AsArray()); Assert.Single(workspace.Body["subtasks"]!.AsArray());
        Assert.NotEmpty(workspace.Body["history"]!.AsArray());
        var loaded = db.Runtime.CreateTaskStore().Get(id, db.Organization)!;
        var updated = loaded.UpdateEditableFields(db.User, DateTimeOffset.UtcNow, "Договор согласован", null, default, default, loaded.Content.Apply("{\"description\":\"Согласовано\",\"watcherIds\":[]}"));
        db.Runtime.CreateTaskStore().Save(updated, loaded.Metadata.Version);
        var completed = updated.Complete(db.User, DateTimeOffset.UtcNow); db.Runtime.CreateTaskStore().Save(completed, updated.Metadata.Version);
        var reloaded = await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, id, db.OtherUser);
        Assert.Equal(TaskWorkStatus.Completed, reloaded!.Status); Assert.NotNull(reloaded.CompletedAtUtc); Assert.Equal("Согласовано", reloaded.Content!.Description); Assert.Empty(reloaded.Content.WatcherIds);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => db.Call("tasks", "task-check-add", "{\"text\":\"Поздно\"}", id, (int)reloaded.Version)).Status);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = Id(file), ["linkType"] = "task_file" }.ToJsonString(), id, (int)reloaded.Version)).Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task TaskCard_RealPostgres_VisibilityAndOptionsArePermissionBound()
    {
        using var db = Database.Create(); if (db is null) return;
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Закрытый проект", ["ownerUserId"] = db.User }.ToJsonString());
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Закрытая задача", DateTimeOffset.UtcNow, content: new() { ProjectId = Id(project) });
        db.Runtime.CreateTaskStore().Add(task);
        Assert.Null(await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, task.Metadata.Id, db.OtherUser));
        Assert.Empty((await db.Runtime.CreateTaskReadStore().GetPageAsync(new(db.Organization, db.OtherUser, 1))).Items);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("tasks", "task-workspace", id: task.Metadata.Id, user: db.OtherUser, admin: false)).Status);
        var options = db.Call("tasks", "task-options", permissions: ["Task.Read"], admin: false);
        Assert.Empty(options.Body!["people"]!.AsArray()); Assert.Empty(options.Body["projects"]!.AsArray());
        Assert.Empty(options.Body["files"]!.AsArray());
        var full = db.Call("tasks", "task-options", permissions: ProductApiRoutes.All.Select(r => r.Permission).Append("Employee.Read").ToArray()); Assert.Equal(2, full.Body!["people"]!.AsArray().Count);
        Assert.Single(full.Body["projects"]!.AsArray());
    }

    [Fact]
    public async System.Threading.Tasks.Task TaskCard_TransactionalValidationRejectsInvalidRelationsAndUnauthorizedProjectDetach()
    {
        using var db = Database.Create(); if (db is null) return;
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Проект", ["ownerUserId"] = db.User }.ToJsonString());
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Проверка прав", DateTimeOffset.UtcNow,
            content: new() { ProjectId = Id(project), AssigneeIds = [db.OtherUser] });
        db.Runtime.CreateTaskStore().Add(task);
        var executor = db.Runtime.CreateTaskWriteCommandExecutor();
        TaskWriteCommand Patch(Guid actor, string json) => new(db.Organization, actor, null, "PATCH_api_v1_tasks_id", Guid.NewGuid(),
            Guid.NewGuid().ToString("D"), TaskWriteRequestHasher.ComputeSha256(json), task.Metadata.Id, 1, "task.update", "TaskUpdated",
            ["description"], "{}", current =>
            {
                var updated = current!.UpdateEditableFields(actor, DateTimeOffset.UtcNow, null, null, default, default, current.Content.Apply(json));
                return new(updated, new TaskWriteHttpResult(200, new Dictionary<string, string>(), "{}", task.Metadata.Id), ["description"]);
            });
        await Assert.ThrowsAsync<KeyNotFoundException>(() => executor.ExecuteAsync(Patch(db.OtherUser, "{\"projectId\":null}")));
        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(Patch(db.User, new JsonObject { ["assigneeIds"] = new JsonArray(Guid.NewGuid().ToString()) }.ToJsonString())));
        Assert.Equal(1, db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!.Metadata.Version);
        var changed = await executor.ExecuteAsync(Patch(db.User, "{\"projectId\":null}"));
        Assert.Equal(TaskWriteCommandDisposition.Executed, changed.Disposition);
        Assert.Null(db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!.Content.ProjectId);
    }
}
