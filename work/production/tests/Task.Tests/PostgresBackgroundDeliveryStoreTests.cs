using Npgsql;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresBackgroundDeliveryStoreTests(ITestOutputHelper output)
{
    private const string AdminConnectionVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_BackgroundDeliveryIsLeasedIdempotentAndRetrySafe()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            output.WriteLine($"NOT RUN: set {AdminConnectionVariable} for the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_delivery_{Guid.NewGuid():N}";
        var runtimeRole = $"task_delivery_role_{Guid.NewGuid():N}";
        var roleCreated = false;
        using var admin = NpgsqlDataSource.Create(adminConnectionString);
        Execute(admin, $"CREATE DATABASE {databaseName};");
        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;
            using var owner = NpgsqlDataSource.Create(databaseConnection);
            new TaskPersistenceMigrator(owner).ApplyPending();

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            Seed(owner, organizationId, userId, targetId);

            Execute(admin,
                $"CREATE ROLE {runtimeRole} LOGIN PASSWORD 'task-delivery-runtime-test-only' NOSUPERUSER NOCREATEDB NOCREATEROLE;");
            roleCreated = true;
            ApplyRuntimeGrants(owner, runtimeRole);
            var runtimeConnection = new NpgsqlConnectionStringBuilder(databaseConnection)
            {
                Username = runtimeRole,
                Password = "task-delivery-runtime-test-only",
            }.ConnectionString;
            using var runtime = new TaskPersistenceRuntime(runtimeConnection, TimeSpan.FromSeconds(10));
            var store = runtime.CreateBackgroundDeliveryStore();
            Assert.False(Scalar<bool>(owner,
                "SELECT has_table_privilege($1, 'sync.change_feed', 'INSERT');", runtimeRole));

            var firstOutboxId = InsertOutbox(owner, organizationId, userId, targetId, "TaskChanged");
            var firstToken = Guid.NewGuid();
            var firstClaim = await store.ClaimOutboxAsync("gate-a", firstToken, TimeSpan.FromMinutes(1), 10);
            Assert.Single(firstClaim);
            Assert.Equal(firstOutboxId, firstClaim[0].Id);
            Assert.Empty(await store.ClaimOutboxAsync("gate-b", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));

            await store.RenewOutboxLeaseAsync(firstClaim[0], TimeSpan.FromMinutes(1));
            await store.DeliverOutboxAsync(firstClaim[0]);
            Assert.Equal("published", Scalar<string>(owner,
                "SELECT status FROM governance.outbox_messages WHERE id=$1;", firstOutboxId));
            Assert.Equal(1L, Scalar<long>(owner,
                "SELECT count(*) FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", firstOutboxId));
            Assert.Equal(targetId, Scalar<Guid>(owner,
                "SELECT object_id FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", firstOutboxId));
            Assert.Equal("task", Scalar<string>(owner,
                "SELECT object_type FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", firstOutboxId));
            Assert.Equal("upsert", Scalar<string>(owner,
                "SELECT operation FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", firstOutboxId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeliverOutboxAsync(firstClaim[0]));
            Assert.Equal(1L, Scalar<long>(owner,
                "SELECT count(*) FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", firstOutboxId));

            var deletedOutboxId = InsertOutbox(owner, organizationId, userId, targetId, "TaskDeleted");
            var deleted = Assert.Single(await store.ClaimOutboxAsync(
                "gate-delete", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Assert.Equal(deletedOutboxId, deleted.Id);
            await store.DeliverOutboxAsync(deleted);
            Assert.Equal("tombstone", Scalar<string>(owner,
                "SELECT operation FROM sync.change_feed WHERE source_event_id=(SELECT domain_event_id FROM governance.outbox_messages WHERE id=$1);", deletedOutboxId));

            var recoveredOutboxId = InsertOutbox(owner, organizationId, userId, targetId, "TaskRecovered");
            var abandoned = Assert.Single(await store.ClaimOutboxAsync(
                "crashed-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Execute(owner,
                "UPDATE governance.outbox_messages SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=$1;",
                recoveredOutboxId);
            var recovered = Assert.Single(await store.ClaimOutboxAsync(
                "recovery-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Assert.Equal(abandoned.Id, recovered.Id);
            Assert.Equal(2, recovered.AttemptCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RenewOutboxLeaseAsync(abandoned, TimeSpan.FromMinutes(1)));
            await store.RenewOutboxLeaseAsync(recovered, TimeSpan.FromMinutes(1));
            await store.DeliverOutboxAsync(recovered);

            var deadLetterId = InsertOutbox(owner, organizationId, userId, targetId, "TaskPoisoned");
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                var poison = Assert.Single(await store.ClaimOutboxAsync(
                    "retry-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
                Assert.Equal(deadLetterId, poison.Id);
                Assert.Equal(attempt, poison.AttemptCount);
                await store.FailOutboxAsync(poison, "EXPECTED_FAILURE", "integration gate");
                Execute(owner,
                    "UPDATE governance.outbox_messages SET next_attempt_at=clock_timestamp()-interval '1 second' WHERE id=$1;",
                    deadLetterId);
            }
            Assert.Equal("dead_letter", Scalar<string>(owner,
                "SELECT status FROM governance.outbox_messages WHERE id=$1;", deadLetterId));
            Assert.Empty(await store.ClaimOutboxAsync("retry-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));

            var exhaustedLeaseId = InsertOutbox(owner, organizationId, userId, targetId, "TaskLeaseExhausted");
            Execute(owner,
                """
                UPDATE governance.outbox_messages
                   SET status='processing', attempt_count=10, locked_by='lost-worker',
                       lock_token=$2, locked_at=clock_timestamp()-interval '2 minutes',
                       heartbeat_at=clock_timestamp()-interval '2 minutes',
                       lease_expires_at=clock_timestamp()-interval '1 minute'
                 WHERE id=$1;
                """,
                exhaustedLeaseId, Guid.NewGuid());
            Assert.Empty(await store.ClaimOutboxAsync(
                "recovery-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Assert.Equal("dead_letter", Scalar<string>(owner,
                "SELECT status FROM governance.outbox_messages WHERE id=$1;", exhaustedLeaseId));

            var reminderId = Guid.NewGuid();
            Execute(owner,
                """
                INSERT INTO calendar.reminders (
                    id, organization_id, target_object_id, recipient_user_id, trigger_type,
                    absolute_trigger_at, next_trigger_at, created_by)
                VALUES ($1,$2,$3,$4,'absolute',clock_timestamp()-interval '1 minute',
                        clock_timestamp()-interval '1 minute',$4);
                """,
                reminderId, organizationId, targetId, userId);
            Assert.Equal(1, await store.MaterializeDueRemindersAsync(10));
            Assert.Equal(0, await store.MaterializeDueRemindersAsync(10));
            var reminder = Assert.Single(await store.ClaimReminderDeliveriesAsync(
                "reminder-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Assert.Equal(reminderId, reminder.ReminderId);
            await store.RenewReminderLeaseAsync(reminder, TimeSpan.FromMinutes(1));
            await store.DeliverReminderAsync(reminder);

            Assert.Equal("delivered", Scalar<string>(owner,
                "SELECT status FROM calendar.reminder_occurrences WHERE id=$1;", reminder.OccurrenceId));
            Assert.Equal("delivered", Scalar<string>(owner,
                "SELECT status FROM calendar.reminders WHERE id=$1;", reminderId));
            Assert.Equal("delivered", Scalar<string>(owner,
                "SELECT status FROM notify.notifications WHERE id=$1;", reminder.OccurrenceId));
            Assert.Equal(1L, Scalar<long>(owner,
                "SELECT count(*) FROM notify.notifications WHERE deduplication_key=$1;", reminder.IdempotencyKey));
            Assert.Empty(await store.ClaimReminderDeliveriesAsync(
                "reminder-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));

            var exhaustedReminderId = Guid.NewGuid();
            Execute(owner,
                """
                INSERT INTO calendar.reminders (
                    id, organization_id, target_object_id, recipient_user_id, trigger_type,
                    absolute_trigger_at, next_trigger_at, created_by)
                VALUES ($1,$2,$3,$4,'absolute',clock_timestamp()-interval '1 minute',
                        clock_timestamp()-interval '1 minute',$4);
                """,
                exhaustedReminderId, organizationId, targetId, userId);
            Assert.Equal(1, await store.MaterializeDueRemindersAsync(10));
            var exhaustedReminder = Assert.Single(await store.ClaimReminderDeliveriesAsync(
                "lost-reminder-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Execute(owner,
                """
                UPDATE calendar.reminder_occurrences
                   SET attempt_count=10, claimed_at=clock_timestamp()-interval '2 minutes',
                       heartbeat_at=clock_timestamp()-interval '2 minutes',
                       lease_expires_at=clock_timestamp()-interval '1 minute'
                 WHERE id=$1;
                """,
                exhaustedReminder.OccurrenceId);
            Assert.Empty(await store.ClaimReminderDeliveriesAsync(
                "recovery-reminder-worker", Guid.NewGuid(), TimeSpan.FromMinutes(1), 10));
            Assert.Equal("dead_letter", Scalar<string>(owner,
                "SELECT status FROM calendar.reminder_occurrences WHERE id=$1;", exhaustedReminder.OccurrenceId));

            output.WriteLine(
                "PASS: v14 outbox projection, SKIP LOCKED claim, lease recovery, exponential retry/DLQ, and reminder notification delivery.");
        }
        finally
        {
            Execute(admin, $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
            if (roleCreated) Execute(admin, $"DROP ROLE {runtimeRole};");
        }
    }

    private static void Seed(NpgsqlDataSource owner, Guid organizationId, Guid userId, Guid targetId)
    {
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Execute(owner,
            "INSERT INTO core.organizations(id,code,name,default_time_zone) VALUES($1,$2,'Delivery gate','Europe/Minsk');",
            organizationId, organizationId.ToString("N"));
        Execute(owner,
            """
            INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by)
            VALUES($1,$3,'employee_profile',$4,$2,$4,$2),
                  ($2,$3,'user_account',$4,$2,$4,$2),
                  ($5,$3,'task',$4,$2,$4,$2);
            """,
            profileId, userId, organizationId, now, targetId);
        Execute(owner,
            "INSERT INTO org.employee_profiles(id,organization_id,first_name,last_name,display_name,preferred_time_zone) VALUES($1,$2,'Delivery','User','Delivery User','Europe/Minsk');",
            profileId, organizationId);
        Execute(owner,
            "INSERT INTO iam.user_accounts(id,organization_id,employee_profile_id,login,password_hash,password_parameters) VALUES($1,$2,$3,$4,$5,'{}'::jsonb);",
            userId, organizationId, profileId, userId.ToString("N"), new string('h', 64));
    }

    private static Guid InsertOutbox(
        NpgsqlDataSource owner,
        Guid organizationId,
        Guid userId,
        Guid aggregateId,
        string eventType)
    {
        var eventId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        Execute(owner,
            """
            INSERT INTO governance.domain_events(
                id,organization_id,aggregate_id,aggregate_type,aggregate_version,event_type,
                actor_user_id,correlation_id,operation_id,idempotency_key,payload)
            VALUES($1,$2,$3,'task',1,$4,$5,$6,$7,$8,'{}'::jsonb);
            """,
            eventId, organizationId, aggregateId, eventType, userId, Guid.NewGuid(),
            $"gate.{eventId:N}", $"gate-key-{eventId:N}");
        Execute(owner,
            """
            INSERT INTO governance.outbox_messages(
                id,organization_id,domain_event_id,destination,message_type,payload)
            VALUES($1,$2,$3,'sync',$4,'{}'::jsonb);
            """,
            outboxId, organizationId, eventId, eventType);
        return outboxId;
    }

    private static void ApplyRuntimeGrants(NpgsqlDataSource owner, string runtimeRole)
    {
        var assembly = typeof(PostgresBackgroundDeliveryStoreTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("grant-runtime.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = string.Join('\n', reader.ReadToEnd().Split('\n')
                .Where(line => !line.TrimStart().StartsWith('\\')))
            .Replace("task_runtime", runtimeRole, StringComparison.Ordinal);
        Execute(owner, sql);
    }

    private static T Scalar<T>(NpgsqlDataSource source, string sql, params object[] values)
    {
        using var command = source.CreateCommand(sql);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return (T)command.ExecuteScalar()!;
    }

    private static void Execute(NpgsqlDataSource source, string sql, params object[] values)
    {
        using var command = source.CreateCommand(sql);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        command.ExecuteNonQuery();
    }
}
