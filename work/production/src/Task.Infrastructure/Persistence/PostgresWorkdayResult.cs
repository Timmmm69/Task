using Npgsql;
using Task.Application;

namespace Task.Infrastructure.Persistence;

public sealed partial class PostgresTaskReadStore : IWorkdayReadStore
{
    public async System.Threading.Tasks.Task<WorkdayResult?> ReadDayAsync(Guid organization, Guid actor,
        TaskTodayWindow day, DateTimeOffset cutoff, CancellationToken token)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        // Current ACLs are checked before reading any historical revision. Only totals leave this query.
        await using var command = new NpgsqlCommand("""
            WITH tasks AS (
                SELECT o.id, o.created_by, o.updated_at, t.card_content, t.start_at_utc,
                       t.status, t.completed_at, old.body, old.body IS NOT NULL AS historical
                FROM core.objects o JOIN work.tasks t ON t.organization_id=o.organization_id AND t.id=o.id
                LEFT JOIN LATERAL (
                    SELECT r.response_body AS body FROM iam.idempotency_records r
                    WHERE o.updated_at > $3 AND r.organization_id=$1 AND r.resource_id=o.id
                      AND r.state='completed' AND r.response_body ? 'status' AND r.response_body ? 'version'
                      AND (r.response_body->>'updatedAt')::timestamptz <= $3
                      AND NOT EXISTS (
                          SELECT 1 FROM governance.domain_events e
                          WHERE e.organization_id=$1 AND e.aggregate_id=o.id AND e.occurred_at <= $3
                            AND e.aggregate_version > (r.response_body->>'version')::bigint
                            AND e.event_type IN ('TaskUpdated','TaskStatusChanged'))
                    ORDER BY (r.response_body->>'version')::bigint DESC LIMIT 1
                ) old ON true
                WHERE o.organization_id=$1 AND o.created_at <= $3 AND o.lifecycle_state <> 'trashed'
                  AND (o.archived_at IS NULL OR o.archived_at > $3)
                  AND work.task_visible(o.organization_id,o.id,$2)
            ), facts AS (
                SELECT created_by, updated_at <= $3 OR historical AS known,
                    CASE WHEN historical THEN body ELSE card_content END AS card,
                    CASE WHEN historical THEN (body->>'startAtUtc')::timestamptz ELSE start_at_utc END AS starts,
                    CASE WHEN historical THEN body->>'status' ELSE status END AS state,
                    CASE WHEN historical THEN (body->>'completedAt')::timestamptz ELSE completed_at END AS finished
                FROM tasks
            ), planned AS (
                SELECT known, state, finished, (created_by=$2 OR card->'assigneeIds' ? $2::text OR card->'watcherIds' ? $2::text)
                    AND state <> 'cancelled' AND
                    (CASE WHEN starts IS NULL THEN card->>'scheduledDate'=$4 ELSE
                        starts < $6 AND (starts >= $5 OR
                            starts + make_interval(mins => (card->>'plannedDurationMinutes')::int) > $5) END) AS is_planned
                FROM facts
            )
            SELECT count(*) FILTER (WHERE NOT known OR (is_planned AND state='completed' AND finished IS NULL)),
                   count(*) FILTER (WHERE is_planned),
                   count(*) FILTER (WHERE is_planned AND state='completed' AND finished <= $3)
            FROM planned
            """, connection);
        command.Parameters.AddWithValue(organization);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(cutoff.ToUniversalTime());
        command.Parameters.AddWithValue(day.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(day.FromUtc);
        command.Parameters.AddWithValue(day.ToUtc);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return reader.GetInt64(0) == 0 ? new(checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(1))) : null;
    }
}
