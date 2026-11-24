using Npgsql;
using NpgsqlTypes;
using System.Globalization;
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
    public const int TodayPageSize = 200;

    private const string TodayPredicate = """
        AND t.status NOT IN ('completed','cancelled')
        AND (
            o.created_by=$6 OR t.card_content->'assigneeIds' ? $6::text OR t.card_content->'watcherIds' ? $6::text
            OR (t.start_at_utc IS NULL AND t.card_content->>'scheduledDate'=$9)
            OR (t.start_at_utc < $8 AND (
                t.start_at_utc >= $7
                OR CASE WHEN t.card_content->>'plannedDurationMinutes' ~ '^[0-9]{1,5}$'
                        THEN t.start_at_utc + make_interval(mins => (t.card_content->>'plannedDurationMinutes')::int) > $7
                        ELSE false END
            ))
        )
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresTaskReadStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public global::System.Threading.Tasks.Task<TaskReadProjection?> GetByIdAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken = default) =>
        ReadByIdAsync(organizationId, taskId, null, cancellationToken);

    public global::System.Threading.Tasks.Task<TaskReadProjection?> GetVisibleByIdAsync(Guid organizationId, Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default) =>
        ReadByIdAsync(organizationId, taskId, actorUserId, cancellationToken);

    private async global::System.Threading.Tasks.Task<TaskReadProjection?> ReadByIdAsync(
        Guid organizationId,
        Guid taskId, Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(taskId, nameof(taskId));
        if (actorUserId is { } actor) PostgresAuthorizationAudit.AdministrativeRead(_dataSource, organizationId, actor, "task.read");

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
                t.deadline_at, t.card_content || jsonb_build_object('deadlineReminderOffsetMinutes',
                    (SELECT p.offset_minutes FROM calendar.task_deadline_preferences p WHERE p.organization_id=o.organization_id AND p.task_id=o.id AND p.recipient_user_id=$3), 'deadlineReminderTime',
                    (SELECT p.date_only_time::text FROM calendar.task_deadline_preferences p WHERE p.organization_id=o.organization_id AND p.task_id=o.id AND p.recipient_user_id=$3)), t.completed_at,
                (SELECT series_id FROM calendar.recurrence_occurrences ro WHERE ro.organization_id=o.organization_id AND ro.task_id=o.id LIMIT 1)
            FROM core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            WHERE o.organization_id = $1
              AND o.id = $2
              AND o.object_type = 'task'
              AND o.lifecycle_state = 'active' AND ($3::uuid IS NULL OR work.task_visible(o.organization_id,t.id,$3));
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = taskId });
        AddNullableGuid(command, actorUserId);

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

        if (request.Today is { IsValid: false }) throw new ArgumentException("Invalid Today window.", nameof(request));
        var today = request.Today;
        var pageSize = today is null ? PageSize : TodayPageSize;
        var includeLinkedFolders = today is null && request.IncludeLinkedFolders;
        const string linkedFolders = """
            (SELECT COALESCE(jsonb_agg(jsonb_build_object('Id', p.id, 'Name', p.name) ORDER BY p.name,p.id), '[]'::jsonb)
             FROM core.object_links l
             JOIN files.catalog_items p ON p.organization_id=l.organization_id AND p.id=l.target_object_id
             JOIN core.objects target ON target.organization_id=p.organization_id AND target.id=p.id AND target.lifecycle_state='active'
             WHERE l.organization_id=o.organization_id AND l.source_object_id=o.id AND l.link_type='task_file'
               AND p.item_type='folder_reference' AND iam.object_allowed(o.organization_id,p.id,$6,'filecatalog.read',$7))
            """;
        PostgresAuthorizationAudit.AdministrativeRead(_dataSource, request.OrganizationId, request.UserAccountId,
            today is null ? "task.list" : "task.today");
        var continuation = request.Cursor is null
            ? null
            : TaskReadCursorCodec.Parse(
                request.Cursor,
                request.OrganizationId,
                request.UserAccountId,
                request.AuthorizationScopeVersion,
                today);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $$"""
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
                t.deadline_at, t.card_content || jsonb_build_object('deadlineReminderOffsetMinutes',
                    (SELECT p.offset_minutes FROM calendar.task_deadline_preferences p WHERE p.organization_id=o.organization_id AND p.task_id=o.id AND p.recipient_user_id=$6), 'deadlineReminderTime',
                    (SELECT p.date_only_time::text FROM calendar.task_deadline_preferences p WHERE p.organization_id=o.organization_id AND p.task_id=o.id AND p.recipient_user_id=$6)), t.completed_at,
                (SELECT series_id FROM calendar.recurrence_occurrences ro WHERE ro.organization_id=o.organization_id AND ro.task_id=o.id LIMIT 1),
                snapshot.boundary{{(includeLinkedFolders ? ", " + linkedFolders : "")}}
            FROM snapshot
            CROSS JOIN core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            WHERE o.organization_id = $1
              AND o.object_type = 'task'
              AND o.lifecycle_state = 'active' AND work.task_visible(o.organization_id,t.id,$6)
              AND o.updated_at <= snapshot.boundary
              AND ($3::timestamptz IS NULL
                   OR o.updated_at < $3
                   OR (o.updated_at = $3 AND o.id < $4::uuid))
              {{(today is null ? "" : TodayPredicate)}}
            ORDER BY o.updated_at DESC, o.id DESC
            LIMIT $5;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = request.OrganizationId });
        AddNullableTimestamp(command, continuation?.SnapshotBoundaryUtc);
        AddNullableTimestamp(command, continuation?.LastUpdatedAtUtc);
        AddNullableGuid(command, continuation?.LastId);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pageSize + 1 });

        var items = new List<TaskReadProjection>(pageSize + 1);
        DateTimeOffset? snapshotBoundaryUtc = null;
        command.Parameters.AddWithValue(request.UserAccountId);
        if (today is not null)
        {
            command.Parameters.AddWithValue(today.FromUtc);
            command.Parameters.AddWithValue(today.ToUtc);
            command.Parameters.AddWithValue(today.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (includeLinkedFolders) command.Parameters.AddWithValue(request.CanManageOrganization);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var task = ReadProjection(reader);
            if (includeLinkedFolders)
                task = task with { LinkedFolders = System.Text.Json.JsonSerializer.Deserialize<TaskFolderReference[]>(reader.GetString(15)) };
            items.Add(task);
            snapshotBoundaryUtc ??= ReadUtcTimestamp(reader, 14);
        }

        string? nextCursor = null;
        if (items.Count == pageSize + 1)
        {
            items.RemoveAt(pageSize);
            var last = items[^1];
            nextCursor = TaskReadCursorCodec.Create(
                request.OrganizationId,
                request.UserAccountId,
                request.AuthorizationScopeVersion,
                snapshotBoundaryUtc!.Value,
                last.UpdatedAtUtc,
                last.Id,
                today);
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
        ReadNullableUtcTimestamp(reader, 10), ReadCard(reader.GetString(11)),
        ReadNullableUtcTimestamp(reader, 12), reader.IsDBNull(13) ? null : reader.GetGuid(13));

    private static TaskCardContent ReadCard(string json) => TaskCardContent.FromJson(json) with
    { DeadlineReminderTime = System.Text.Json.Nodes.JsonNode.Parse(json)?["deadlineReminderTime"]?.GetValue<string>() };

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
