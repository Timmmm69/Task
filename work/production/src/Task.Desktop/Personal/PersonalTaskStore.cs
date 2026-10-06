using System.Globalization;
using Microsoft.Data.Sqlite;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Personal;

internal sealed class PersonalVersionConflictException : Exception;
internal sealed class PersonalTaskNotFoundException : Exception;
internal sealed class PersonalTransitionException : Exception;

/// <summary>Local task transactions. No corporate identity, cache or permissions.</summary>
public sealed partial class PersonalTaskStore : IDisposable
{
    private readonly PersonalDatabase _database;
    private readonly PersonalStoreOwnership _ownership;
    private readonly bool _ownsLease;
    private readonly object _gate = new();
    private bool _disposed;
    private readonly Guid _actor;
    private readonly TimeProvider _clock;
    public Guid LocalActorId => _actor;
    private const string Columns = "id,version,created_at,updated_at,title,description,status,priority,start_at,deadline,scheduled_date,start_time,time_zone,duration,project_id,parent_task_id,counterparty_id,completed_at";

    public PersonalTaskStore(PersonalDataPaths paths, TimeProvider? clock = null) : this(paths, clock, null) { }
    internal PersonalTaskStore(PersonalDataPaths paths, TimeProvider? clock, PersonalStoreOwnership? ownership, Action<string>? fault = null, bool restoring = false)
    {
        _ownership = ownership ?? PersonalStoreOwnership.Acquire(paths);
        _ownsLease = ownership is null;
        _ownership.Verify(paths);
        _clock = clock ?? TimeProvider.System;
        try
        {
            _database = new(paths, fault, restoring);
            using var command = Command("SELECT value FROM personal_metadata WHERE key='PersonalActorId';");
            _actor = Guid.Parse((string)command.ExecuteScalar()!);
        }
        catch { _database?.Dispose(); if (_ownsLease) _ownership.Dispose(); throw; }
    }

