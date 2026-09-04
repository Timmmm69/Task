using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

public sealed partial class PostgresDeviceRegistrationStore
{
    public async global::System.Threading.Tasks.Task UpdateMetadataAsync(
        Guid organizationId, Guid deviceId, string platform, string appVersion, string? osVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE iam.devices SET platform=$3,app_version=$4,os_version=$5,last_seen_at=clock_timestamp() WHERE organization_id=$1 AND id=$2 AND revoked_at IS NULL;",
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=deviceId});
        command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=platform});
        command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=appVersion});
        AddDeviceNullableText(command,osVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<DeviceReadProjection?> GetReadModelAsync(
        Guid organizationId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection=await _dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadDeviceAsync(connection,null,organizationId,deviceId,false,cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<DeviceReadPage> GetPageAsync(
        Guid organizationId, Guid requestingUserId, bool includeAll, string? filter, int page, Guid? cursor,
        CancellationToken cancellationToken = default)
    {
        if(page is <1 or >100000) throw new ArgumentOutOfRangeException(nameof(page));
        await using var connection=await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command=new NpgsqlCommand(
            """
            SELECT d.id,d.organization_id,d.user_account_id,o.version,o.created_at,o.updated_at,
                   COALESCE(d.display_name,'Unnamed device'),d.platform,d.app_version,d.os_version,d.last_seen_at,d.revoked_at
            FROM iam.devices d JOIN core.objects o ON o.id=d.id AND o.organization_id=d.organization_id
            WHERE d.organization_id=$1 AND ($2 OR d.user_account_id=$3)
              AND ($4::text IS NULL OR d.display_name ILIKE '%'||$4||'%' OR d.app_version ILIKE '%'||$4||'%')
              AND ($5::uuid IS NULL OR d.id>$5)
            ORDER BY d.id OFFSET $6 LIMIT 101;
            """,connection);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});
        command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=includeAll});
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=requestingUserId});
        AddDeviceNullableText(command,string.IsNullOrWhiteSpace(filter)?null:filter.Trim());
        AddDeviceNullableGuid(command,cursor);
        command.Parameters.Add(new NpgsqlParameter<int>{TypedValue=cursor is null?(page-1)*100:0});
        var items=new List<DeviceReadProjection>();
        await using(var reader=await command.ExecuteReaderAsync(cancellationToken)) while(await reader.ReadAsync(cancellationToken)) items.Add(ReadDevice(reader));
        var more=items.Count>100;if(more)items.RemoveAt(items.Count-1);
        await using var count=new NpgsqlCommand("SELECT count(*) FROM iam.devices WHERE organization_id=$1 AND ($2 OR user_account_id=$3) AND ($4::text IS NULL OR display_name ILIKE '%'||$4||'%' OR app_version ILIKE '%'||$4||'%');",connection);
        count.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});count.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=includeAll});count.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=requestingUserId});AddDeviceNullableText(count,string.IsNullOrWhiteSpace(filter)?null:filter.Trim());
        var total=Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
        return new DeviceReadPage(items,more?items[^1].Id:null,total);
    }

    public global::System.Threading.Tasks.Task<DeviceCommandResult> PatchAsync(
        IdentityCommandContext context, Guid deviceId, long expectedVersion, DevicePatchCommand patch,
        CancellationToken cancellationToken = default) => ExecuteDeviceAsync(context,deviceId,expectedVersion,"DeviceUpdated",async(connection,transaction,ct)=>
        {
            await using var command=new NpgsqlCommand(
                """
                WITH mutation_0 AS (
                    UPDATE iam.devices SET
                    display_name=CASE WHEN $3 THEN $4::text ELSE display_name END,
                    platform=CASE WHEN $5 THEN $6 ELSE platform END,
                    app_version=CASE WHEN $7 THEN $8 ELSE app_version END
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                )
                UPDATE core.objects SET version=version+1,updated_at=clock_timestamp(),updated_by=$9
                WHERE organization_id=$1 AND id=$2;
                """,connection,transaction);
            command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=deviceId});
            command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=patch.NameSpecified});AddDeviceNullableText(command,patch.DeviceName);
            command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=patch.PlatformSpecified});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=patch.Platform??"windows"});
            command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=patch.AppVersionSpecified});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=patch.AppVersion??"unknown"});
            command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});await command.ExecuteNonQueryAsync(ct);
        },cancellationToken);

    public async global::System.Threading.Tasks.Task<bool> HeartbeatAsync(
        Guid organizationId, Guid userId, Guid deviceId, string appVersion, string? osVersion,
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection=await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command=new NpgsqlCommand(
            "UPDATE iam.devices SET last_seen_at=GREATEST(last_seen_at,$4),app_version=$5,os_version=$6 WHERE organization_id=$1 AND user_account_id=$2 AND id=$3 AND revoked_at IS NULL;",connection);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=userId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=deviceId});
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>{TypedValue=observedAtUtc});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=appVersion});AddDeviceNullableText(command,osVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken)==1;
    }

    public global::System.Threading.Tasks.Task<DeviceCommandResult> RevokeAsync(
        IdentityCommandContext context, Guid deviceId, long expectedVersion, string reason,
        CancellationToken cancellationToken = default) => ExecuteDeviceAsync(context,deviceId,expectedVersion,"DeviceRevoked",async(connection,transaction,ct)=>
        {
            await using var command=new NpgsqlCommand(
                """
                WITH mutation_0 AS (
                    UPDATE iam.devices SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_1 AS (
                    UPDATE core.objects SET version=version+1,updated_at=clock_timestamp(),updated_by=$3 WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_2 AS (
                    UPDATE iam.refresh_tokens SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE session_id IN(SELECT id FROM iam.sessions WHERE organization_id=$1 AND device_id=$2) RETURNING 1
                )
                UPDATE iam.sessions SET revoked_at=COALESCE(revoked_at,clock_timestamp()),revoke_reason=COALESCE(revoke_reason,'device-revoked') WHERE organization_id=$1 AND device_id=$2;
                """,connection,transaction);
            command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=deviceId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});await command.ExecuteNonQueryAsync(ct);
        },cancellationToken,reason);

    private async global::System.Threading.Tasks.Task<DeviceCommandResult> ExecuteDeviceAsync(
        IdentityCommandContext context,Guid deviceId,long expectedVersion,string eventType,
        Func<NpgsqlConnection,NpgsqlTransaction,CancellationToken,global::System.Threading.Tasks.Task> mutation,CancellationToken cancellationToken,string? reason=null)
    {
        await using var connection=await _dataSource.OpenConnectionAsync(cancellationToken);await using var transaction=await connection.BeginTransactionAsync(cancellationToken);var owner=Guid.NewGuid();
        try
        {
            var acquire=await AcquireDeviceAsync(connection,transaction,context,owner,cancellationToken);
            if(acquire.Disposition=="replay"){var replay=acquire.Body is null?null:JsonSerializer.Deserialize<DeviceReadProjection>(acquire.Body);if(replay is null || (replay.UserId!=context.ActorUserId && !context.CanManageAllDevices)){await transaction.RollbackAsync(CancellationToken.None);return new(IdentityCommandDisposition.NotFound);}await transaction.CommitAsync(cancellationToken);return new(IdentityCommandDisposition.Replayed,replay);}
            if(acquire.Disposition=="in_progress"){await transaction.CommitAsync(cancellationToken);return new(IdentityCommandDisposition.RequestInProgress,RetryAfterSeconds:acquire.RetryAfter);}
            var current=await ReadDeviceAsync(connection,transaction,context.OrganizationId,deviceId,true,cancellationToken);
            if(current is null){await transaction.RollbackAsync(CancellationToken.None);return new(IdentityCommandDisposition.NotFound);}
            if (current.UserId != context.ActorUserId && !context.CanManageAllDevices)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(IdentityCommandDisposition.NotFound);
            }
            if(current.Version!=expectedVersion){await transaction.RollbackAsync(CancellationToken.None);return new(IdentityCommandDisposition.VersionConflict);}
            await mutation(connection,transaction,cancellationToken);
            var updated=(await ReadDeviceAsync(connection,transaction,context.OrganizationId,deviceId,false,cancellationToken))!;
            await AppendDeviceEvidenceAsync(connection,transaction,context,acquire.RecordId,updated,eventType,reason,cancellationToken);
            await CompleteDeviceAsync(connection,transaction,context,acquire.RecordId,owner,updated,cancellationToken);
            await transaction.CommitAsync(cancellationToken);return new(IdentityCommandDisposition.Executed,updated);
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.CheckViolation&&ex.MessageText=="IDEMPOTENCY_KEY_REUSED"){await transaction.RollbackAsync(CancellationToken.None);return new(IdentityCommandDisposition.IdempotencyKeyReused);}
    }

    private static async global::System.Threading.Tasks.Task<DeviceAcquire> AcquireDeviceAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,IdentityCommandContext context,Guid owner,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("SELECT disposition,stored_record_id,stored_response_body::text,retry_after_seconds FROM iam.acquire_idempotency_record($1,$2,$3,$4,$5,$6,$7,interval '2 minutes',interval '7 days');",connection,transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=Guid.NewGuid()});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=context.OperationId});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=context.IdempotencyKey});command.Parameters.Add(new NpgsqlParameter<byte[]>{NpgsqlDbType=NpgsqlDbType.Bytea,TypedValue=context.RequestHash});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=owner});
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);return new(reader.GetString(0),reader.GetGuid(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetInt32(3));
    }

    private static async global::System.Threading.Tasks.Task CompleteDeviceAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,IdentityCommandContext context,Guid recordId,Guid owner,DeviceReadProjection device,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("SELECT iam.complete_idempotency_record($1,$2,$3,$4,$5,200,$6::jsonb,$7::jsonb,$8);",connection,transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=recordId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=owner});command.Parameters.Add(new NpgsqlParameter<byte[]>{NpgsqlDbType=NpgsqlDbType.Bytea,TypedValue=context.RequestHash});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=JsonSerializer.Serialize(new Dictionary<string,string>{{"ETag",$"\"v{device.Version}\""}})});command.Parameters.Add(new NpgsqlParameter<string>{TypedValue=JsonSerializer.Serialize(device)});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=device.Id});await command.ExecuteNonQueryAsync(ct);
    }

    private static async global::System.Threading.Tasks.Task AppendDeviceEvidenceAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,IdentityCommandContext context,Guid requestId,DeviceReadProjection device,string eventType,string? reason,CancellationToken ct)
    {
        await using var audit=new NpgsqlCommand("INSERT INTO governance.audit_entries(id,organization_id,actor_user_id,actor_session_id,action_code,object_id,object_type,outcome,correlation_id,request_id,metadata,new_state,redaction_level) VALUES($1,$2,$3,$4,$5,$6,'device','success',$7,$8,$9::jsonb,'{}'::jsonb,'restricted');",connection,transaction);
        audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=Guid.NewGuid()});audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});AddDeviceNullableGuid(audit,context.ActorSessionId);audit.Parameters.Add(new NpgsqlParameter<string>{TypedValue=eventType});audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=device.Id});audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.CorrelationId});audit.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=requestId});audit.Parameters.Add(new NpgsqlParameter<string>{TypedValue=JsonSerializer.Serialize(new{reason})});await audit.ExecuteNonQueryAsync(ct);
        var eventId=Guid.NewGuid();await using var domainEvent=new NpgsqlCommand("INSERT INTO governance.domain_events(id,organization_id,aggregate_id,aggregate_type,aggregate_version,event_type,actor_user_id,correlation_id,operation_id,idempotency_key,changed_fields,payload) VALUES($1,$2,$3,'device',$4,$5,$6,$7,$8,$9,ARRAY['device'],'{}'::jsonb);",connection,transaction);
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=eventId});domainEvent.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});domainEvent.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=device.Id});domainEvent.Parameters.Add(new NpgsqlParameter<long>{TypedValue=device.Version});domainEvent.Parameters.Add(new NpgsqlParameter<string>{TypedValue=eventType});domainEvent.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.ActorUserId});domainEvent.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.CorrelationId});domainEvent.Parameters.Add(new NpgsqlParameter<string>{TypedValue=context.OperationId});domainEvent.Parameters.Add(new NpgsqlParameter<string>{TypedValue=context.IdempotencyKey});await domainEvent.ExecuteNonQueryAsync(ct);
        await using var outbox=new NpgsqlCommand("INSERT INTO governance.outbox_messages(id,organization_id,domain_event_id,destination,message_type,payload) VALUES($1,$2,$3,'realtime',$4,'{}'::jsonb);",connection,transaction);outbox.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=Guid.NewGuid()});outbox.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=context.OrganizationId});outbox.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=eventId});outbox.Parameters.Add(new NpgsqlParameter<string>{TypedValue=eventType});await outbox.ExecuteNonQueryAsync(ct);
    }

    private static async global::System.Threading.Tasks.Task<DeviceReadProjection?> ReadDeviceAsync(NpgsqlConnection connection,NpgsqlTransaction? transaction,Guid organizationId,Guid deviceId,bool forUpdate,CancellationToken ct)
    {
        var sql="SELECT d.id,d.organization_id,d.user_account_id,o.version,o.created_at,o.updated_at,COALESCE(d.display_name,'Unnamed device'),d.platform,d.app_version,d.os_version,d.last_seen_at,d.revoked_at FROM iam.devices d JOIN core.objects o ON o.id=d.id AND o.organization_id=d.organization_id WHERE d.organization_id=$1 AND d.id=$2"+(forUpdate?" FOR UPDATE OF d,o":"");
        await using var command=new NpgsqlCommand(sql,connection,transaction);command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=deviceId});await using var reader=await command.ExecuteReaderAsync(ct);return await reader.ReadAsync(ct)?ReadDevice(reader):null;
    }

    private static DeviceReadProjection ReadDevice(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetInt64(3),r.GetFieldValue<DateTimeOffset>(4).ToUniversalTime(),r.GetFieldValue<DateTimeOffset>(5).ToUniversalTime(),r.GetString(6),r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetFieldValue<DateTimeOffset>(10).ToUniversalTime(),r.IsDBNull(11)?null:r.GetFieldValue<DateTimeOffset>(11).ToUniversalTime());
    private static void AddDeviceNullableText(NpgsqlCommand command,string? value)=>command.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Text,Value=value is null?DBNull.Value:value});
    private static void AddDeviceNullableGuid(NpgsqlCommand command,Guid? value)=>command.Parameters.Add(new NpgsqlParameter{NpgsqlDbType=NpgsqlDbType.Uuid,Value=value is null?DBNull.Value:value.Value});
    private sealed record DeviceAcquire(string Disposition,Guid RecordId,string? Body,int? RetryAfter);
}
