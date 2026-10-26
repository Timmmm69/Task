using System.Text.Json;
using Npgsql;
using Task.Application;
using Task.Application.Security;
using Task.Domain;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task TaskReturn_Postgres_ReplayHistoryAuditAndNoNotification()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        var command = ReturnCommand(db, task);
        var executor = db.ApiRuntime.CreateTaskWriteCommandExecutor();

        var results = await System.Threading.Tasks.Task.WhenAll(
            executor.ExecuteAsync(command), executor.ExecuteAsync(command));

        Assert.Single(results, r => r.Disposition == TaskWriteCommandDisposition.Executed);
        Assert.Single(results, r => r.Disposition == TaskWriteCommandDisposition.Replayed);
        Assert.Equal(results[0].HttpResult!.BodyJson, results[1].HttpResult!.BodyJson);
        Assert.Equal("\"v4\"", results[0].HttpResult!.Headers["ETag"]);
        AssertReturnEffects(db, task, TaskWorkStatus.InProgress);
        using var payload = JsonDocument.Parse(ScalarReturn<string>(db,
            "SELECT payload::text FROM governance.domain_events WHERE aggregate_id=$1", task.Metadata.Id));
        Assert.Equal("review", payload.RootElement.GetProperty("fromStatus").GetString());
        Assert.Equal("in_progress", payload.RootElement.GetProperty("targetStatus").GetString());
        Assert.Equal(4, payload.RootElement.GetProperty("aggregateVersion").GetInt32());
        Assert.Equal(db.User, payload.RootElement.GetProperty("actorId").GetGuid());
        Assert.Equal(command.CorrelationId, payload.RootElement.GetProperty("correlationId").GetGuid());
        Assert.False(payload.RootElement.TryGetProperty("reason", out _));
        Assert.Equal(command.CorrelationId, ScalarReturn<Guid>(db,
            "SELECT correlation_id FROM governance.audit_entries WHERE object_id=$1", task.Metadata.Id));
        Assert.Equal("task.change_status", ScalarReturn<string>(db,
            "SELECT action_code FROM governance.audit_entries WHERE object_id=$1", task.Metadata.Id));
        var history = db.Call("tasks", "history", id: task.Metadata.Id).Body!["items"]!.AsArray();
        var entry = Assert.Single(history)!;
        Assert.Equal("TaskStatusChanged", entry["action"]!.GetValue<string>());
        Assert.Equal(task.Metadata.Id, entry["objectId"]!.GetValue<Guid>());
        Assert.Equal(4, entry["objectVersion"]!.GetValue<int>());
        Assert.Equal("state_changed", entry["changeType"]!.GetValue<string>());
        Assert.Equal(command.CorrelationId, entry["correlationId"]!.GetValue<Guid>());
        Assert.Equal(DateTimeOffset.Parse(entry["occurredAt"]!.ToString()),
            DateTimeOffset.Parse(entry["changedAt"]!.ToString()));
        Assert.EndsWith("Z", entry["changedAt"]!.ToString());
        if (Environment.GetEnvironmentVariable("TASK_RETURN_CONTRACT_EVIDENCE") is { Length: > 0 } evidence)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "task-return-history.json"),
                db.Call("tasks", "history", id: task.Metadata.Id).Body!.ToJsonString());
        }
        Assert.Equal(4, entry["version"]!.GetValue<int>());
        Assert.Equal(db.User, entry["actorUserId"]!.GetValue<Guid>());
        Assert.Equal("status", Assert.Single(entry["changedFields"]!.AsArray())!.GetValue<string>());
        Assert.Equal(0, db.Count("notify.notifications"));

        Assert.Equal(TaskWriteCommandDisposition.IdempotencyKeyReused,
            (await executor.ExecuteAsync(ReturnCommand(db, task, target: TaskWorkStatus.Completed))).Disposition);
        AssertReturnEffects(db, task, TaskWorkStatus.InProgress);

        // Replay also rechecks effective permissions and does not append side effects.
        db.Sql("DELETE FROM iam.user_roles WHERE user_account_id=$1", db.User);
        await Assert.ThrowsAsync<TaskWriteSessionException>(() => executor.ExecuteAsync(command));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => executor.ExecuteAsync(ReturnCommand(db, task)));
        AssertReturnEffects(db, task, TaskWorkStatus.InProgress);
    }

    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task TaskReturn_Postgres_ConcurrentDifferentCommandsAndStaleVersion()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        var executor = db.ApiRuntime.CreateTaskWriteCommandExecutor();
        var first = executor.ExecuteAsync(ReturnCommand(db, task, key: "return-concurrent-01"));
        var second = executor.ExecuteAsync(ReturnCommand(db, task,
            target: TaskWorkStatus.Completed, key: "complete-concurrent-01"));
        var errors = await System.Threading.Tasks.Task.WhenAll(
            Record.ExceptionAsync(async () => { await first; }),
            Record.ExceptionAsync(async () => { await second; }));
        Assert.Single(errors, e => e is null);
        Assert.IsType<TaskLifecycleConcurrencyException>(Assert.Single(errors, e => e is not null));
        var current = db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!;
        Assert.Contains(current.WorkStatus, new[] { TaskWorkStatus.InProgress, TaskWorkStatus.Completed });
        AssertReturnEffects(db, task, current.WorkStatus);
        await Assert.ThrowsAsync<TaskLifecycleConcurrencyException>(() =>
            executor.ExecuteAsync(ReturnCommand(db, task, key: "return-stale-01")));
        AssertReturnEffects(db, task, current.WorkStatus);
    }

    [RequiresPostgresTheory]
    [InlineData("observer")]
    [InlineData("outsider")]
    [InlineData("watcher")]
    [InlineData("explicit-deny")]
    public async System.Threading.Tasks.Task TaskReturn_Postgres_PermissionAndObjectScope(string scenario)
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        if (scenario == "observer")
        {
            db.Sql("DELETE FROM iam.user_roles WHERE user_account_id=$1", db.User);
            db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.User, db.Role("system_observer"));
        }
        if (scenario == "explicit-deny")
        {
            db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'task.changestatus','deny')", db.Role("system_observer"));
            db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.User, db.Role("system_observer"));
        }
        if (scenario is "outsider" or "watcher")
            db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.OtherUser, db.Role("system_employee"));
        if (scenario == "watcher")
        {
            db.Sql("UPDATE work.tasks SET card_content=jsonb_set(card_content,'{watcherIds}',jsonb_build_array($2::text)) WHERE id=$1",
                task.Metadata.Id, db.OtherUser.ToString("D"));
            Assert.NotNull(await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, task.Metadata.Id, db.OtherUser));
        }
        var command = ReturnCommand(db, task, otherActor: scenario is "outsider" or "watcher");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => db.ApiRuntime.CreateTaskWriteCommandExecutor().ExecuteAsync(command));
        Assert.Equal(TaskWorkStatus.Review, db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!.WorkStatus);
        Assert.Equal(0, db.Count("governance.audit_entries"));
        Assert.Equal(0, db.Count("governance.domain_events"));
        Assert.Equal(0, db.Count("governance.outbox_messages"));
        Assert.Equal(0, db.Count("iam.idempotency_records"));
    }

    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task TaskReturn_Postgres_EventFailureRollsBackTaskAuditAndIdempotency()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        db.Sql("""
            INSERT INTO governance.domain_events(id,organization_id,aggregate_id,aggregate_type,
                aggregate_version,event_type,actor_user_id,correlation_id,operation_id,idempotency_key,payload)
            VALUES($1,$2,$3,'task',4,'TaskStatusChanged',$4,$5,'seed.return.conflict','seed-return-conflict','{}'::jsonb)
            """, Guid.NewGuid(), db.Organization, task.Metadata.Id, db.User, Guid.NewGuid());

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.ApiRuntime.CreateTaskWriteCommandExecutor().ExecuteAsync(ReturnCommand(db, task)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        var current = db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!;
        Assert.Equal(TaskWorkStatus.Review, current.WorkStatus);
        Assert.Equal(3, current.Metadata.Version);
        Assert.Equal(0, db.Count("governance.audit_entries"));
        Assert.Equal(1, db.Count("governance.domain_events"));
        Assert.Equal(0, db.Count("governance.outbox_messages"));
        Assert.Equal(0, db.Count("iam.idempotency_records"));
    }

    private static TaskAggregate SeedReturnTask(Database db)
    {
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.User, db.Role("system_employee"));
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var store = db.Runtime.CreateTaskStore();
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Return under review", now);
        store.Add(task);
        task = task.Start(db.User, now.AddSeconds(1)); store.Save(task, 1);
        task = task.SubmitForReview(db.User, now.AddSeconds(2)); store.Save(task, 2);
        return task;
    }

    private static TaskWriteCommand ReturnCommand(Database db, TaskAggregate task,
        TaskWorkStatus target = TaskWorkStatus.InProgress, string key = "return-command-01", bool otherActor = false)
    {
        var actor = otherActor ? db.OtherUser : db.User;
        var scopeVersion = ScalarReturn<long>(db,
            "SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=$1", actor);
        var context = new AuthenticatedRequestContext(actor,
            otherActor ? db.OtherSession : db.Session, db.Organization, 1, scopeVersion, Guid.NewGuid().ToString("D"), "task142");
        var targetValue = target == TaskWorkStatus.InProgress ? "in_progress" : "completed";
        return new TaskStatusTransitionCommandService(db.ApiRuntime.CreateTaskWriteCommandExecutor())
            .CreateCommand(context, key, JsonSerializer.Serialize(new { targetStatus = targetValue }),
                task.Metadata.Id, task.Metadata.Version, target, updated => new TaskWriteHttpResult(200,
                    new Dictionary<string, string> { ["ETag"] = $"\"v{updated.Metadata.Version}\"" },
                    JsonSerializer.Serialize(new { status = targetValue, version = updated.Metadata.Version }), task.Metadata.Id));
    }

    private static void AssertReturnEffects(Database db, TaskAggregate original, TaskWorkStatus status)
    {
        var current = db.Runtime.CreateTaskStore().Get(original.Metadata.Id, db.Organization)!;
        Assert.Equal(status, current.WorkStatus);
        Assert.Equal(4, current.Metadata.Version);
        Assert.Equal(1, db.Count("governance.audit_entries"));
        Assert.Equal(1, db.Count("governance.domain_events"));
        Assert.Equal(1, db.Count("governance.outbox_messages"));
        Assert.Equal(1, db.Count("iam.idempotency_records"));
    }

    private static T ScalarReturn<T>(Database db, string sql, Guid id)
    {
        using var command = db.Source.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return (T)command.ExecuteScalar()!;
    }
}
