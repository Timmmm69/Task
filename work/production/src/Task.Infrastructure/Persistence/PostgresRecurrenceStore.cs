using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application.Calendar;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

public sealed class PostgresRecurrenceStore(NpgsqlDataSource dataSource) : IRecurrenceStore
{
    private const string Columns = "id, organization_id, version, created_at, updated_at, created_by, definition";
    public IReadOnlyList<RecurrenceRecord> ListDue(DateOnly throughDate, int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        using var command = dataSource.CreateCommand($"SELECT {Columns} FROM calendar.recurrence_series WHERE definition->>'status'='active' AND (definition->>'nextGenerationDate')::date <= $1 AND ((definition->>'untilDate') IS NULL OR (definition->>'untilDate')::date >= (definition->>'nextGenerationDate')::date) AND ((definition->>'maxOccurrences') IS NULL OR (SELECT count(*) FROM calendar.recurrence_occurrences r WHERE r.organization_id=recurrence_series.organization_id AND r.series_id=recurrence_series.id) < (definition->>'maxOccurrences')::int) ORDER BY (definition->>'nextGenerationDate')::date,id LIMIT $2");
        command.Parameters.AddWithValue(throughDate); command.Parameters.AddWithValue(limit);
        using var reader = command.ExecuteReader(); var result = new List<RecurrenceRecord>();
        while (reader.Read()) result.Add(Read(reader)); return result;
    }
    public IReadOnlyList<RecurrenceRecord> List(Guid organizationId)
    {
        using var command = dataSource.CreateCommand($"SELECT {Columns} FROM calendar.recurrence_series WHERE organization_id = $1 ORDER BY id LIMIT 501");
        command.Parameters.AddWithValue(organizationId);
        using var reader = command.ExecuteReader();
        var result = new List<RecurrenceRecord>();
        while (reader.Read()) result.Add(Read(reader));
        if (result.Count > 500) throw new RecurrenceRequestException(422, "RESULT_LIMIT_EXCEEDED", "The series list exceeds 500 entries.");
        return result;
    }
    public RecurrenceRecord? Get(Guid organizationId, Guid id)
    {
        using var connection = dataSource.OpenConnection();
        return Get(connection, null, organizationId, id, false);
    }
    public IReadOnlyList<RecurrenceOccurrenceDetails> GetOccurrences(Guid organizationId, Guid id)
    {
        using var command = dataSource.CreateCommand("SELECT r.local_date,r.task_id,o.version,t.title,t.status,r.skipped,r.template FROM calendar.recurrence_occurrences r JOIN core.objects o ON o.organization_id=r.organization_id AND o.id=r.task_id JOIN work.tasks t ON t.organization_id=r.organization_id AND t.id=r.task_id WHERE r.organization_id=$1 AND r.series_id=$2 ORDER BY r.local_date DESC LIMIT 500");
        command.Parameters.AddWithValue(organizationId); command.Parameters.AddWithValue(id);
        using var reader = command.ExecuteReader(); var result = new List<RecurrenceOccurrenceDetails>();
        while (reader.Read()) result.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<RecurrenceTemplateData>(reader.GetString(6), RecurrenceService.JsonOptions)));
        return result;
    }
    private static RecurrenceRecord? Get(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid org, Guid id, bool locked)
    {
        using var command = new NpgsqlCommand($"SELECT {Columns} FROM calendar.recurrence_series WHERE organization_id = $1 AND id = $2" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id);
        using var reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    }
    private static RecurrenceRecord Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetInt64(2),
        r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4), r.GetGuid(5),
        JsonSerializer.Deserialize<RecurrenceDefinition>(r.GetString(6), RecurrenceService.JsonOptions)!);

    public RecurrenceReply Execute(Guid org, Guid actor, Guid id, string operation, string key, string hash,
        Func<RecurrenceRecord?, IRecurrenceTransaction, RecurrenceReply> action)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var resource = operation == "create" ? Guid.Empty : id;
        // Same-key requests serialize before replay, different keys serialize on the
        // series row before checking its version. Failures roll back every side effect.
        using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        {
            command.Parameters.AddWithValue($"recurrence:{org}:{actor}:{resource}:{operation}:{key}"); command.ExecuteNonQuery();
        }
        using (var command = new NpgsqlCommand("SELECT request_hash, status, version, response FROM calendar.recurrence_commands WHERE organization_id=$1 AND actor_id=$2 AND resource_id=$3 AND operation=$4 AND idempotency_key=$5", connection, transaction))
        {
            AddScope(command, org, actor, resource, operation, key);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                if (reader.GetString(0) != hash) throw new RecurrenceRequestException(409, "IDEMPOTENCY_KEY_REUSED", "The idempotency key belongs to a different request.");
                return new(reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3));
            }
        }
        var current = Get(connection, transaction, org, id, true);
        var unit = new UnitOfWork(connection, transaction, org, id);
        var reply = action(current, unit);
        using (var command = new NpgsqlCommand("INSERT INTO calendar.recurrence_commands(organization_id,actor_id,resource_id,operation,idempotency_key,request_hash,status,version,response) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)", connection, transaction))
        {
            AddScope(command, org, actor, resource, operation, key);
            command.Parameters.AddWithValue(hash); command.Parameters.AddWithValue(reply.Status); command.Parameters.AddWithValue(reply.Version);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, reply.Json); command.ExecuteNonQuery();
        }
        if (unit.Changed) AppendEffects(connection, transaction, org, actor, id, reply.Version, operation, key);
        transaction.Commit();
        return reply;
    }
    private static void AddScope(NpgsqlCommand command, Guid org, Guid actor, Guid resource, string operation, string key)
    { command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(resource); command.Parameters.AddWithValue(operation); command.Parameters.AddWithValue(key); }

    private static void AppendEffects(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid org, Guid actor, Guid id, long version, string operation, string key)
    {
        var eventId = Guid.NewGuid(); var correlation = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { objectId = id, version, operation }, RecurrenceService.JsonOptions);
        using (var command = new NpgsqlCommand("INSERT INTO governance.audit_entries(id,organization_id,actor_user_id,action_code,object_id,object_type,outcome,correlation_id,request_id,metadata) VALUES($1,$2,$3,$4,$5,'recurrence_series','success',$6,$6,$7)", connection, transaction))
        {
            command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(actor);
            command.Parameters.AddWithValue("recurrence." + operation); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(correlation);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload); command.ExecuteNonQuery();
        }
        using (var command = new NpgsqlCommand("INSERT INTO governance.domain_events(id,organization_id,aggregate_id,aggregate_type,aggregate_version,event_type,actor_user_id,correlation_id,operation_id,idempotency_key,payload) VALUES($1,$2,$3,'recurrence_series',$4,'RecurrenceSeriesChanged',$5,$6,$7,$8,$9)", connection, transaction))
        {
            command.Parameters.AddWithValue(eventId); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(version); command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(correlation);
            command.Parameters.AddWithValue($"recurrence.{operation}.{id:N}"); command.Parameters.AddWithValue(key);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload); command.ExecuteNonQuery();
        }
        using (var command = new NpgsqlCommand("INSERT INTO governance.outbox_messages(id,organization_id,domain_event_id,destination,message_type,payload) VALUES($1,$2,$3,'sync','RecurrenceSeriesChanged',$4)", connection, transaction))
        {
            command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(eventId);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload); command.ExecuteNonQuery();
        }
    }

    private sealed class UnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid org, Guid id) : IRecurrenceTransaction
    {
        public bool Changed { get; private set; }
        public IReadOnlyList<RecurrenceOccurrenceRecord> Occurrences
        {
            get
            {
                using var command = new NpgsqlCommand("SELECT local_date,task_id,skipped,generated_task_version,template,is_exception FROM calendar.recurrence_occurrences WHERE organization_id=$1 AND series_id=$2 ORDER BY local_date", connection, transaction);
                command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id);
                using var reader = command.ExecuteReader(); var result = new List<RecurrenceOccurrenceRecord>();
                while (reader.Read()) result.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetGuid(1), reader.GetBoolean(2), reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<RecurrenceTemplateData>(reader.GetString(4), RecurrenceService.JsonOptions), reader.GetBoolean(5)));
                return result;
            }
        }
        public TaskAggregate? GetTask(Guid taskId)
        {
            using var command = new NpgsqlCommand("SELECT id FROM core.objects WHERE organization_id=$1 AND id=$2 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(taskId); command.ExecuteScalar();
            return PostgresTaskAggregateStore.Get(connection, transaction, taskId, org);
        }
        public void SaveTask(TaskAggregate task, int? expectedVersion)
        {
            if (task.Metadata.OrganizationId != org) throw new InvalidOperationException("Tenant mismatch.");
            if (expectedVersion.HasValue)
            {
                if (task.Metadata.Version == expectedVersion.Value) return;
                PostgresTaskAggregateStore.Save(connection, transaction, task, expectedVersion.Value);
            }
            else PostgresTaskAggregateStore.Add(connection, transaction, task);
            Changed = true;
        }
        public void SaveOccurrence(RecurrenceOccurrenceRecord occurrence)
        {
            using var command = new NpgsqlCommand("INSERT INTO calendar.recurrence_occurrences(organization_id,series_id,local_date,task_id,skipped,generated_task_version,template,is_exception) VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT(organization_id,series_id,local_date) DO UPDATE SET skipped=EXCLUDED.skipped,generated_task_version=EXCLUDED.generated_task_version,template=EXCLUDED.template,is_exception=EXCLUDED.is_exception", connection, transaction);
            command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(occurrence.LocalDate);
            command.Parameters.AddWithValue(occurrence.TaskId); command.Parameters.AddWithValue(occurrence.Skipped);
            command.Parameters.AddWithValue(occurrence.GeneratedTaskVersion);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, (object?)(occurrence.Template is null ? null : JsonSerializer.Serialize(occurrence.Template, RecurrenceService.JsonOptions)) ?? DBNull.Value);
            command.Parameters.AddWithValue(occurrence.IsException); command.ExecuteNonQuery(); Changed = true;
        }
        public void SaveSeries(RecurrenceRecord series)
        {
            if (series.OrganizationId != org || series.Id != id) throw new InvalidOperationException("Tenant mismatch.");
            ValidateReferences(series.Definition.Template);
            using var command = new NpgsqlCommand("INSERT INTO calendar.recurrence_series(id,organization_id,version,created_at,updated_at,created_by,definition) VALUES($1,$2,$3,$4,$5,$6,$7) ON CONFLICT(id) DO UPDATE SET version=EXCLUDED.version,updated_at=EXCLUDED.updated_at,definition=EXCLUDED.definition WHERE calendar.recurrence_series.organization_id=EXCLUDED.organization_id", connection, transaction);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(series.Version);
            command.Parameters.AddWithValue(series.CreatedAt); command.Parameters.AddWithValue(series.UpdatedAt); command.Parameters.AddWithValue(series.CreatedBy);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(series.Definition, RecurrenceService.JsonOptions));
            command.ExecuteNonQuery(); Changed = true;
        }
        private void ValidateReferences(RecurrenceTemplateData template)
        {
            var users = template.AssigneeIds.Concat(template.WatcherIds).Append(template.AuthorUserId)
                .Concat(template.RequesterUserId.HasValue ? [template.RequesterUserId.Value] : []).Distinct().ToArray();
            using (var command = new NpgsqlCommand("SELECT count(*) FROM iam.user_accounts WHERE organization_id=$1 AND id=ANY($2)", connection, transaction))
            {
                command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(users);
                if ((long)command.ExecuteScalar()! != users.Length) throw new RecurrenceRequestException(422, "VALIDATION_FAILED", "A template user is absent or not visible.");
            }
            foreach (var reference in new[] { (template.ProjectId, "project"), (template.PrimaryCounterpartyObjectId, "contact") })
            {
                if (!reference.Item1.HasValue) continue;
                using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM core.objects WHERE organization_id=$1 AND id=$2 AND lifecycle_state='active' AND (object_type=$3 OR ($3='contact' AND object_type='company')))", connection, transaction);
                command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(reference.Item1.Value); command.Parameters.AddWithValue(reference.Item2);
                if (!(bool)command.ExecuteScalar()!) throw new RecurrenceRequestException(422, "VALIDATION_FAILED", "A template reference is absent or not visible.");
            }
        }
    }
}
