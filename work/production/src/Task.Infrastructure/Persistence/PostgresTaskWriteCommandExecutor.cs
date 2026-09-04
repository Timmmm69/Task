using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Task.Application;

namespace Task.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL unit of work for Task commands. Aggregate, audit, event, outbox and durable
/// response writes share exactly one connection, transaction and commit.
/// </summary>
public sealed class PostgresTaskWriteCommandExecutor : ITaskWriteCommandExecutor
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetentionDuration = TimeSpan.FromDays(7);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresTaskWriteCommandExecutor(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
        TaskWriteCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var requestHash = command.RequestHash.ToArray();
        var leaseOwner = Guid.NewGuid();
        var proposedRecordId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            using (var scopeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('task-product-api:' || $1::text,0));", connection, transaction))
            { scopeLock.Parameters.AddWithValue(command.OrganizationId); await scopeLock.ExecuteNonQueryAsync(cancellationToken); }
            AcquireResult acquire;
            try
            {
                acquire = await AcquireAsync(
                    connection,
                    transaction,
                    command,
                    requestHash,
                    proposedRecordId,
                    leaseOwner,
                    cancellationToken);
            }
            catch (PostgresException exception)
                when (exception.SqlState == PostgresErrorCodes.CheckViolation &&
                    exception.MessageText == "IDEMPOTENCY_KEY_REUSED")
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(TaskWriteCommandDisposition.IdempotencyKeyReused, HttpResult: null);
            }

            if (acquire.Disposition == "replay")
            {
                var replayedTask = PostgresTaskAggregateStore.Get(connection, transaction, acquire.ResourceId ?? command.TaskId, command.OrganizationId);
                if (replayedTask is not null) PostgresTaskCardValidation.EnsureVisible(connection, transaction, replayedTask, command.ActorUserId);
                await transaction.CommitAsync(cancellationToken);
                return new(
                    TaskWriteCommandDisposition.Replayed,
                    ToHttpResult(acquire.Status, acquire.HeadersJson, acquire.BodyJson, acquire.ResourceId));
            }

            if (acquire.Disposition == "in_progress")
            {
                await transaction.CommitAsync(cancellationToken);
                return new(
                    TaskWriteCommandDisposition.RequestInProgress,
                    HttpResult: null,
                    TimeSpan.FromSeconds(acquire.RetryAfterSeconds ?? 1));
            }

            if (acquire.Disposition != "execute")
            {
                throw new InvalidOperationException("PostgreSQL returned an unknown idempotency disposition.");
            }

            await ValidateActorSessionAsync(connection, transaction, command, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var current = PostgresTaskAggregateStore.Get(
                connection,
                transaction,
                command.TaskId,
                command.OrganizationId);
            if (command.ExpectedVersion is null)
            {
                if (current is not null)
                {
                    throw new InvalidOperationException("The Task create command targets an existing aggregate.");
                }
            }
            else
            {
                if (current is null)
                {
                    throw new KeyNotFoundException("The Task aggregate was not found in the command organization.");
                }

                if (current.Metadata.Version != command.ExpectedVersion.Value)
                {
                    throw new TaskLifecycleConcurrencyException(
                        command.TaskId,
                        command.ExpectedVersion.Value,
                        current.Metadata.Version);
                }
            }

            if (current is not null) PostgresTaskCardValidation.EnsureVisible(connection, transaction, current, command.ActorUserId);
            var mutation = command.Mutation(current)
                ?? throw new InvalidOperationException("The Task mutation returned no result.");
            if (current is null || current.Content.ToJson() != mutation.Aggregate.Content.ToJson())
                PostgresTaskCardValidation.Validate(connection, transaction, mutation.Aggregate, command.ActorUserId, current);
            var changedFields = mutation.ChangedFields ?? command.ChangedFields;
            ValidateMutation(command, mutation, current, changedFields);
            var safePayloadJson = mutation.SafePayloadJson ?? command.SafePayloadJson;
            ValidateSafePayload(safePayloadJson, nameof(mutation.SafePayloadJson));
            var isNoOp = current is not null && changedFields.Count == 0;
            cancellationToken.ThrowIfCancellationRequested();

            if (isNoOp)
            {
                // Durable no-op: the response is completed under the idempotency lease below,
                // while aggregate, audit, event and outbox state remains untouched.
            }
            else if (command.ExpectedVersion is null)
            {
                PostgresTaskAggregateStore.Add(connection, transaction, mutation.Aggregate);
            }
            else
            {
                PostgresTaskAggregateStore.Save(
                    connection,
                    transaction,
                    mutation.Aggregate,
                    checked((int)command.ExpectedVersion.Value));
            }

            if (!isNoOp)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eventId = Guid.NewGuid();
                var auditMetadata = BuildAuditMetadata(command, changedFields, safePayloadJson);
                var outboxPayload = BuildOutboxPayload(command, mutation.Aggregate, eventId, changedFields, safePayloadJson);
                await AppendAuditAsync(
                    connection,
                    transaction,
                    command,
                    acquire.RecordId,
                    auditMetadata,
                    safePayloadJson,
                    cancellationToken);
                await AppendEventAndOutboxAsync(
                    connection,
                    transaction,
                    command,
                    mutation.Aggregate.Metadata.Version,
                    eventId,
                    outboxPayload,
                    safePayloadJson,
                    cancellationToken);
            }

            var headersJson = JsonSerializer.Serialize(mutation.HttpResult.Headers);
            await CompleteAsync(
                connection,
                transaction,
                command,
                requestHash,
                acquire.RecordId,
                leaseOwner,
                mutation.HttpResult,
                headersJson,
                cancellationToken);

            var storedResult = await ReadStoredResultAsync(
                connection,
                transaction,
                command,
                acquire.RecordId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(TaskWriteCommandDisposition.Executed, storedResult);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The transaction may already be completed or broken by a failed commit.
            }

            throw;
        }
    }

    private static async global::System.Threading.Tasks.Task<AcquireResult> AcquireAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        byte[] requestHash,
        Guid proposedRecordId,
        Guid leaseOwner,
        CancellationToken cancellationToken)
    {
        await using var acquire = new NpgsqlCommand(
            """
            SELECT disposition,
                   stored_record_id,
                   stored_response_status,
                   stored_response_headers::text,
                   stored_response_body::text,
                   stored_resource_id,
                   retry_after_seconds
            FROM iam.acquire_idempotency_record($1, $2, $3, $4, $5, $6, $7, $8, $9);
            """,
            connection,
            transaction);
        acquire.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = proposedRecordId });
        acquire.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        acquire.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
        acquire.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.OperationId });
        acquire.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.IdempotencyKey });
        acquire.Parameters.Add(new NpgsqlParameter<byte[]> { NpgsqlDbType = NpgsqlDbType.Bytea, TypedValue = requestHash });
        acquire.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = leaseOwner });
        acquire.Parameters.Add(new NpgsqlParameter<TimeSpan> { NpgsqlDbType = NpgsqlDbType.Interval, TypedValue = LeaseDuration });
        acquire.Parameters.Add(new NpgsqlParameter<TimeSpan> { NpgsqlDbType = NpgsqlDbType.Interval, TypedValue = RetentionDuration });

        await using var reader = await acquire.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("PostgreSQL returned no idempotency acquisition result.");
        }

        return new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetInt16(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6));
    }

    private static async global::System.Threading.Tasks.Task ValidateActorSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ActorSessionId is null)
        {
            return;
        }

        await using var check = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM iam.sessions
                WHERE id = $1 AND organization_id = $2 AND user_account_id = $3
            );
            """,
            connection,
            transaction);
        check.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorSessionId.Value });
        check.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        check.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
        if (!(bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            throw new InvalidOperationException("Actor session is outside the command tenant or user scope.");
        }
    }

    private static async global::System.Threading.Tasks.Task AppendAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        Guid requestId,
        string metadataJson,
        string safePayloadJson,
        CancellationToken cancellationToken)
    {
        await using var audit = new NpgsqlCommand(
            """
            INSERT INTO governance.audit_entries (
                id, organization_id, actor_user_id, actor_session_id, action_code,
                object_id, object_type, outcome, correlation_id, request_id,
                metadata, new_state, redaction_level)
            VALUES ($1, $2, $3, $4, $5, $6, 'task', 'success', $7, $8, $9, $10, 'standard');
            """,
            connection,
            transaction);
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
        AddNullableGuid(audit, command.ActorSessionId);
        audit.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.AuditAction });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.TaskId });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.CorrelationId });
        audit.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = requestId });
        AddJson(audit, metadataJson);
        AddJson(audit, safePayloadJson);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task AppendEventAndOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        int aggregateVersion,
        Guid eventId,
        string outboxPayload,
        string safePayloadJson,
        CancellationToken cancellationToken)
    {
        await using (var domainEvent = new NpgsqlCommand(
            """
            INSERT INTO governance.domain_events (
                id, organization_id, aggregate_id, aggregate_type, aggregate_version,
                event_type, actor_user_id, correlation_id, operation_id, idempotency_key,
                changed_fields, payload)
            VALUES ($1, $2, $3, 'task', $4, $5, $6, $7, $8, $9, $10, $11);
            """,
            connection,
            transaction))
        {
            domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
            domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
            domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.TaskId });
            domainEvent.Parameters.Add(new NpgsqlParameter<long> { TypedValue = aggregateVersion });
            domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.EventType });
            domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
            domainEvent.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.CorrelationId });
            domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.OperationId });
            domainEvent.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.IdempotencyKey });
            domainEvent.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = command.ChangedFields.ToArray() });
            AddJson(domainEvent, safePayloadJson);
            await domainEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var outbox = new NpgsqlCommand(
            """
            INSERT INTO governance.outbox_messages (
                id, organization_id, domain_event_id, destination, message_type, payload)
            VALUES ($1, $2, $3, 'realtime', $4, $5);
            """,
            connection,
            transaction);
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        outbox.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        outbox.Parameters.Add(new NpgsqlParameter<string> { TypedValue = command.EventType });
        AddJson(outbox, outboxPayload);
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task CompleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        byte[] requestHash,
        Guid recordId,
        Guid leaseOwner,
        TaskWriteHttpResult result,
        string headersJson,
        CancellationToken cancellationToken)
    {
        await using var complete = new NpgsqlCommand(
            "SELECT iam.complete_idempotency_record($1, $2, $3, $4, $5, $6, $7, $8, $9);",
            connection,
            transaction);
        complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = recordId });
        complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
        complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = leaseOwner });
        complete.Parameters.Add(new NpgsqlParameter<byte[]> { NpgsqlDbType = NpgsqlDbType.Bytea, TypedValue = requestHash });
        complete.Parameters.Add(new NpgsqlParameter<int> { TypedValue = result.StatusCode });
        AddJson(complete, headersJson);
        AddJson(complete, result.BodyJson);
        AddNullableGuid(complete, result.ResourceId);
        await complete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task<TaskWriteHttpResult> ReadStoredResultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskWriteCommand command,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await using var read = new NpgsqlCommand(
            """
            SELECT response_status, response_headers::text, response_body::text, resource_id
            FROM iam.idempotency_records
            WHERE id = $1 AND organization_id = $2 AND user_account_id = $3 AND state = 'completed';
            """,
            connection,
            transaction);
        read.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = recordId });
        read.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.OrganizationId });
        read.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = command.ActorUserId });
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Completed idempotency response could not be read.");
        }

        return ToHttpResult(
            reader.GetInt16(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private static TaskWriteHttpResult ToHttpResult(
        int? status,
        string? headersJson,
        string? bodyJson,
        Guid? resourceId)
    {
        if (status is null || headersJson is null || bodyJson is null)
        {
            throw new InvalidOperationException("Stored idempotency response is incomplete.");
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson)
            ?? throw new InvalidOperationException("Stored idempotency headers are invalid.");
        return new(status.Value, headers, bodyJson, resourceId);
    }

    private static void Validate(TaskWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureIdentifier(command.OrganizationId, nameof(command.OrganizationId));
        EnsureIdentifier(command.ActorUserId, nameof(command.ActorUserId));
        EnsureIdentifier(command.CorrelationId, nameof(command.CorrelationId));
        EnsureIdentifier(command.TaskId, nameof(command.TaskId));
        if (command.ActorSessionId == Guid.Empty)
        {
            throw new ArgumentException("Actor session identifier must not be empty.", nameof(command.ActorSessionId));
        }

        EnsureBounded(command.OperationId, 160, nameof(command.OperationId));
        EnsureBounded(command.AuditAction, 128, nameof(command.AuditAction));
        EnsureBounded(command.EventType, 100, nameof(command.EventType));
        if (command.ExpectedVersion is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(command.ExpectedVersion));
        }

        if (command.RequestHash.Length != 32)
        {
            throw new ArgumentException("Request hash must be exactly 32 bytes of SHA-256 output.", nameof(command.RequestHash));
        }

        if (command.IdempotencyKey.Length is < 8 or > 200 ||
            command.IdempotencyKey.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException(
                "Idempotency key must contain 8-200 printable ASCII characters without spaces.",
                nameof(command.IdempotencyKey));
        }

        if (command.ChangedFields.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Changed field names must not be empty.", nameof(command.ChangedFields));
        }

        ValidateSafePayload(command.SafePayloadJson, nameof(command.SafePayloadJson));
    }

    private static void ValidateMutation(
        TaskWriteCommand command,
        TaskWriteMutationResult mutation,
        global::Task.Domain.TaskAggregate? current,
        IReadOnlyList<string> changedFields)
    {
        ArgumentNullException.ThrowIfNull(mutation.Aggregate);
        ArgumentNullException.ThrowIfNull(mutation.HttpResult);
        if (mutation.Aggregate.Metadata.OrganizationId != command.OrganizationId ||
            mutation.Aggregate.Metadata.Id != command.TaskId)
        {
            throw new InvalidOperationException("Task mutation changed the command tenant or aggregate identity.");
        }

        if (changedFields.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Changed field names must not be empty.", nameof(mutation));
        }

        if (current is null && changedFields.Count == 0)
        {
            throw new InvalidOperationException("A Task create mutation cannot be a no-op.");
        }

        if (current is not null && changedFields.Count == 0 &&
            (!ReferenceEquals(current, mutation.Aggregate) ||
             mutation.Aggregate.Metadata.Version != current.Metadata.Version))
        {
            throw new InvalidOperationException("A no-op mutation must return the unchanged aggregate instance.");
        }

        if (current is not null && changedFields.Count > 0 &&
            mutation.Aggregate.Metadata.Version != checked(current.Metadata.Version + 1))
        {
            throw new InvalidOperationException("A visible Task mutation must advance the version exactly once.");
        }

        if (mutation.HttpResult.StatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(mutation.HttpResult.StatusCode));
        }

        ArgumentNullException.ThrowIfNull(mutation.HttpResult.Headers);
        foreach (var header in mutation.HttpResult.Headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) ||
                header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Durable response contains an unsafe header name.");
            }
        }

        TaskWriteRequestHasher.ValidateSafePayload(mutation.HttpResult.BodyJson, nameof(mutation.HttpResult.BodyJson));
    }

    private static string BuildAuditMetadata(
        TaskWriteCommand command,
        IReadOnlyList<string> changedFields,
        string safePayloadJson)
    {
        using var payload = JsonDocument.Parse(safePayloadJson);
        return JsonSerializer.Serialize(new
        {
            command.OperationId,
            changedFields,
            payload = payload.RootElement,
        });
    }

    private static string BuildOutboxPayload(
        TaskWriteCommand command,
        global::Task.Domain.TaskAggregate aggregate,
        Guid eventId,
        IReadOnlyList<string> changedFields,
        string safePayloadJson)
    {
        using var payload = JsonDocument.Parse(safePayloadJson);
        return JsonSerializer.Serialize(new
        {
            eventId,
            organizationId = command.OrganizationId,
            aggregateId = command.TaskId,
            aggregateType = "task",
            aggregateVersion = aggregate.Metadata.Version,
            eventType = command.EventType,
            correlationId = command.CorrelationId,
            changedFields,
            payload = payload.RootElement,
        });
    }

    private static void ValidateSafePayload(string safePayloadJson, string parameterName)
    {
        TaskWriteRequestHasher.ValidateSafePayload(safePayloadJson, parameterName);
        using var payload = JsonDocument.Parse(safePayloadJson);
        if (payload.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Safe payload must be a JSON object.", parameterName);
        }
    }

    private static void AddJson(NpgsqlCommand command, string json) =>
        command.Parameters.Add(new NpgsqlParameter<string> { NpgsqlDbType = NpgsqlDbType.Jsonb, TypedValue = json });

    private static void AddNullableGuid(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsureBounded(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new ArgumentException($"Value must contain 1-{maxLength} characters.", parameterName);
        }
    }

    private sealed record AcquireResult(
        string Disposition,
        Guid RecordId,
        int? Status,
        string? HeadersJson,
        string? BodyJson,
        Guid? ResourceId,
        int? RetryAfterSeconds);
}
