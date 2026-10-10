using System.Text.Json.Nodes;
using Task.Application;
using Task.Domain;
namespace Task.Tests;
public sealed partial class PostgresProductApiTests
{
    [Fact]
    public async System.Threading.Tasks.Task ContextTaskCreation_TransactionalRoundTripAndStaleRelations()
    {
        using var db = Database.Create(); if (db is null) return;
        var projectId = Id(db.Call("projects", "create", new JsonObject { ["name"] = "Проект", ["ownerUserId"] = db.User }.ToJsonString()));
        var parent = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Родитель", DateTimeOffset.UtcNow);
        var store = db.Runtime.CreateTaskStore(); store.Add(parent);
        var executor = db.Runtime.CreateTaskWriteCommandExecutor();
        async System.Threading.Tasks.Task<Guid> Create(TaskCardContent card)
        {
            var id = Guid.NewGuid(); var json = card.ToJson();
            var command = new TaskWriteCommand(db.Organization, db.User, null, "POST_api_v1_tasks", Guid.NewGuid(), Guid.NewGuid().ToString("D"),
                TaskWriteRequestHasher.ComputeSha256(json), id, null, "task.create", "TaskCreated", ["title"], "{}", _ =>
                {
                    var task = TaskAggregate.Create(id, db.Organization, db.User, "Новая задача", DateTimeOffset.UtcNow, content: card);
                    return new(task, new TaskWriteHttpResult(201, new Dictionary<string, string>(), "{}", id), ["title"]);
                });
            Assert.Equal(TaskWriteCommandDisposition.Executed, (await executor.ExecuteAsync(command)).Disposition);
            return id;
        }
        var projectTask = await Create(new() { ProjectId = projectId });
        var child = await Create(new() { ParentTaskId = parent.Metadata.Id });
        var reader = db.Runtime.CreateTaskReadStore();
        Assert.Equal(projectId, (await reader.GetVisibleByIdAsync(db.Organization, projectTask, db.User))!.Content!.ProjectId);
        var loadedChild = (await reader.GetVisibleByIdAsync(db.Organization, child, db.User))!;
        Assert.Equal(parent.Metadata.Id, loadedChild.Content!.ParentTaskId); Assert.Null(loadedChild.Content.ProjectId);
        Assert.Null(await reader.GetVisibleByIdAsync(Guid.NewGuid(), child, db.User));
        Assert.Contains(db.Call("tasks", "task-workspace", id: parent.Metadata.Id).Body!["subtasks"]!.AsArray(), n => n!["id"]!.ToString() == child.ToString());
        var count = db.Count("work.tasks");
        await Assert.ThrowsAsync<ArgumentException>(() => Create(new() { ParentTaskId = child }));
        var cancelled = parent.Cancel(db.User, DateTimeOffset.UtcNow); store.Save(cancelled, parent.Metadata.Version);
        await Assert.ThrowsAsync<ArgumentException>(() => Create(new() { ParentTaskId = parent.Metadata.Id }));
        db.Sql("UPDATE core.objects SET lifecycle_state='archived',archived_at=statement_timestamp(),updated_at=statement_timestamp() WHERE id=$1;", projectId);
        await Assert.ThrowsAsync<ArgumentException>(() => Create(new() { ProjectId = projectId }));
        Assert.Equal(count, db.Count("work.tasks")); Assert.Equal(loadedChild.Version, (await reader.GetVisibleByIdAsync(db.Organization, child, db.User))!.Version);
    }
}
