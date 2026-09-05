using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Persistence;

public sealed class PostgresUserAccountCommandStore : IUserAccountCommandStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetentionDuration = TimeSpan.FromDays(7);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresUserAccountCommandStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public global::System.Threading.Tasks.Task<UserAccountCommandResult> CreateAsync(
        IdentityCommandContext context,
        UserAccountCreateCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteUserAsync(context, 201, async (connection, transaction, ct) =>
        {
            if (command.DepartmentId is Guid departmentId && !await DepartmentVisibleAsync(connection,transaction,context.OrganizationId,departmentId,ct))
                return Failure(IdentityCommandDisposition.NotFound);
            var userId = Guid.NewGuid();
            var profileId = Guid.NewGuid();
            await using var create = new NpgsqlCommand(
                """
                WITH organization AS (
                    SELECT default_time_zone FROM core.organizations WHERE id=$1 AND status='active'
                ), profile_object AS (
                    INSERT INTO core.objects(id,organization_id,object_type,version,created_at,created_by,updated_at,updated_by)
                    SELECT $2,$1,'employee_profile',1,clock_timestamp(),$3,clock_timestamp(),$3 FROM organization
                ), account_object AS (
                    INSERT INTO core.objects(id,organization_id,object_type,version,created_at,created_by,updated_at,updated_by)
                    SELECT $4,$1,'user_account',1,clock_timestamp(),$3,clock_timestamp(),$3 FROM organization
                ), profile AS (
                    INSERT INTO org.employee_profiles(
                        id,organization_id,first_name,last_name,display_name,job_title,work_email,
                        department_id,employment_status,preferred_time_zone)
                    SELECT $2,$1,$5,$6,$7,$8,$9,$10,'active',default_time_zone FROM organization
                ), account AS (
                    INSERT INTO iam.user_accounts(
                        id,organization_id,employee_profile_id,login,password_hash,password_parameters,
                        credential_version,account_status,must_change_password)
                    VALUES($4,$1,$2,$11,$12,$13::jsonb,1,'pending',true)
                )
                INSERT INTO iam.authorization_scope_versions(user_account_id,version) VALUES($4,1);
                """, connection, transaction);
            create.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
            create.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = profileId });
            create.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
            create.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.FirstName.Trim() });
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.LastName.Trim() });
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.DisplayName.Trim() });
            AddNullableText(create, command.JobTitle);
            AddNullableText(create, command.WorkEmail);
            AddNullableGuid(create, command.DepartmentId);
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.Login.Trim() });
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.InitialCredential.Hash });
            create.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.InitialCredential.Parameters });
            await create.ExecuteNonQueryAsync(ct);
            return Success((await ReadAsync(connection, transaction, context.OrganizationId, userId, ct))!,
                "UserCreated", ["displayName", "firstName", "lastName", "login", "workEmail", "departmentId", "jobTitle", "accountStatus"]);
        }, cancellationToken);

    public global::System.Threading.Tasks.Task<UserAccountCommandResult> UpdateAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        UserAccountPatchCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteUserAsync(context, 200, async (connection, transaction, ct) =>
        {
            var current = await ReadAsync(connection, transaction, context.OrganizationId, userId, ct, forUpdate: true);
            if (current is null) return Failure(IdentityCommandDisposition.NotFound);
            if (current.Version != expectedVersion) return Failure(IdentityCommandDisposition.VersionConflict);

            if (command.DepartmentId.IsSpecified && command.DepartmentId.Value is Guid departmentId
                && !await DepartmentVisibleAsync(connection,transaction,context.OrganizationId,departmentId,ct))
                return Failure(IdentityCommandDisposition.NotFound);

            await using var update = new NpgsqlCommand(
                """
                WITH mutation_0 AS (
                    UPDATE org.employee_profiles ep SET
                    display_name=CASE WHEN $3 THEN $4 ELSE display_name END,
                    first_name=CASE WHEN $5 THEN $6 ELSE first_name END,
                    last_name=CASE WHEN $7 THEN $8 ELSE last_name END,
                    work_email=CASE WHEN $9 THEN $10::text::citext ELSE work_email END,
                    department_id=CASE WHEN $11 THEN $12::uuid ELSE department_id END,
                    job_title=CASE WHEN $13 THEN $14::text ELSE job_title END
                FROM iam.user_accounts ua
                WHERE ua.organization_id=$1 AND ua.id=$2 AND ep.id=ua.employee_profile_id RETURNING 1
                ),
                mutation_1 AS (
                    UPDATE iam.user_accounts SET login=CASE WHEN $15 THEN $16::text::citext ELSE login END
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                )
                UPDATE core.objects SET version=version+1,updated_at=clock_timestamp(),updated_by=$17
                WHERE organization_id=$1 AND id=$2;
                """, connection, transaction);
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            AddOptionalText(update, command.DisplayName);
            AddOptionalText(update, command.FirstName);
            AddOptionalText(update, command.LastName);
            AddOptionalNullableText(update, command.WorkEmail);
            AddOptionalNullableGuid(update, command.DepartmentId);
            AddOptionalNullableText(update, command.JobTitle);
            AddOptionalText(update, command.Login);
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
            await update.ExecuteNonQueryAsync(ct);
            var changed = ChangedFields(command);
            return Success((await ReadAsync(connection, transaction, context.OrganizationId, userId, ct))!, "UserUpdated", changed);
        }, cancellationToken);

    public global::System.Threading.Tasks.Task<UserAccountCommandResult> TransitionAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        UserAccountTransition transition,
        string? reason,
        CancellationToken cancellationToken = default) =>
        ExecuteUserAsync(context, 200, async (connection, transaction, ct) =>
        {
            var current = await ReadAsync(connection, transaction, context.OrganizationId, userId, ct, forUpdate: true);
            if (current is null) return Failure(IdentityCommandDisposition.NotFound);
            if (current.Version != expectedVersion) return Failure(IdentityCommandDisposition.VersionConflict);
            var target = transition switch
            {
                UserAccountTransition.Activate => UserAccountStatus.Active,
                UserAccountTransition.Block => UserAccountStatus.Blocked,
                UserAccountTransition.Deactivate => UserAccountStatus.Deactivated,
                UserAccountTransition.Reactivate or UserAccountTransition.Unblock => UserAccountStatus.Active,
                _ => throw new ArgumentOutOfRangeException(nameof(transition)),
            };
            if (!IsAllowed(current.AccountStatus, transition)) return Failure(IdentityCommandDisposition.InvalidStateTransition);
            if (current.AccountStatus == target) return Success(current, null, []);
            if (target != UserAccountStatus.Active)
            {
                await using var lastAdmin = new NpgsqlCommand("SELECT iam.permission_granted($1,$2,'organization.manage') AND NOT EXISTS (SELECT 1 FROM iam.user_accounts u WHERE u.organization_id=$1 AND u.id<>$2 AND u.account_status='active' AND iam.permission_granted($1,u.id,'organization.manage'));", connection, transaction);
                lastAdmin.Parameters.AddWithValue(context.OrganizationId); lastAdmin.Parameters.AddWithValue(userId);
                if (await lastAdmin.ExecuteScalarAsync(ct) is true) return Failure(IdentityCommandDisposition.InvalidStateTransition);
            }

            await using var update = new NpgsqlCommand(
                """
                WITH mutation_0 AS (
                    UPDATE iam.user_accounts SET account_status=$3,failed_login_count=0,locked_until=NULL
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_1 AS (
                    UPDATE core.objects SET version=version+1,updated_at=clock_timestamp(),updated_by=$4
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_2 AS (
                    UPDATE iam.authorization_scope_versions SET version=version+1,updated_at=clock_timestamp()
                WHERE user_account_id=$2 AND $5 RETURNING 1
                ),
                mutation_3 AS (
                    UPDATE iam.refresh_tokens SET revoked_at=COALESCE(revoked_at,clock_timestamp())
                WHERE session_id IN (SELECT id FROM iam.sessions WHERE organization_id=$1 AND user_account_id=$2) AND $5 RETURNING 1
                )
                UPDATE iam.sessions SET revoked_at=COALESCE(revoked_at,clock_timestamp()),revoke_reason=COALESCE(revoke_reason,$6)
                WHERE organization_id=$1 AND user_account_id=$2 AND $5;
                """, connection, transaction);
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            update.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToStoredStatus(target) });
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
            update.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = transition != UserAccountTransition.Activate });
            update.Parameters.Add(new NpgsqlParameter<string> { TypedValue = transition.ToString().ToLowerInvariant() });
            await update.ExecuteNonQueryAsync(ct);
            var eventType = transition switch
            {
                UserAccountTransition.Activate => "UserActivated",
                UserAccountTransition.Block => "UserBlocked",
                UserAccountTransition.Deactivate => "UserDeactivated",
                UserAccountTransition.Reactivate => "UserReactivated",
                UserAccountTransition.Unblock => "UserUnblocked",
                _ => throw new ArgumentOutOfRangeException(nameof(transition)),
            };
            return Success((await ReadAsync(connection, transaction, context.OrganizationId, userId, ct))!, eventType, ["accountStatus"]) with { Reason = reason };
        }, cancellationToken);

    public async global::System.Threading.Tasks.Task<PasswordResetCommandResult> ResetPasswordAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        PasswordHashRecord temporaryCredential,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteUserAsync(context, 200, async (connection, transaction, ct) =>
        {
            var current = await ReadAsync(connection, transaction, context.OrganizationId, userId, ct, forUpdate: true);
            if (current is null) return Failure(IdentityCommandDisposition.NotFound);
            if (current.Version != expectedVersion) return Failure(IdentityCommandDisposition.VersionConflict);
            await using var update = new NpgsqlCommand(
                """
                WITH mutation_0 AS (
                    UPDATE iam.user_accounts SET password_hash=$3,password_parameters=$4::jsonb,
                    credential_version=credential_version+1,must_change_password=true,temporary_password_expires_at=transaction_timestamp()+interval '24 hours',
                    failed_login_count=0,locked_until=NULL
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_1 AS (
                    UPDATE core.objects SET version=version+1,updated_at=transaction_timestamp(),updated_by=$5
                WHERE organization_id=$1 AND id=$2 RETURNING 1
                ),
                mutation_2 AS (
                    UPDATE iam.refresh_tokens SET revoked_at=COALESCE(revoked_at,clock_timestamp())
                WHERE session_id IN (SELECT id FROM iam.sessions WHERE organization_id=$1 AND user_account_id=$2) RETURNING 1
                )
                UPDATE iam.sessions SET revoked_at=COALESCE(revoked_at,clock_timestamp()),revoke_reason=COALESCE(revoke_reason,'admin-password-reset')
                WHERE organization_id=$1 AND user_account_id=$2;
                """, connection, transaction);
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            update.Parameters.Add(new NpgsqlParameter<string> { TypedValue = temporaryCredential.Hash });
            update.Parameters.Add(new NpgsqlParameter<string> { TypedValue = temporaryCredential.Parameters });
            update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
            await update.ExecuteNonQueryAsync(ct);
            return Success((await ReadAsync(connection, transaction, context.OrganizationId, userId, ct))!, "PasswordResetByAdmin", ["credentialVersion", "mustChangePassword"]);
        }, cancellationToken);
        return new PasswordResetCommandResult(result.Disposition, result.User?.Version, result.RetryAfterSeconds, result.User?.UpdatedAtUtc.AddHours(24));
    }

    private async global::System.Threading.Tasks.Task<UserAccountCommandResult> ExecuteUserAsync(
        IdentityCommandContext context,
        int successStatus,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, global::System.Threading.Tasks.Task<Mutation>> mutate,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var scope = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('task-product-api:' || $1::text,0));", connection, transaction))
        { scope.Parameters.AddWithValue(context.OrganizationId); await scope.ExecuteNonQueryAsync(cancellationToken); }
        var owner = Guid.NewGuid();
        try
        {
            var acquire = await AcquireAsync(connection, transaction, context, owner, cancellationToken);
            if (acquire.Disposition == "replay")
            {
                var replay = acquire.BodyJson is null ? null : JsonSerializer.Deserialize<UserAccountReadProjection>(acquire.BodyJson);
                await transaction.CommitAsync(cancellationToken);
                return new UserAccountCommandResult(IdentityCommandDisposition.Replayed, replay);
            }
            if (acquire.Disposition == "in_progress")
            {
                await transaction.CommitAsync(cancellationToken);
                return new UserAccountCommandResult(IdentityCommandDisposition.RequestInProgress, RetryAfterSeconds: acquire.RetryAfterSeconds);
            }
            var mutation = await mutate(connection, transaction, cancellationToken);
            if (mutation.Disposition != IdentityCommandDisposition.Executed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new UserAccountCommandResult(mutation.Disposition);
            }
            if (mutation.EventType is not null)
            {
                await AppendEvidenceAsync(connection, transaction, context, acquire.RecordId, mutation.User!, mutation.EventType, mutation.ChangedFields, mutation.Reason, cancellationToken);
            }
            await CompleteAsync(connection, transaction, context, acquire.RecordId, owner, successStatus, mutation.User!, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new UserAccountCommandResult(IdentityCommandDisposition.Executed, mutation.User);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation && exception.MessageText == "IDEMPOTENCY_KEY_REUSED")
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new UserAccountCommandResult(IdentityCommandDisposition.IdempotencyKeyReused);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new UserAccountCommandResult(IdentityCommandDisposition.DuplicateResource);
        }
    }

    private static async global::System.Threading.Tasks.Task<Acquire> AcquireAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IdentityCommandContext context,
        Guid owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT disposition,stored_record_id,stored_response_body::text,stored_resource_id,retry_after_seconds FROM iam.acquire_idempotency_record($1,$2,$3,$4,$5,$6,$7,$8,$9);",
            connection, transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context.OperationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context.IdempotencyKey });
        command.Parameters.Add(new NpgsqlParameter<byte[]> { NpgsqlDbType = NpgsqlDbType.Bytea, TypedValue = context.RequestHash });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = owner });
        command.Parameters.Add(new NpgsqlParameter<TimeSpan> { NpgsqlDbType = NpgsqlDbType.Interval, TypedValue = LeaseDuration });
        command.Parameters.Add(new NpgsqlParameter<TimeSpan> { NpgsqlDbType = NpgsqlDbType.Interval, TypedValue = RetentionDuration });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new Acquire(reader.GetString(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetInt32(4));
    }

    private static async global::System.Threading.Tasks.Task CompleteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IdentityCommandContext context,
        Guid recordId, Guid owner, int status, UserAccountReadProjection user, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT iam.complete_idempotency_record($1,$2,$3,$4,$5,$6,$7::jsonb,$8::jsonb,$9);", connection, transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = recordId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = owner });
        command.Parameters.Add(new NpgsqlParameter<byte[]> { NpgsqlDbType = NpgsqlDbType.Bytea, TypedValue = context.RequestHash });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = status });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = JsonSerializer.Serialize(new Dictionary<string, string> { ["ETag"] = $"\"v{user.Version}\"" }) });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = JsonSerializer.Serialize(user) });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = user.Id });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task AppendEvidenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IdentityCommandContext context,
        Guid requestId, UserAccountReadProjection user, string eventType, IReadOnlyList<string> changedFields, string? reason,
        CancellationToken cancellationToken)
    {
        await using var audit = new NpgsqlCommand(
            "INSERT INTO governance.audit_entries(id,organization_id,actor_user_id,actor_session_id,action_code,object_id,object_type,outcome,correlation_id,request_id,metadata,new_state,redaction_level) VALUES($1,$2,$3,$4,$5,$6,'user_account','success',$7,$8,$9::jsonb,'{}'::jsonb,'restricted');",
            connection, transaction);
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
        AddNullableGuid(audit, context.ActorSessionId);
        audit.Parameters.Add(new NpgsqlParameter<string> { TypedValue = eventType });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = user.Id });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.CorrelationId });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = requestId });
        audit.Parameters.Add(new NpgsqlParameter<string> { TypedValue = JsonSerializer.Serialize(new { reason }) });
        await audit.ExecuteNonQueryAsync(cancellationToken);

        var eventId = Guid.NewGuid();
        await using var domainEvent = new NpgsqlCommand(
            "INSERT INTO governance.domain_events(id,organization_id,aggregate_id,aggregate_type,aggregate_version,event_type,actor_user_id,correlation_id,operation_id,idempotency_key,changed_fields,payload) VALUES($1,$2,$3,'user_account',$4,$5,$6,$7,$8,$9,$10,'{}'::jsonb);",
            connection, transaction);
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = user.Id });
        domainEvent.Parameters.Add(new NpgsqlParameter<long> { TypedValue = user.Version });
        domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = eventType });
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.ActorUserId });
        domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.CorrelationId });
        domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context.OperationId });
        domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context.IdempotencyKey });
        domainEvent.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = changedFields.ToArray() });
        await domainEvent.ExecuteNonQueryAsync(cancellationToken);
        await using var outbox = new NpgsqlCommand("INSERT INTO governance.outbox_messages(id,organization_id,domain_event_id,destination,message_type,payload) VALUES($1,$2,$3,'realtime',$4,'{}'::jsonb);", connection, transaction);
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = context.OrganizationId });
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        outbox.Parameters.Add(new NpgsqlParameter<string> { TypedValue = eventType });
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task<UserAccountReadProjection?> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid organizationId, Guid userId,
        CancellationToken cancellationToken, bool forUpdate = false)
    {
        var sql = """
            SELECT ua.id,ua.organization_id,o.version,o.created_at,o.updated_at,ep.display_name,ep.first_name,
                   ep.last_name,ua.login::text,ep.work_email::text,ep.department_id,ep.job_title,ua.account_status
            FROM iam.user_accounts ua JOIN core.objects o ON o.id=ua.id AND o.organization_id=ua.organization_id
            JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id
            WHERE ua.organization_id=$1 AND ua.id=$2 AND o.lifecycle_state='active'
            """ + (forUpdate ? " FOR UPDATE OF ua,o,ep" : "");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new UserAccountReadProjection(reader.GetGuid(0),reader.GetGuid(1),reader.GetInt64(2),
            reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime(),reader.GetFieldValue<DateTimeOffset>(4).ToUniversalTime(),
            reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),
            reader.IsDBNull(9)?null:reader.GetString(9),reader.IsDBNull(10)?null:reader.GetGuid(10),
            reader.IsDBNull(11)?null:reader.GetString(11),ParseStatus(reader.GetString(12)));
    }

    private static async global::System.Threading.Tasks.Task<bool> DepartmentVisibleAsync(
        NpgsqlConnection connection,NpgsqlTransaction transaction,Guid organizationId,Guid departmentId,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM core.objects WHERE organization_id=$1 AND id=$2 AND object_type='department' AND lifecycle_state='active');",connection,transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=organizationId});
        command.Parameters.Add(new NpgsqlParameter<Guid>{TypedValue=departmentId});
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static bool IsAllowed(UserAccountStatus current, UserAccountTransition transition) => transition switch
    {
        UserAccountTransition.Activate => current == UserAccountStatus.PendingActivation,
        UserAccountTransition.Block => current is UserAccountStatus.Active or UserAccountStatus.Blocked,
        UserAccountTransition.Deactivate => current is not UserAccountStatus.Deactivated,
        UserAccountTransition.Reactivate => current is UserAccountStatus.Deactivated or UserAccountStatus.Active,
        UserAccountTransition.Unblock => current is UserAccountStatus.Blocked or UserAccountStatus.Active,
        _ => false,
    };

    private static string ToStoredStatus(UserAccountStatus status) => status switch
    {
        UserAccountStatus.PendingActivation => "pending", UserAccountStatus.Active => "active",
        UserAccountStatus.Blocked => "blocked", UserAccountStatus.Deactivated => "deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static UserAccountStatus ParseStatus(string status) => status switch
    {
        "pending" => UserAccountStatus.PendingActivation, "active" => UserAccountStatus.Active,
        "blocked" => UserAccountStatus.Blocked, "deactivated" => UserAccountStatus.Deactivated,
        _ => throw new InvalidOperationException("Unknown account status."),
    };

    private static IReadOnlyList<string> ChangedFields(UserAccountPatchCommand command)
    {
        var fields = new List<string>();
        if (command.DisplayName.IsSpecified) fields.Add("displayName"); if (command.FirstName.IsSpecified) fields.Add("firstName");
        if (command.LastName.IsSpecified) fields.Add("lastName"); if (command.Login.IsSpecified) fields.Add("login");
        if (command.WorkEmail.IsSpecified) fields.Add("workEmail"); if (command.DepartmentId.IsSpecified) fields.Add("departmentId");
        if (command.JobTitle.IsSpecified) fields.Add("jobTitle"); return fields;
    }

    private static Mutation Success(UserAccountReadProjection user, string? eventType, IReadOnlyList<string> fields) => new(IdentityCommandDisposition.Executed,user,eventType,fields);
    private static Mutation Failure(IdentityCommandDisposition disposition) => new(disposition,null,null,[]);
    private static void AddNullableText(NpgsqlCommand command, string? value) => command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Text,Value=value is null?DBNull.Value:value.Trim() });
    private static void AddNullableGuid(NpgsqlCommand command, Guid? value) => command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Uuid,Value=value is null?DBNull.Value:value.Value });
    private static void AddOptionalText(NpgsqlCommand command, OptionalUserField<string> value) { command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=value.IsSpecified}); AddNullableText(command,value.Value); }
    private static void AddOptionalNullableText(NpgsqlCommand command, OptionalUserField<string?> value) { command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=value.IsSpecified}); AddNullableText(command,value.Value); }
    private static void AddOptionalNullableGuid(NpgsqlCommand command, OptionalUserField<Guid?> value) { command.Parameters.Add(new NpgsqlParameter<bool>{TypedValue=value.IsSpecified}); AddNullableGuid(command,value.Value); }

    private sealed record Acquire(string Disposition, Guid RecordId, string? BodyJson, Guid? ResourceId, int? RetryAfterSeconds);
    private sealed record Mutation(IdentityCommandDisposition Disposition, UserAccountReadProjection? User, string? EventType, IReadOnlyList<string> ChangedFields, string? Reason = null);
}
