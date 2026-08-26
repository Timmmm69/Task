using Npgsql;
using NpgsqlTypes;
using Task.Application;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

public sealed class PostgresTaskAggregateStore : ITaskAggregateStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresTaskAggregateStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public TaskAggregate? Get(Guid taskId, Guid organizationId)
    {
        EnsureIdentifier(taskId, nameof(taskId));
        EnsureIdentifier(organizationId, nameof(organizationId));

        using var connection = _dataSource.OpenConnection();
        return Get(connection, transaction: null, taskId, organizationId);
    }

    internal static TaskAggregate? Get(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid taskId,
        Guid organizationId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT
                o.id,
                o.organization_id,
                o.created_by,
                o.created_at,
                o.updated_by,
                o.updated_at,
                o.version,
                o.lifecycle_state,
                o.lifecycle_state_before_trash,
                o.deleted_at,
                o.deleted_by,
                o.archived_at,
                t.title,
                t.status,
                t.priority,
                t.start_at_utc,
                t.deadline_at,
                t.completed_at,
                t.completed_by
            FROM core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            WHERE o.organization_id = $1 AND o.id = $2 AND o.object_type = 'task';
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = taskId });

        using var reader = command.ExecuteReader();
        return reader.Read() ? Hydrate(reader) : null;
    }

    public void Add(TaskAggregate task)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Add(connection, transaction, task);
        transaction.Commit();
    }

    internal static void Add(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskAggregate task)
    {
        ValidateNewTask(task);

        using (var objectCommand = new NpgsqlCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, lifecycle_state, lifecycle_state_before_trash,
                version, created_at, created_by, updated_at, updated_by,
                archived_at, deleted_at, deleted_by)
            VALUES (
                $1, $2, 'task', $3, $4, $5, $6, $7, $8, $9, $10, $11, $12);
            """,
            connection,
            transaction))
        {
            AddMetadataParameters(objectCommand, task.Metadata, includeIdentity: true);
            objectCommand.ExecuteNonQuery();
        }

        using (var taskCommand = new NpgsqlCommand(
            """
            INSERT INTO work.tasks (
                id, organization_id, title, status, priority, start_at_utc,
                deadline_at, completed_at, completed_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9);
            """,
            connection,
            transaction))
        {
            AddTaskParameters(taskCommand, task, includeIdentity: true);
            taskCommand.ExecuteNonQuery();
        }

    }

    public void Save(TaskAggregate task, int expectedVersion)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Save(connection, transaction, task, expectedVersion);
        transaction.Commit();
    }

    internal static void Save(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskAggregate task,
        int expectedVersion)
    {
        ValidateSavedTask(task, expectedVersion);
        using var command = new NpgsqlCommand(
            """
            WITH updated_object AS (
                UPDATE core.objects
                SET lifecycle_state = $3,
                    lifecycle_state_before_trash = $4,
                    version = $5,
                    updated_at = $6,
                    updated_by = $7,
                    archived_at = $8,
                    deleted_at = $9,
                    deleted_by = $10
                WHERE organization_id = $1 AND id = $2 AND object_type = 'task' AND version = $11
                RETURNING organization_id, id
            ),
            updated_task AS (
                UPDATE work.tasks AS t
                SET title = $12,
                    status = $13,
                    priority = $14,
                    start_at_utc = $15,
                    deadline_at = $16,
                    completed_at = $17,
                    completed_by = $18
                FROM updated_object AS o
                WHERE t.organization_id = o.organization_id AND t.id = o.id
                RETURNING t.id
            )
            SELECT EXISTS (SELECT 1 FROM updated_object), EXISTS (SELECT 1 FROM updated_task);
            """,
            connection,
            transaction);

        AddSaveParameters(command, task, expectedVersion);
        using var reader = command.ExecuteReader();
        reader.Read();
        var objectUpdated = reader.GetBoolean(0);
        var taskUpdated = reader.GetBoolean(1);
        reader.Close();

        if (!objectUpdated)
        {
            ThrowMissingOrConcurrency(
                connection,
                transaction,
                task.Metadata.OrganizationId,
                task.Metadata.Id,
                expectedVersion);
        }

        if (!taskUpdated)
        {
            throw new InvalidOperationException(
                $"Persistence corruption: task row '{task.Metadata.Id}' is missing for its core object.");
        }
    }

    private static void ThrowMissingOrConcurrency(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        Guid taskId,
        int expectedVersion)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT version
            FROM core.objects
            WHERE organization_id = $1 AND id = $2 AND object_type = 'task';
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = taskId });
        var actual = command.ExecuteScalar();
        if (actual is null)
        {
            throw new KeyNotFoundException(
                $"Task '{taskId}' was not found in organization '{organizationId}'.");
        }

        throw new TaskLifecycleConcurrencyException(taskId, expectedVersion, checked((int)(long)actual));
    }

    private static void ValidateNewTask(TaskAggregate task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Metadata.Version != 1 ||
            task.Metadata.LifecycleState != EntityLifecycleState.Active ||
            task.Metadata.CreatedAtUtc != task.Metadata.UpdatedAtUtc ||
            task.Metadata.CreatedBy != task.Metadata.UpdatedBy ||
            task.WorkStatus != TaskWorkStatus.New ||
            task.Priority != TaskPriority.Normal ||
            task.Schedule.StartsAtUtc is not null ||
            task.Schedule.DeadlineUtc is not null ||
            task.CompletedAtUtc is not null ||
            task.CompletedBy is not null)
        {
            throw new ArgumentException("A new task must be in its initial version-1 aggregate state.", nameof(task));
        }
    }

    private static void ValidateSavedTask(TaskAggregate task, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), "Expected version must be positive.");
        }

        if (task.Metadata.Version != checked(expectedVersion + 1))
        {
            throw new ArgumentException(
                "The saved aggregate version must be exactly one greater than the expected version.",
                nameof(task));
        }
    }

    private static TaskAggregate Hydrate(NpgsqlDataReader reader)
    {
        var version = reader.GetInt64(6);
        if (version > int.MaxValue)
        {
            throw new InvalidOperationException("Stored task version exceeds the supported domain range.");
        }

        var metadata = SyncableEntityMetadata.Reconstitute(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetGuid(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            (int)version,
            ParseLifecycle(reader.GetString(7)),
            reader.IsDBNull(8) ? null : ParseLifecycle(reader.GetString(8)),
            ReadNullableTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            ReadNullableTimestamp(reader, 11));

        return TaskAggregate.Reconstitute(
            metadata,
            reader.GetString(12),
            ParseWorkStatus(reader.GetString(13)),
            ReadNullableTimestamp(reader, 17),
            reader.IsDBNull(18) ? null : reader.GetGuid(18),
            ParsePriority(reader.GetString(14)),
            TaskSchedule.Create(ReadNullableTimestamp(reader, 15), ReadNullableTimestamp(reader, 16)));
    }

    private static void AddMetadataParameters(
        NpgsqlCommand command,
        SyncableEntityMetadata metadata,
        bool includeIdentity)
    {
        if (includeIdentity)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.OrganizationId });
        }

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(metadata.LifecycleState) });
        AddNullableText(command, metadata.LifecycleStateBeforeTrash is null
            ? null
            : ToDatabase(metadata.LifecycleStateBeforeTrash.Value));
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = metadata.Version });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = metadata.CreatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.CreatedBy });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = metadata.UpdatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.UpdatedBy });
        AddNullableTimestamp(command, metadata.ArchivedAtUtc);
        AddNullableTimestamp(command, metadata.DeletedAtUtc);
        AddNullableGuid(command, metadata.DeletedBy);
    }

    private static void AddTaskParameters(NpgsqlCommand command, TaskAggregate task, bool includeIdentity)
    {
        if (includeIdentity)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = task.Metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = task.Metadata.OrganizationId });
        }

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = task.Title });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(task.WorkStatus) });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(task.Priority) });
        AddNullableTimestamp(command, task.Schedule.StartsAtUtc);
        AddNullableTimestamp(command, task.Schedule.DeadlineUtc);
        AddNullableTimestamp(command, task.CompletedAtUtc);
        AddNullableGuid(command, task.CompletedBy);
    }

    private static void AddSaveParameters(NpgsqlCommand command, TaskAggregate task, int expectedVersion)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = task.Metadata.OrganizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = task.Metadata.Id });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(task.Metadata.LifecycleState) });
        AddNullableText(command, task.Metadata.LifecycleStateBeforeTrash is null
            ? null
            : ToDatabase(task.Metadata.LifecycleStateBeforeTrash.Value));
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = task.Metadata.Version });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = task.Metadata.UpdatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = task.Metadata.UpdatedBy });
        AddNullableTimestamp(command, task.Metadata.ArchivedAtUtc);
        AddNullableTimestamp(command, task.Metadata.DeletedAtUtc);
        AddNullableGuid(command, task.Metadata.DeletedBy);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedVersion });
        AddTaskParameters(command, task, includeIdentity: false);
    }

    private static void AddNullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        });

    private static void AddNullableTimestamp(NpgsqlCommand command, DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void AddNullableGuid(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static EntityLifecycleState ParseLifecycle(string value) => value switch
    {
        "active" => EntityLifecycleState.Active,
        "archived" => EntityLifecycleState.Archived,
        "trashed" => EntityLifecycleState.Trashed,
        _ => throw new InvalidOperationException($"Unknown stored lifecycle state '{value}'."),
    };

    private static TaskWorkStatus ParseWorkStatus(string value) => value switch
    {
        "new" => TaskWorkStatus.New,
        "in_progress" => TaskWorkStatus.InProgress,
        "review" => TaskWorkStatus.Review,
        "completed" => TaskWorkStatus.Completed,
        "cancelled" => TaskWorkStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown stored task status '{value}'."),
    };

    private static TaskPriority ParsePriority(string value) => value switch
    {
        "low" => TaskPriority.Low,
        "normal" => TaskPriority.Normal,
        "high" => TaskPriority.High,
        "critical" => TaskPriority.Critical,
        _ => throw new InvalidOperationException($"Unknown stored task priority '{value}'."),
    };

    private static string ToDatabase(EntityLifecycleState value) => value switch
    {
        EntityLifecycleState.Active => "active",
        EntityLifecycleState.Archived => "archived",
        EntityLifecycleState.Trashed => "trashed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(TaskWorkStatus value) => value switch
    {
        TaskWorkStatus.New => "new",
        TaskWorkStatus.InProgress => "in_progress",
        TaskWorkStatus.Review => "review",
        TaskWorkStatus.Completed => "completed",
        TaskWorkStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(TaskPriority value) => value switch
    {
        TaskPriority.Low => "low",
        TaskPriority.Normal => "normal",
        TaskPriority.High => "high",
        TaskPriority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
