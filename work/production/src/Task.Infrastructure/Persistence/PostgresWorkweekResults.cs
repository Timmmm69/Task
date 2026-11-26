using Npgsql;
using Task.Application;

namespace Task.Infrastructure.Persistence;

public sealed partial class PostgresTaskReadStore : IWorkweekReadStore
{
    public async System.Threading.Tasks.Task<WorkweekResults> ReadWeekAsync(Guid organization, Guid actor,
        DateTimeOffset from, DateTimeOffset through, CancellationToken token)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("""
            WITH completions AS (
                SELECT aggregate_id AS task_id, occurred_at AS finished
                FROM governance.domain_events
                WHERE organization_id=$1 AND actor_user_id=$2 AND event_type='TaskStatusChanged'
                  AND payload->>'targetStatus'='completed' AND occurred_at BETWEEN $3 AND $4
                UNION ALL
                SELECT id, completed_at FROM work.tasks WHERE organization_id=$1 AND completed_by=$2
                  AND completed_at BETWEEN $3 AND $4
            ), latest AS (SELECT task_id, max(finished) AS finished FROM completions GROUP BY task_id)
            SELECT t.title, latest.finished, count(*) OVER ()
            FROM latest JOIN work.tasks t ON t.id=latest.task_id AND t.organization_id=$1
            JOIN core.objects o ON o.organization_id=t.organization_id AND o.id=t.id
            WHERE o.lifecycle_state IN ('active','archived') AND work.task_visible(t.organization_id,t.id,$2)
              AND iam.object_allowed(t.organization_id,t.id,$2,'history.read',iam.permission_granted($1,$2,'organization.manage'))
            ORDER BY latest.finished DESC, t.id LIMIT 2
            """, connection);
        command.Parameters.AddWithValue(organization);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(from.ToUniversalTime());
        command.Parameters.AddWithValue(through.ToUniversalTime());
        var items = new List<WeeklyCompletion>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            total = checked((int)reader.GetInt64(2));
            items.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }
        return new(total, items);
    }
}
