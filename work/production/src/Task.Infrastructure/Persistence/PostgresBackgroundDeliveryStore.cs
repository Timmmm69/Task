using Npgsql;
using NpgsqlTypes;
using Task.Application.Background;

namespace Task.Infrastructure.Persistence;

internal sealed class PostgresBackgroundDeliveryStore(NpgsqlDataSource dataSource) : IBackgroundDeliveryStore
{
    public async global::System.Threading.Tasks.Task<IReadOnlyList<OutboxDelivery>> ClaimOutboxAsync(
        string workerName,
        Guid leaseToken,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ValidateClaim(workerName, leaseToken, leaseDuration, batchSize);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH exhausted AS (
                UPDATE governance.outbox_messages
                   SET status = 'dead_letter', last_error_code = 'LEASE_EXHAUSTED',
                       last_error_detail = 'The final delivery lease expired before completion.',
                       locked_by = NULL, lock_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL
                 WHERE status = 'processing' AND lease_expires_at < clock_timestamp()
                   AND attempt_count >= 10
                RETURNING id
            ), candidates AS (
                SELECT id
                FROM governance.outbox_messages
                WHERE (((status IN ('pending','failed') AND attempt_count < 10)
                            AND next_attempt_at <= clock_timestamp()
                            AND available_at <= clock_timestamp())
                       OR (status = 'processing' AND attempt_count < 10
                            AND lease_expires_at < clock_timestamp()))
                ORDER BY available_at, created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT $4
            )
            UPDATE governance.outbox_messages AS message
               SET status = 'processing', locked_by = $1, lock_token = $2,
                   locked_at = clock_timestamp(), heartbeat_at = clock_timestamp(),
                   lease_expires_at = clock_timestamp() + $3, attempt_count = attempt_count + 1
              FROM candidates
             WHERE message.id = candidates.id
            RETURNING message.id, message.organization_id, message.destination,
                      message.message_type, message.payload::text, message.attempt_count;
            """,
            connection);
        AddClaimParameters(command, workerName, leaseToken, leaseDuration, batchSize);

        var deliveries = new List<OutboxDelivery>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), leaseToken));
        }
        return deliveries;
    }

    public async global::System.Threading.Tasks.Task DeliverOutboxAsync(
        OutboxDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var project = new NpgsqlCommand(
            """
            SELECT sync.project_domain_event_change(
                       event.id,
                       event.aggregate_id,
                       event.aggregate_type,
                       CASE
                           WHEN lower(event.event_type) LIKE '%deleted'
                             OR lower(event.event_type) LIKE '%.trash'
                             OR lower(event.event_type) LIKE '%movedtotrash'
                               THEN 'tombstone'
                           WHEN lower(event.event_type) LIKE '%scopechanged'
                             OR lower(event.event_type) LIKE '%roleschanged'
                               THEN 'scope_revoke'
                           ELSE 'upsert'
                       END,
                       event.aggregate_version,
                       coalesce((
                           SELECT max(scope.version)
                             FROM iam.authorization_scope_versions AS scope
                             JOIN iam.user_accounts AS account
                               ON account.id = scope.user_account_id
                            WHERE account.organization_id = event.organization_id
                       ), 1),
                       event.changed_fields,
                       jsonb_build_object(
                           'sourceEventId', event.id,
                           'eventType', event.event_type))
              FROM governance.outbox_messages AS message
              JOIN governance.domain_events AS event
                ON event.organization_id = message.organization_id
               AND event.id = message.domain_event_id
             WHERE message.id = $1
               AND message.status = 'processing'
               AND message.lock_token = $2;
            """,
            connection,
            transaction))
        {
            project.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.Id });
            project.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
            if (await project.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidOperationException("OUTBOX_LEASE_LOST");
        }

        await using (var complete = new NpgsqlCommand(
            """
            UPDATE governance.outbox_messages
               SET status = 'published', published_at = clock_timestamp(), locked_by = NULL,
                   lock_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL,
                   last_error_code = NULL, last_error_detail = NULL
             WHERE id = $1 AND status = 'processing' AND lock_token = $2;
            """,
            connection,
            transaction))
        {
            complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.Id });
            complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
            if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("OUTBOX_LEASE_LOST");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task RenewOutboxLeaseAsync(
        OutboxDelivery delivery,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ValidateLeaseDuration(leaseDuration);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE governance.outbox_messages
               SET heartbeat_at = clock_timestamp(),
                   lease_expires_at = clock_timestamp() + $3
             WHERE id = $1 AND status = 'processing' AND lock_token = $2
               AND lease_expires_at >= clock_timestamp();
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.Id });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>
        {
            NpgsqlDbType = NpgsqlDbType.Interval,
            TypedValue = leaseDuration,
        });
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("OUTBOX_LEASE_LOST");
    }

    public async global::System.Threading.Tasks.Task FailOutboxAsync(
        OutboxDelivery delivery,
        string errorCode,
        string errorDetail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE governance.outbox_messages
               SET status = CASE WHEN attempt_count >= 10 THEN 'dead_letter' ELSE 'failed' END,
                   next_attempt_at = clock_timestamp()
                       + make_interval(secs => least(3600, 5 * (2 ^ least(attempt_count, 9))::integer)),
                   last_error_code = left($3, 100), last_error_detail = left($4, 4000),
                   locked_by = NULL, lock_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL
             WHERE id = $1 AND status = 'processing' AND lock_token = $2;
            """,
            connection);
        AddFailureParameters(command, delivery.Id, delivery.LeaseToken, errorCode, errorDetail);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("OUTBOX_LEASE_LOST");
    }

    public async global::System.Threading.Tasks.Task<int> MaterializeDueRemindersAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchSize(batchSize);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH candidates AS (
                SELECT id, organization_id, next_trigger_at
                  FROM calendar.reminders
                 WHERE status IN ('scheduled','snoozed') AND next_trigger_at <= clock_timestamp()
                 ORDER BY next_trigger_at, id
                 FOR UPDATE SKIP LOCKED
                 LIMIT $1
            ), inserted AS (
                INSERT INTO calendar.reminder_occurrences (
                    id, organization_id, reminder_id, due_at, status, next_attempt_at, idempotency_key)
                SELECT gen_random_uuid(), organization_id, id, next_trigger_at, 'created',
                       next_trigger_at, id::text || '|' || next_trigger_at::text
                  FROM candidates
                ON CONFLICT (reminder_id, due_at) DO NOTHING
                RETURNING reminder_id, due_at
            ), advanced AS (
                UPDATE calendar.reminders AS reminder
                   SET status = 'due', version = version + 1, updated_at = clock_timestamp()
                  FROM inserted
                 WHERE reminder.id = inserted.reminder_id
                   AND reminder.next_trigger_at = inserted.due_at
                RETURNING reminder.id
            )
            SELECT count(*)::integer FROM advanced;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = batchSize });
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ReminderDelivery>> ClaimReminderDeliveriesAsync(
        string workerName,
        Guid leaseToken,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ValidateClaim(workerName, leaseToken, leaseDuration, batchSize);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH exhausted AS (
                UPDATE calendar.reminder_occurrences
                   SET status = 'dead_letter', last_error_code = 'LEASE_EXHAUSTED',
                       claimed_by_worker = NULL, lock_token = NULL,
                       lease_expires_at = NULL, heartbeat_at = NULL
                 WHERE status = 'claimed' AND lease_expires_at < clock_timestamp()
                   AND attempt_count >= 10
                RETURNING id
            ), candidates AS (
                SELECT occurrence.id
                  FROM calendar.reminder_occurrences AS occurrence
                 WHERE (((occurrence.status IN ('created','failed') AND occurrence.attempt_count < 10)
                            AND occurrence.next_attempt_at <= clock_timestamp()
                            AND occurrence.due_at <= clock_timestamp())
                       OR (occurrence.status = 'claimed' AND occurrence.attempt_count < 10
                            AND occurrence.lease_expires_at < clock_timestamp()))
                 ORDER BY occurrence.due_at, occurrence.id
                 FOR UPDATE SKIP LOCKED
                 LIMIT $4
            ), claimed AS (
                UPDATE calendar.reminder_occurrences AS occurrence
                   SET status = 'claimed', claimed_by_worker = $1, claimed_at = clock_timestamp(),
                       lock_token = $2, lease_expires_at = clock_timestamp() + $3,
                       heartbeat_at = clock_timestamp(), attempt_count = attempt_count + 1
                  FROM candidates
                 WHERE occurrence.id = candidates.id
                RETURNING occurrence.*
            )
            SELECT claimed.id, claimed.organization_id, claimed.reminder_id,
                   reminder.target_object_id, reminder.recipient_user_id, claimed.due_at,
                   claimed.idempotency_key, claimed.attempt_count
              FROM claimed
              JOIN calendar.reminders AS reminder
                ON reminder.organization_id = claimed.organization_id
               AND reminder.id = claimed.reminder_id
             ORDER BY claimed.due_at, claimed.id;
            """,
            connection);
        AddClaimParameters(command, workerName, leaseToken, leaseDuration, batchSize);

        var deliveries = new List<ReminderDelivery>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetGuid(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6),
                reader.GetInt32(7), leaseToken));
        }
        return deliveries;
    }

    public async global::System.Threading.Tasks.Task DeliverReminderAsync(
        ReminderDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var createObject = new NpgsqlCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, lifecycle_state, version,
                created_at, created_by, updated_at, updated_by)
            SELECT occurrence.id, occurrence.organization_id, 'notification', 'active', 1,
                   clock_timestamp(), reminder.created_by, clock_timestamp(), reminder.created_by
              FROM calendar.reminder_occurrences AS occurrence
              JOIN calendar.reminders AS reminder ON reminder.id = occurrence.reminder_id
             WHERE occurrence.id = $1 AND occurrence.status = 'claimed' AND occurrence.lock_token = $2;
            """,
            connection,
            transaction))
        {
            createObject.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OccurrenceId });
            createObject.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
            if (await createObject.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("REMINDER_LEASE_LOST");
        }

        await using (var createNotification = new NpgsqlCommand(
            """
            INSERT INTO notify.notifications (
                id, organization_id, recipient_user_id, notification_type, source_object_id,
                title, body, severity, status, not_before, delivered_at,
                deduplication_key, action_payload)
            VALUES ($1, $2, $3, 'reminder', $4, 'Напоминание',
                    'Наступило время напоминания.', 'info', 'delivered', $5,
                    clock_timestamp(), $6,
                    jsonb_build_object('reminderId', $7::text, 'targetObjectId', $4::text));
            """,
            connection,
            transaction))
        {
            createNotification.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OccurrenceId });
            createNotification.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OrganizationId });
            createNotification.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.RecipientUserId });
            createNotification.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.TargetObjectId });
            createNotification.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = delivery.DueAtUtc });
            createNotification.Parameters.Add(new NpgsqlParameter<string> { TypedValue = delivery.IdempotencyKey });
            createNotification.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.ReminderId });
            await createNotification.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var complete = new NpgsqlCommand(
            """
            WITH completed AS (
                UPDATE calendar.reminder_occurrences
                   SET status = 'delivered', delivered_at = clock_timestamp(),
                       claimed_by_worker = NULL, lock_token = NULL,
                       lease_expires_at = NULL, heartbeat_at = NULL, last_error_code = NULL
                 WHERE id = $1 AND status = 'claimed' AND lock_token = $2
                RETURNING reminder_id, due_at
            )
            UPDATE calendar.reminders AS reminder
               SET status = 'delivered', version = version + 1, updated_at = clock_timestamp()
              FROM completed
             WHERE reminder.id = completed.reminder_id
               AND reminder.next_trigger_at = completed.due_at;
            """,
            connection,
            transaction))
        {
            complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OccurrenceId });
            complete.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
            if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("REMINDER_LEASE_LOST");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task RenewReminderLeaseAsync(
        ReminderDelivery delivery,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ValidateLeaseDuration(leaseDuration);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE calendar.reminder_occurrences
               SET heartbeat_at = clock_timestamp(),
                   lease_expires_at = clock_timestamp() + $3
             WHERE id = $1 AND status = 'claimed' AND lock_token = $2
               AND lease_expires_at >= clock_timestamp();
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OccurrenceId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
        command.Parameters.Add(new NpgsqlParameter<TimeSpan>
        {
            NpgsqlDbType = NpgsqlDbType.Interval,
            TypedValue = leaseDuration,
        });
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("REMINDER_LEASE_LOST");
    }

    public async global::System.Threading.Tasks.Task FailReminderAsync(
        ReminderDelivery delivery,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE calendar.reminder_occurrences
               SET status = CASE WHEN attempt_count >= 10 THEN 'dead_letter' ELSE 'failed' END,
                   next_attempt_at = clock_timestamp()
                       + make_interval(secs => least(3600, 5 * (2 ^ least(attempt_count, 9))::integer)),
                   last_error_code = left($3, 80), claimed_by_worker = NULL,
                   lock_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL
             WHERE id = $1 AND status = 'claimed' AND lock_token = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.OccurrenceId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = delivery.LeaseToken });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = NormalizeError(errorCode, "DELIVERY_FAILED") });
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("REMINDER_LEASE_LOST");
    }

    private static void ValidateClaim(string workerName, Guid leaseToken, TimeSpan leaseDuration, int batchSize)
    {
        if (string.IsNullOrWhiteSpace(workerName)) throw new ArgumentException("Worker name is required.", nameof(workerName));
        if (workerName.Length > 200) throw new ArgumentOutOfRangeException(nameof(workerName));
        if (leaseToken == Guid.Empty) throw new ArgumentException("Lease token is required.", nameof(leaseToken));
        ValidateLeaseDuration(leaseDuration);
        ValidateBatchSize(batchSize);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
    }

    private static void AddClaimParameters(
        NpgsqlCommand command,
        string workerName,
        Guid leaseToken,
        TimeSpan leaseDuration,
        int batchSize)
    {
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = workerName });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = leaseToken });
        command.Parameters.Add(new NpgsqlParameter<TimeSpan> { NpgsqlDbType = NpgsqlDbType.Interval, TypedValue = leaseDuration });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = batchSize });
    }

    private static void AddFailureParameters(
        NpgsqlCommand command,
        Guid id,
        Guid leaseToken,
        string errorCode,
        string errorDetail)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = leaseToken });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = NormalizeError(errorCode, "DELIVERY_FAILED") });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = NormalizeError(errorDetail, "Delivery failed.") });
    }

    private static string NormalizeError(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