    internal void Snapshot(string destination) => Locked(() =>
    {
        using var target = PersonalDatabaseValidation.Open(destination, SqliteOpenMode.ReadWriteCreate);
        _database.Connection.BackupDatabase(target);
        return true;
    });
    internal void LimitPagesForTest() => Locked(() =>
    {
        using var command = Command("PRAGMA page_count;");
        var count = command.ExecuteScalar();
        command.CommandText = $"PRAGMA max_page_count={count};";
        command.ExecuteNonQuery(); return true;
    });

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null, params (string, object?)[] parameters)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = sql; command.Transaction = transaction;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private T Locked<T>(Func<T> action)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return action(); }
    }
    private static string? Instant(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset? ReadInstant(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    private static Guid? ReadId(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));
    private DesktopTaskDto Read(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Empty, r.GetInt64(1), ReadInstant(r, 2), ReadInstant(r, 3),
        r.GetString(4), _actor, (DesktopTaskStatus)r.GetInt32(6), (DesktopTaskPriority)r.GetInt32(7),
        ReadInstant(r, 8), ReadInstant(r, 9), [], [], null,
        new TaskCardContent
        {
            Description = r.IsDBNull(5) ? null : r.GetString(5),
            ScheduledDate = r.IsDBNull(10) ? null : DateOnly.Parse(r.GetString(10), CultureInfo.InvariantCulture),
            StartTimeLocal = r.IsDBNull(11) ? null : TimeOnly.Parse(r.GetString(11), CultureInfo.InvariantCulture),
            ScheduleTimeZone = r.IsDBNull(12) ? null : r.GetString(12),
            PlannedDurationMinutes = r.IsDBNull(13) ? null : r.GetInt32(13),
            ProjectId = ReadId(r, 14),
            ParentTaskId = ReadId(r, 15),
            PrimaryCounterpartyObjectId = ReadId(r, 16),
        }, ReadInstant(r, 17));

    private DesktopTaskDto? Get(Guid id, SqliteTransaction? tx)
    {
        using var command = Command($"SELECT {Columns} FROM tasks WHERE id=$id AND archived_at IS NULL AND trashed_at IS NULL;", tx, ("$id", id.ToString("D")));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }
    public DesktopTaskDto? Get(Guid id) => Locked(() => Get(id, null));
    public IReadOnlyList<DesktopTaskDto> List() => Locked<IReadOnlyList<DesktopTaskDto>>(() =>
    {
        using var command = Command($"SELECT {Columns} FROM tasks WHERE archived_at IS NULL AND trashed_at IS NULL ORDER BY created_at DESC,id;");
        using var reader = command.ExecuteReader(); var tasks = new List<DesktopTaskDto>();
        while (reader.Read()) tasks.Add(Read(reader));
        return tasks;
    });

    private void Validate(DesktopTaskDto task, SqliteTransaction tx)
    {
        _ = TaskRules.NormalizeTitle(task.Title);
        if (!Enum.IsDefined(task.Priority)) throw new ArgumentException("Unknown priority.");
        _ = TaskSchedule.Create(task.StartAtUtc, task.DeadlineAtUtc);
        var card = task.Card ?? new(); card.Validate(task.StartAtUtc, allowEarlierAmbiguousInstant: true);
        if (card.RequesterUserId is not null || card.AssigneeIds.Count != 0 || card.WatcherIds.Count != 0)
            throw new ArgumentException("Corporate participants are unavailable in Personal.");
        if (Get(task.Id, tx)?.Card?.ProjectId != card.ProjectId) ValidateProjectLink(card.ProjectId, tx);
        if (card.PrimaryCounterpartyObjectId is not null)
            throw new ArgumentException("Personal contacts are not available yet.");
        if (card.ParentTaskId is { } parentId)
        {
            var parent = Get(parentId, tx);
            if (parent is null || parentId == task.Id || parent.Card?.ParentTaskId is not null || TaskRules.IsTerminal((TaskWorkStatus)parent.Status))
                throw new ArgumentException("Subtasks require an active top-level parent.");
            using var children = Command("SELECT 1 FROM tasks WHERE parent_task_id=$id LIMIT 1;", tx, ("$id", task.Id.ToString("D")));
            if (children.ExecuteScalar() is not null) throw new ArgumentException("Only one subtask level is supported.");
        }
    }
    private void Save(DesktopTaskDto task, SqliteTransaction tx, bool create)
    {
        var card = task.Card ?? new();
        var sql = create
            ? $"INSERT INTO tasks ({Columns}) VALUES ($id,$version,$created,$updated,$title,$description,$status,$priority,$start,$deadline,$date,$time,$zone,$duration,$project,$parent,$counterparty,$completed);"
            : "UPDATE tasks SET version=$version,updated_at=$updated,title=$title,description=$description,status=$status,priority=$priority,start_at=$start,deadline=$deadline,scheduled_date=$date,start_time=$time,time_zone=$zone,duration=$duration,project_id=$project,parent_task_id=$parent,counterparty_id=$counterparty,completed_at=$completed WHERE id=$id AND version=$expected;";
        using var command = Command(sql, tx,
            ("$id", task.Id.ToString("D")), ("$version", task.Version), ("$expected", task.Version - 1),
            ("$created", Instant(task.CreatedAtUtc)), ("$updated", Instant(task.UpdatedAtUtc)),
            ("$title", task.Title), ("$description", card.Description), ("$status", (int)task.Status), ("$priority", (int)task.Priority),
            ("$start", Instant(task.StartAtUtc)), ("$deadline", Instant(task.DeadlineAtUtc)),
            ("$date", card.ScheduledDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$time", card.StartTimeLocal?.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
            ("$zone", card.ScheduleTimeZone), ("$duration", card.PlannedDurationMinutes),
            ("$project", card.ProjectId?.ToString("D")), ("$parent", card.ParentTaskId?.ToString("D")),
            ("$counterparty", card.PrimaryCounterpartyObjectId?.ToString("D")), ("$completed", Instant(task.CompletedAtUtc)));
        if (command.ExecuteNonQuery() != 1) throw new PersonalVersionConflictException();
    }

    public DesktopTaskDto Create(DesktopCreateTaskCommand input) => Locked(() =>
    {
        var now = _clock.GetUtcNow();
        var task = new DesktopTaskDto(Guid.NewGuid(), Guid.Empty, 1, now, now, TaskRules.NormalizeTitle(input.Title), _actor,
            DesktopTaskStatus.New, input.Priority, input.StartAtUtc, input.DeadlineAtUtc, [], [], null, input.Card ?? new());
        using var tx = _database.Connection.BeginTransaction();
        Validate(task, tx); Save(task, tx, true); tx.Commit(); return task;
    });
    private DesktopTaskDto Current(Guid id, long version, SqliteTransaction tx)
    {
        var task = Get(id, tx) ?? throw new PersonalTaskNotFoundException();
        if (task.Version != version) throw new PersonalVersionConflictException();
        if (TaskRules.IsTerminal((TaskWorkStatus)task.Status)) throw new PersonalTransitionException();
        return task;
    }
    public DesktopTaskDto Patch(DesktopPatchTaskCommand input) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction();
        var old = Current(input.Id, input.ExpectedVersion, tx);
        var task = old with
        {
            Title = input.Title.IsSpecified ? TaskRules.NormalizeTitle(input.Title.Value!) : old.Title,
            Priority = input.Priority.IsSpecified ? input.Priority.Value : old.Priority,
            StartAtUtc = input.StartAtUtc.IsSpecified ? input.StartAtUtc.Value : old.StartAtUtc,
            DeadlineAtUtc = input.DeadlineAtUtc.IsSpecified ? input.DeadlineAtUtc.Value : old.DeadlineAtUtc,
            Card = (old.Card ?? new()).Apply(input.CardPatch),
            Version = checked(old.Version + 1),
            UpdatedAtUtc = _clock.GetUtcNow(),
        };
        Validate(task, tx); Save(task, tx, false); tx.Commit(); return task;
    });
    public DesktopTaskDto Transition(DesktopTransitionTaskCommand input) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var old = Current(input.Id, input.ExpectedVersion, tx);
        if (!TaskRules.CanTransition((TaskWorkStatus)old.Status, (TaskWorkStatus)input.TargetStatus)) throw new PersonalTransitionException();
        var now = _clock.GetUtcNow();
        var task = old with
        {
            Status = input.TargetStatus,
            Version = checked(old.Version + 1),
            UpdatedAtUtc = now,
            CompletedAtUtc = input.TargetStatus == DesktopTaskStatus.Completed ? now : null
        };
        Save(task, tx, false); tx.Commit(); return task;
    });

    public IReadOnlyList<TaskWorkspaceItem> Checklist(Guid taskId) => Locked<IReadOnlyList<TaskWorkspaceItem>>(() =>
    {
        using var command = Command("SELECT id,text,completed FROM checklist WHERE task_id=$task ORDER BY sort_order,id;", null, ("$task", taskId.ToString("D")));
        using var reader = command.ExecuteReader(); var items = new List<TaskWorkspaceItem>();
        while (reader.Read()) items.Add(new(Guid.Parse(reader.GetString(0)), null, reader.GetString(1), reader.GetBoolean(2)));
        return items;
    });
    public DesktopTaskDto WriteChecklist(Guid taskId, long version, Guid? itemId, string? text, bool? completed, bool remove) => Locked(() =>
    {
        if (itemId is null && (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 2000)) throw new ArgumentException("Checklist text must contain 1 to 2000 characters.");
        using var tx = _database.Connection.BeginTransaction(); var task = Current(taskId, version, tx);
        var sql = itemId is null ? "INSERT INTO checklist VALUES ($id,$task,$text,0,COALESCE((SELECT MAX(sort_order)+1 FROM checklist WHERE task_id=$task),0));"
            : remove ? "DELETE FROM checklist WHERE id=$id AND task_id=$task;"
            : "UPDATE checklist SET completed=$completed WHERE id=$id AND task_id=$task;";
        using var command = Command(sql, tx, ("$id", (itemId ?? Guid.NewGuid()).ToString("D")), ("$task", taskId.ToString("D")), ("$text", text?.Trim()), ("$completed", completed == true ? 1 : 0));
        if (command.ExecuteNonQuery() != 1) throw new PersonalTaskNotFoundException();
        var updated = task with { Version = checked(task.Version + 1), UpdatedAtUtc = _clock.GetUtcNow() };
        Save(updated, tx, false); tx.Commit(); return updated;
    });
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; try { _database.Dispose(); } finally { if (_ownsLease) _ownership.Dispose(); } }
    }
}
