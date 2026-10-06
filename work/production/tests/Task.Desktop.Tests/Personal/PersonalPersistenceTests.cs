using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-personal-tests", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);
    private SqliteConnection OpenSql()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private void Sql(string text)
    {
        using var connection = OpenSql(); using var command = connection.CreateCommand(); command.CommandText = text; command.ExecuteNonQuery();
    }
    private static DesktopTaskDto Success(DesktopTaskWriteResult<DesktopTaskDto> result) => Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(result).Value;
    [Fact]
    public void SharedTypedCommands_AllowOptionalDeadlineConsistentWithDomainSchedule()
    {
        var start = new DateTimeOffset(2026, 10, 8, 11, 20, 0, TimeSpan.Zero);
        var create = new DesktopCreateTaskCommand("Planned", DesktopTaskPriority.Normal, start);
        Assert.Equal(TaskSchedule.Create(start, null).StartsAtUtc, create.StartAtUtc);
        Assert.Null(create.DeadlineAtUtc);
        var clear = new DesktopPatchTaskCommand(Guid.NewGuid(), 1,
            startAtUtc: DesktopTaskField<DateTimeOffset?>.From(start), deadlineAtUtc: DesktopTaskField<DateTimeOffset?>.From(null));
        Assert.Null(clear.DeadlineAtUtc.Value);
        Assert.Throws<ArgumentException>(() => new DesktopCreateTaskCommand("Bad", DesktopTaskPriority.Normal, start, start.AddMinutes(-1)));
    }

    [Fact]
    public void CreateCloseReopen_PreservesEveryValueAndActorWithoutCorporateFiles()
    {
        var start = new DateTimeOffset(2026, 10, 8, 11, 20, 0, TimeSpan.Zero);
        DesktopTaskDto saved;
        using (var store = new PersonalTaskStore(Paths))
        {
            var parent = store.Create(new("Parent", DesktopTaskPriority.Normal));
            var card = new TaskCardContent
            {
                Description = "Точный текст\nвторая строка",
                ParentTaskId = parent.Id,
                ScheduledDate = new(2026, 10, 8),
                StartTimeLocal = new(11, 20),
                ScheduleTimeZone = "UTC",
                PlannedDurationMinutes = 45
            };
            saved = store.Create(new("  Local task  ", DesktopTaskPriority.Critical, start, start.AddDays(1), card));
            saved = store.Patch(new(saved.Id, saved.Version, title: DesktopTaskField<string>.From("Edited")));
            saved = store.Transition(new(saved.Id, saved.Version, DesktopTaskStatus.InProgress));
        }
        using var reopened = new PersonalTaskStore(Paths);
        var found = reopened.Get(saved.Id)!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(saved), System.Text.Json.JsonSerializer.Serialize(found));
        Assert.Equal(saved.Card!.ToJson(), found.Card!.ToJson()); Assert.Equal(3, found.Version);
        Assert.NotEqual(Guid.Empty, found.AuthorUserId); Assert.Equal(Guid.Empty, found.OrganizationId);
        Assert.Empty(found.AssigneeIds); Assert.Empty(found.WatcherIds);
        Assert.False(File.Exists(Path.Combine(_root, "credentials.bin"))); Assert.False(File.Exists(Path.Combine(_root, "server-settings.json")));
        Assert.Equal(Path.Combine(_root, "Personal", "tasks.db"), Paths.DatabasePath);
    }

    [Fact]
    public void DateOnlyEditAndClear_PreserveVersionAndScheduleAcrossReopen()
    {
        Guid id;
        using (var store = new PersonalTaskStore(Paths))
        {
            var task = store.Create(new("Date", DesktopTaskPriority.High, card: new() { ScheduledDate = new(2026, 10, 12) })); id = task.Id;
            task = store.Patch(new(id, 1, priority: DesktopTaskField<DesktopTaskPriority>.From(DesktopTaskPriority.Low),
                cardPatch: "{\"description\":\"hello\",\"plannedDurationMinutes\":30}"));
            Assert.Equal(2, task.Version); Assert.Null(task.StartAtUtc);
        }
        using var reopened = new PersonalTaskStore(Paths);
        var restored = reopened.Get(id)!;
        Assert.Equal(new DateOnly(2026, 10, 12), restored.Card!.ScheduledDate); Assert.Equal("hello", restored.Card.Description);
        Assert.Equal(30, restored.Card.PlannedDurationMinutes); Assert.Equal(DesktopTaskPriority.Low, restored.Priority);
        var cleared = reopened.Patch(new(id, 2, cardPatch: "{\"scheduledDate\":null,\"description\":null}"));
        Assert.Null(cleared.Card!.ScheduledDate); Assert.Null(cleared.Card.Description); Assert.Equal(3, cleared.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task SharedOwnerClients_RejectStaleAndInvalidWritesWithoutChangingStoredValues()
    {
        using var first = new PersonalTaskStore(Paths);
        Assert.Throws<PersonalStoreBusyException>(() => new PersonalTaskStore(Paths));
        var a = new PersonalTasksClient(first); var b = new PersonalTasksClient(first);
        var task = Success(await a.CreateTaskAsync(new("Before", DesktopTaskPriority.Normal)));
        Success(await b.PatchTaskAsync(new(task.Id, 1, title: DesktopTaskField<string>.From("After"))));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict>(await a.PatchTaskAsync(new(task.Id, 1, title: DesktopTaskField<string>.From("Stale"))));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await a.PatchTaskAsync(new(task.Id, 2, cardPatch: "{\"parentTaskId\":\"" + task.Id + "\"}")));
        Assert.Equal("After", first.Get(task.Id)!.Title); Assert.Equal(2, first.Get(task.Id)!.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task Hierarchy_OnlyOneLevelIncludingMovesAndTerminalParents()
    {
        using var store = new PersonalTaskStore(Paths); var client = new PersonalTasksClient(store);
        var parent = Success(await client.CreateTaskAsync(new("Parent", DesktopTaskPriority.Normal)));
        var child = Success(await client.CreateTaskAsync(new("Child", DesktopTaskPriority.Normal, card: new() { ParentTaskId = parent.Id })));
        Assert.Equal(parent.Id, child.Card!.ParentTaskId);
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await client.CreateTaskAsync(new("Grandchild", DesktopTaskPriority.Normal, card: new() { ParentTaskId = child.Id })));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await client.PatchTaskAsync(new(parent.Id, 1, cardPatch: "{\"parentTaskId\":\"" + child.Id + "\"}")));
        var other = Success(await client.CreateTaskAsync(new("Other", DesktopTaskPriority.Normal)));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await client.PatchTaskAsync(new(parent.Id, 1, cardPatch: "{\"parentTaskId\":\"" + other.Id + "\"}")));
        parent = Success(await client.TransitionTaskAsync(new(parent.Id, 1, DesktopTaskStatus.Completed)));
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await client.CreateTaskAsync(new("Late child", DesktopTaskPriority.Normal, card: new() { ParentTaskId = parent.Id })));
        Assert.Equal(3, store.List().Count);
    }

    [Theory]
    [InlineData(DesktopTaskStatus.InProgress)]
    [InlineData(DesktopTaskStatus.Completed)]
    [InlineData(DesktopTaskStatus.Cancelled)]
    public async System.Threading.Tasks.Task ValidTransitions_PersistAndTerminalEditingIsRejected(DesktopTaskStatus target)
    {
        DesktopTaskDto saved;
        using (var store = new PersonalTaskStore(Paths))
        {
            var client = new PersonalTasksClient(store); var task = Success(await client.CreateTaskAsync(new("Status", DesktopTaskPriority.Normal)));
            Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition>(await client.TransitionTaskAsync(new(task.Id, 1, DesktopTaskStatus.Review)));
            saved = Success(await client.TransitionTaskAsync(new(task.Id, 1, target)));
            if (target != DesktopTaskStatus.InProgress)
                Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition>(await client.PatchTaskAsync(new(task.Id, saved.Version, title: DesktopTaskField<string>.From("No"))));
            else saved = Success(await client.TransitionTaskAsync(new(task.Id, 2, DesktopTaskStatus.Review)));
        }
        using var reopened = new PersonalTaskStore(Paths); var found = reopened.Get(saved.Id)!;
        Assert.Equal(saved.Status, found.Status); Assert.Equal(saved.Version, found.Version); Assert.Equal(saved.CompletedAtUtc, found.CompletedAtUtc);
    }

    [Fact]
    public async System.Threading.Tasks.Task Checklist_ReopenToggleRemove_AndRollbackAfterInsertOnSqlFailure()
    {
        DesktopTaskDto task;
        using (var store = new PersonalTaskStore(Paths))
        {
            var client = new PersonalTasksClient(store); task = Success(await client.CreateTaskAsync(new("Checklist", DesktopTaskPriority.Normal)));
            Sql("CREATE TRIGGER reject_task_update BEFORE UPDATE ON tasks BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
            Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable>(await client.WriteChecklistAsync(task.Id, 1, text: "Must roll back"));
            Assert.Empty(store.Checklist(task.Id)); Assert.Equal(1, store.Get(task.Id)!.Version);
            Sql("DROP TRIGGER reject_task_update;");
            task = Success(await client.WriteChecklistAsync(task.Id, 1, text: "Persistent item"));
            Assert.Equal(2, task.Version);
        }
        using var reopened = new PersonalTaskStore(Paths); var item = Assert.Single(reopened.Checklist(task.Id));
        Assert.Equal("Persistent item", item.Text); Assert.False(item.Completed);
        var updated = reopened.WriteChecklist(task.Id, 2, item.Id, null, true, false); Assert.Equal(3, updated.Version);
        Assert.True(Assert.Single(reopened.Checklist(task.Id)).Completed);
        reopened.WriteChecklist(task.Id, 3, item.Id, null, null, true); Assert.Empty(reopened.Checklist(task.Id));
    }

    [Fact]
    public void SupportedSchemaAndMigration_ReopenPreserveExistingTask()
    {
        DesktopTaskDto task;
        using (var store = new PersonalTaskStore(Paths)) task = store.Create(new("Upgrade", DesktopTaskPriority.Normal));
        Sql(PersonalPlanningTests.RemovePlanningSchema + " DROP TABLE checklist; PRAGMA user_version=1;");
        using (var upgraded = new PersonalTaskStore(Paths)) { Assert.Equal(task.Id, Assert.Single(upgraded.List()).Id); Assert.Empty(upgraded.Checklist(task.Id)); }
        using (var reopened = new PersonalTaskStore(Paths)) Assert.Equal(1, reopened.Get(task.Id)!.Version);
        using var connection = OpenSql(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, command.ExecuteScalar());
    }

    [Fact]
    public void FailedMigration_RollsBackAndNeverResetsData()
    {
        Guid id;
        using (var store = new PersonalTaskStore(Paths)) id = store.Create(new("Keep", DesktopTaskPriority.Normal)).Id;
        Sql("DROP TABLE checklist; PRAGMA user_version=1; CREATE TABLE checklist(id TEXT);");
        Assert.Throws<InvalidDataException>(() => new PersonalTaskStore(Paths));
        using var connection = OpenSql(); using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;"; Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT id FROM tasks;"; Assert.Equal(id.ToString("D"), command.ExecuteScalar());
    }

    [Fact]
    public void FutureSchemaAndUnknownDatabase_ArePreservedByteForByte()
    {
        using (var store = new PersonalTaskStore(Paths)) store.Create(new("Keep", DesktopTaskPriority.Normal));
        Sql("PRAGMA user_version=99;"); var before = File.ReadAllBytes(Paths.DatabasePath);
        Assert.Throws<InvalidOperationException>(() => new PersonalTaskStore(Paths));
        Assert.Equal(before, File.ReadAllBytes(Paths.DatabasePath));
        Sql("PRAGMA user_version=0; PRAGMA application_id=0;"); before = File.ReadAllBytes(Paths.DatabasePath);
        Assert.Throws<InvalidOperationException>(() => new PersonalTaskStore(Paths)); Assert.Equal(before, File.ReadAllBytes(Paths.DatabasePath));
    }

    [Fact]
    public async System.Threading.Tasks.Task DomainValidation_NoCorporateParticipantsNoUnavailableRelationsNoBadSchedule()
    {
        using var store = new PersonalTaskStore(Paths); var client = new PersonalTasksClient(store);
        Assert.Throws<ArgumentException>(() => new DesktopCreateTaskCommand(" ", DesktopTaskPriority.Normal));
        Assert.Throws<ArgumentException>(() => new DesktopCreateTaskCommand(new string('x', 501), DesktopTaskPriority.Normal));
        foreach (var card in new TaskCardContent[] { new() { RequesterUserId = Guid.NewGuid() }, new() { AssigneeIds = [Guid.NewGuid()] },
            new() { ProjectId = Guid.NewGuid() }, new() { PrimaryCounterpartyObjectId = Guid.NewGuid() }, new() { PlannedDurationMinutes = 0 },
            new() { ScheduledDate = new(2026, 10, 12), StartTimeLocal = new(9, 30), ScheduleTimeZone = "UTC" } })
            Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure>(await client.CreateTaskAsync(new("Invalid", DesktopTaskPriority.Normal, card: card)));
        Assert.Empty(store.List());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
