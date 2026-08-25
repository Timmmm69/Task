using Npgsql;
using NpgsqlTypes;
using Task.Application;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

/// <summary>
/// Direct PostgreSQL projection for the read-only active task list and detail.
/// Every query independently applies organization, object type, and lifecycle
/// predicates; cursor contents can never broaden those predicates.
/// </summary>
public sealed class PostgresTaskReadStore : ITaskReadStore
{
    public const int PageSize = 50;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresTaskReadStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<TaskReadProjection?> GetByIdAsync(
        Guid organizationId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(taskId, nameof(taskId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                o.id,
                o.organization_id,
                o.version,
                o.created_at,
                o.updated_at,
                t.title,
                o.created_by,
                t.status,
                t.priority,
                t.start_at_utc,
                t.deadline_at
            FROM core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            WHERE o.organization_id = $1
              AND o.id = $2
              AND o.object_type = 'task'
              AND o.lifecycle_state = 'active';
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = taskId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProjection(reader) : null;
    }

    public async global::System.Threading.Tasks.Task<TaskReadPage> GetPageAsync(
        TaskReadPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdentifier(request.OrganizationId, nameof(request.OrganizationId));
        EnsureIdentifier(request.UserAccountId, nameof(request.UserAccountId));
        if (request.AuthorizationScopeVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.AuthorizationScopeVersion),
                "Authorization scope version must be positive.");
        }

        var continuation = request.Cursor is null
            ? null
            : TaskReadCursorCodec.Parse(
                request.Cursor,
                request.OrganizationId,
                request.UserAccountId,
                request.AuthorizationScopeVersion);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH snapshot AS MATERIALIZED (
                SELECT COALESCE($2::timestamptz, statement_timestamp()) AS boundary
            )
            SELECT
                o.id,
                o.organization_id,
                o.version,
                o.created_at,
                o.updated_at,
                t.title,
                o.created_by,
                t.status,
                t.priority,
                t.start_at_utc,
                t.deadline_at,
                snapshot.boundary
            FROM snapshot
            CROSS JOIN core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            WHERE o.organization_id = $1
              AND o.object_type = 'task'
              AND o.lifecycle_state = 'active'
              AND o.updated_at <= snapshot.boundary
              AND ($3::timestamptz IS NULL
                   OR o.updated_at < $3
                   OR (o.updated_at = $3 AND o.id < $4::uuid))
            ORDER BY o.updated_at DESC, o.id DESC
            LIMIT $5;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = request.OrganizationId });
        AddNullableTimestamp(command, continuation?.SnapshotBoundaryUtc);
        AddNullableTimestamp(command, continuation?.LastUpdatedAtUtc);
        AddNullableGuid(command, continuation?.LastId);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = PageSize + 1 });

        var items = new List<TaskReadProjection>(PageSize + 1);
        DateTimeOffset? snapshotBoundaryUtc = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadProjection(reader));
            snapshotBoundaryUtc ??= ReadUtcTimestamp(reader, 11);
        }

        string? nextCursor = null;
        if (items.Count == PageSize + 1)
        {
            items.RemoveAt(PageSize);
            var last = items[^1];
            nextCursor = TaskReadCursorCodec.Create(
                request.OrganizationId,
                request.UserAccountId,
                request.AuthorizationScopeVersion,
                snapshotBoundaryUtc!.Value,
                last.UpdatedAtUtc,
                last.Id);
        }

        return new TaskReadPage(items, nextCursor);
    }

    private static TaskReadProjection ReadProjection(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetInt64(2),
        ReadUtcTimestamp(reader, 3),
        ReadUtcTimestamp(reader, 4),
        reader.GetString(5),
        reader.GetGuid(6),
        ParseWorkStatus(reader.GetString(7)),
        ParsePriority(reader.GetString(8)),
        ReadNullableUtcTimestamp(reader, 9),
        ReadNullableUtcTimestamp(reader, 10));

    private static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime();

    private static DateTimeOffset? ReadNullableUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtcTimestamp(reader, ordinal);

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

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
