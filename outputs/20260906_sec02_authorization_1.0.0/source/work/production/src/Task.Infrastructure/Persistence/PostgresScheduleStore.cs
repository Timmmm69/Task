using Npgsql;
using Task.Application.Calendar;

namespace Task.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="IScheduleStore"/>: two read-only
/// queries over <c>core.objects</c> joined with <c>calendar.events</c> and
/// <c>work.tasks</c>, filtered to the active lifecycle state and the requested
/// window. Event rows are returned first, task rows second; ordering is
/// established by <see cref="ScheduleQueryService"/>.
/// </summary>
public sealed class PostgresScheduleStore : IScheduleStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresScheduleStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public IReadOnlyList<ScheduleItemRow> QuerySchedule(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? users,
        IReadOnlyList<Guid>? projects,
        string? status, Guid? actorId = null)
    {
        var rows = new List<ScheduleItemRow>();
        if (actorId is { } actor) PostgresAuthorizationAudit.AdministrativeRead(_dataSource, organizationId, actor, "calendar.schedule");
        ReadEvents(rows, organizationId, fromUtc, toUtc, users, projects, status, actorId);
        ReadTasks(rows, organizationId, fromUtc, toUtc, status, actorId);
        return rows;
    }

    private void ReadEvents(
        List<ScheduleItemRow> rows,
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<Guid>? users,
        IReadOnlyList<Guid>? projects,
        string? status, Guid? actorId = null)
    {
        var sql =
            """
            SELECT o.id, e.project_id, e.title, e.event_date, e.is_all_day,
                   e.start_at_utc, e.end_at_utc, e.time_zone_id, e.status
            FROM core.objects AS o
            INNER JOIN calendar.events AS e
                ON e.organization_id = o.organization_id AND e.id = o.id
            WHERE o.organization_id = $1 AND o.object_type = 'calendar_event' AND o.lifecycle_state = 'active'
              AND (
                  (e.is_all_day = false AND e.start_at_utc < $2 AND e.end_at_utc > $3)
                  OR (e.is_all_day = true AND e.event_date >= $4 AND e.event_date <= $5)
              )
            """;

        var parameterIndex = 6;
        if (users is not null && users.Count > 0)
        {
            sql += $" AND e.id IN (SELECT event_id FROM calendar.event_user_attendees WHERE organization_id = $1 AND user_account_id = ANY(${parameterIndex}))";
            parameterIndex++;
        }

        if (projects is not null && projects.Count > 0)
        {
            sql += $" AND e.project_id = ANY(${parameterIndex})";
            parameterIndex++;
        }

        if (!string.IsNullOrEmpty(status))
        {
            sql += $" AND e.status = ${parameterIndex}"; parameterIndex++;
        }

        if (actorId is not null) sql += $" AND iam.object_allowed($1,o.id,${parameterIndex},'calendar.read')";
        using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = toUtc });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = fromUtc });
        command.Parameters.Add(new NpgsqlParameter<DateOnly>
        {
            TypedValue = DateOnly.FromDateTime(fromUtc.Date.AddDays(-1)),
        });
        command.Parameters.Add(new NpgsqlParameter<DateOnly>
        {
            TypedValue = DateOnly.FromDateTime(toUtc.Date.AddDays(1)),
        });

        if (users is not null && users.Count > 0)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid[]> { TypedValue = users.ToArray() });
        }

        if (projects is not null && projects.Count > 0)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid[]> { TypedValue = projects.ToArray() });
        }

        if (!string.IsNullOrEmpty(status))
        {
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = status });
        }

        if (actorId is { } actor) command.Parameters.AddWithValue(actor);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ScheduleItemRow(
                reader.GetGuid(0),
                ScheduleItemType.CalendarEvent,
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetBoolean(4),
                ReadNullableTimestamp(reader, 5),
                ReadNullableTimestamp(reader, 6),
                reader.GetString(7),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(8),
                null));
        }
    }

    private void ReadTasks(
        List<ScheduleItemRow> rows,
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? status, Guid? actorId = null)
    {
        var sql =
            """
            SELECT o.id, t.title, t.status, t.priority, t.start_at_utc, t.deadline_at,
                   r.series_id, r.local_date, r.template->>'description',
                   (r.template->>'projectId')::uuid, s.definition->>'timeZone',
                   (s.definition->>'localStartTime' IS NULL AND r.generated_task_version=o.version AND r.is_exception=false)
            FROM core.objects AS o
            INNER JOIN work.tasks AS t
                ON t.organization_id = o.organization_id AND t.id = o.id
            LEFT JOIN calendar.recurrence_occurrences r ON r.organization_id=o.organization_id AND r.task_id=o.id
            LEFT JOIN calendar.recurrence_series s ON s.organization_id=r.organization_id AND s.id=r.series_id
            WHERE o.organization_id = $1 AND o.object_type = 'task' AND o.lifecycle_state = 'active'
              AND (
                  (t.start_at_utc IS NOT NULL AND t.deadline_at IS NOT NULL AND
                      t.start_at_utc < $2 AND t.deadline_at > $3)
                  OR (t.start_at_utc IS NOT NULL AND t.deadline_at IS NULL AND
                      t.start_at_utc >= $3 AND t.start_at_utc < $2)
                  OR (t.start_at_utc IS NULL AND t.deadline_at IS NOT NULL AND
                      t.deadline_at >= $3 AND t.deadline_at < $2)
              )
            """;

        if (!string.IsNullOrEmpty(status))
        {
            sql += " AND t.status = $4";
        }

        if (actorId is not null) sql += $" AND work.task_visible($1,o.id,${(string.IsNullOrEmpty(status) ? 4 : 5)})";
        using var command = _dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = toUtc });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = fromUtc });

        if (!string.IsNullOrEmpty(status))
        {
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = status });
        }

        if (actorId is { } actor) command.Parameters.AddWithValue(actor);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ScheduleItemRow(
                reader.GetGuid(0),
                ScheduleItemType.Task,
                reader.GetString(1),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                !reader.IsDBNull(6) && !reader.IsDBNull(11) && reader.GetBoolean(11),
                ReadNullableTimestamp(reader, 4),
                ReadNullableTimestamp(reader, 5),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetString(2),
                ParsePriority(reader.GetString(3)),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }
    }

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static ScheduleItemPriority ParsePriority(string value) => value switch
    {
        "low" => ScheduleItemPriority.Low,
        "normal" => ScheduleItemPriority.Normal,
        "high" => ScheduleItemPriority.High,
        "critical" => ScheduleItemPriority.Critical,
        _ => throw new InvalidOperationException($"Unknown stored task priority '{value}'."),
    };
}
