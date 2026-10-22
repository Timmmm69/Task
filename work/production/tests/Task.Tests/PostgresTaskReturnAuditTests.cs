using Task.Application;
using Task.Application.Security;
using Task.Domain;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public async System.Threading.Tasks.Task TaskReturn_Audit_FreshListSearchAndFieldsAreConsistent()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        var command = ReturnCommand(db, task);
        await db.ApiRuntime.CreateTaskWriteCommandExecutor().ExecuteAsync(command);
        var current = db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!;
        Assert.Equal(task.Title, current.Title);
        Assert.Equal(task.Priority, current.Priority);
        Assert.Equal(task.Schedule, current.Schedule);
        Assert.Equal(task.Content.ToJson(), current.Content.ToJson());
        Assert.Null(current.CompletedAtUtc);
        Assert.Null(current.CompletedBy);
        var read = db.ApiRuntime.CreateTaskReadStore();
        var page = await read.GetPageAsync(new(db.Organization, db.User, command.ExpectedAuthorizationScopeVersion!.Value));
        var row = Assert.Single(page.Items);
        Assert.Equal(TaskWorkStatus.InProgress, row.Status);
        Assert.Equal(4, row.Version);
        var detail = await read.GetVisibleByIdAsync(db.Organization, task.Metadata.Id, db.User);
        Assert.NotNull(detail);
        Assert.Equal(row.Id, detail.Id);
        Assert.Equal(row.Status, detail.Status);
        Assert.Equal(row.Version, detail.Version);
        var search = db.Call("search", "search", query: new() { ["q"] = "Return under review", ["types"] = "task" });
        Assert.Equal(task.Metadata.Id, Guid.Parse(Assert.Single(search.Body!["items"]!.AsArray())!["objectId"]!.ToString()));
        Assert.Empty(db.Call("search", "search", user: db.OtherUser, session: db.OtherSession, admin: false,
            permissions: ["Search.Use", "Task.Read"], query: new() { ["q"] = "Return under review", ["types"] = "task" }).Body!["items"]!.AsArray());
    }

    [Fact]
    public async System.Threading.Tasks.Task TaskReturn_Audit_ConcurrentCardSaveCannotOverwriteStatus()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2)", db.User, db.Role("system_manager"));
        var executor = db.ApiRuntime.CreateTaskWriteCommandExecutor();
        var context = new AuthenticatedRequestContext(db.User, db.Session, db.Organization, 1,
            ScalarReturn<long>(db, "SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=$1", db.User),
            Guid.NewGuid().ToString("D"), "audit-save");
        var patch = new TaskUpdateCommandService(executor).CreateCommand(context, "audit-save-01",
            "{\"title\":\"Edited concurrently\"}", task.Metadata.Id, task.Metadata.Version,
            new TaskUpdateModel("Edited concurrently", null, default, default),
            updated => new TaskWriteHttpResult(200, new Dictionary<string, string>(), "{}", updated.Metadata.Id)).Command;
        var errors = await System.Threading.Tasks.Task.WhenAll(
            Record.ExceptionAsync(() => executor.ExecuteAsync(ReturnCommand(db, task))),
            Record.ExceptionAsync(() => executor.ExecuteAsync(patch)));
        Assert.Single(errors, e => e is null);
        Assert.IsType<TaskLifecycleConcurrencyException>(Assert.Single(errors, e => e is not null));
        var current = db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!;
        Assert.Equal(4, current.Metadata.Version);
        Assert.True(current.WorkStatus == TaskWorkStatus.InProgress && current.Title == task.Title
            || current.WorkStatus == TaskWorkStatus.Review && current.Title == "Edited concurrently");
        foreach (var table in new[] { "governance.audit_entries", "governance.domain_events", "governance.outbox_messages", "iam.idempotency_records" })
            Assert.Equal(1, db.Count(table));
    }

    [Theory]
    [InlineData("revoked", false, SessionRequestState.SessionRevoked)]
    [InlineData("revoked", true, SessionRequestState.SessionRevoked)]
    [InlineData("idle", false, SessionRequestState.SessionExpired)]
    [InlineData("idle", true, SessionRequestState.SessionExpired)]
    [InlineData("absolute", false, SessionRequestState.SessionExpired)]
    [InlineData("absolute", true, SessionRequestState.SessionExpired)]
    [InlineData("device", false, SessionRequestState.SessionRevoked)]
    [InlineData("device", true, SessionRequestState.SessionRevoked)]
    [InlineData("credentials", false, SessionRequestState.VersionMismatch)]
    [InlineData("credentials", true, SessionRequestState.VersionMismatch)]
    [InlineData("scope", false, SessionRequestState.VersionMismatch)]
    [InlineData("scope", true, SessionRequestState.VersionMismatch)]
    [InlineData("blocked", false, SessionRequestState.AccountBlocked)]
    [InlineData("blocked", true, SessionRequestState.AccountBlocked)]
    public async System.Threading.Tasks.Task TaskReturn_Audit_InvalidSessionCannotExecuteOrReplay(
        string scenario, bool replay, SessionRequestState expectedState)
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var task = SeedReturnTask(db);
        var command = ReturnCommand(db, task);
        var executor = db.ApiRuntime.CreateTaskWriteCommandExecutor();
        if (replay) await executor.ExecuteAsync(command);

        // Authentication already succeeded when the command was constructed.
        switch (scenario)
        {
            case "revoked": db.Sql("UPDATE iam.sessions SET revoked_at=clock_timestamp(),revoke_reason='audit' WHERE id=$1", db.Session); break;
            case "idle": db.Sql("UPDATE iam.sessions SET idle_expires_at=clock_timestamp()-interval '1 second' WHERE id=$1", db.Session); break;
            case "absolute": db.Sql("UPDATE iam.sessions SET idle_expires_at=clock_timestamp()-interval '2 seconds',absolute_expires_at=clock_timestamp()-interval '1 second' WHERE id=$1", db.Session); break;
            case "device": db.Sql("UPDATE iam.devices SET revoked_at=clock_timestamp() WHERE id=$1", db.DeviceFor(db.Session)); break;
            case "credentials": db.Sql("UPDATE iam.user_accounts SET credential_version=credential_version+1 WHERE id=$1", db.User); break;
            case "scope": db.Sql("UPDATE iam.authorization_scope_versions SET version=version+1 WHERE user_account_id=$1", db.User); break;
            case "blocked": db.Sql("UPDATE iam.user_accounts SET account_status='blocked' WHERE id=$1", db.User); break;
        }
        var error = await Assert.ThrowsAsync<TaskWriteSessionException>(() => executor.ExecuteAsync(command));
        Assert.Equal(expectedState, error.State);
        var current = db.Runtime.CreateTaskStore().Get(task.Metadata.Id, db.Organization)!;
        Assert.Equal(replay ? TaskWorkStatus.InProgress : TaskWorkStatus.Review, current.WorkStatus);
        Assert.Equal(replay ? 4 : 3, current.Metadata.Version);
        foreach (var table in new[] { "governance.audit_entries", "governance.domain_events", "governance.outbox_messages", "iam.idempotency_records" })
            Assert.Equal(replay ? 1 : 0, db.Count(table));
    }
}
