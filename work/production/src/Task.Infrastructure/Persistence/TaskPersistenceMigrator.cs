using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Task.Infrastructure.Persistence;

public sealed class TaskPersistenceMigrator
{
    private const long MigrationLockId = 0x5441534B;
    private const int MinimumPostgresVersionNumber = 160000;
    private readonly NpgsqlDataSource _dataSource;

    public TaskPersistenceMigrator(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public void ApplyPending() => ApplyPendingAsync().GetAwaiter().GetResult();

    public async Task<TaskPersistenceMigrationInspection> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await InspectConnectionAsync(connection, transaction: null, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task ApplyPendingAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var serverVersionNumber = await ReadServerVersionNumberAsync(connection, transaction, cancellationToken);
        if (serverVersionNumber < MinimumPostgresVersionNumber)
        {
            throw new TaskPersistenceMigrationException(
                TaskPersistenceMigrationError.UnsupportedServerVersion);
        }

        await using (var migrationLock = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock($1);",
            connection,
            transaction))
        {
            migrationLock.Parameters.Add(new NpgsqlParameter<long> { TypedValue = MigrationLockId });
            var acquired = (bool)(await migrationLock.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!acquired)
            {
                throw new TaskPersistenceMigrationException(
                    TaskPersistenceMigrationError.LockUnavailable);
            }
        }

        var inspection = await InspectConnectionAsync(connection, transaction, cancellationToken);
        if (inspection.Status == TaskPersistenceMigrationStatus.Current)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (inspection.Status is not TaskPersistenceMigrationStatus.HistoryMissing and
            not TaskPersistenceMigrationStatus.Pending)
        {
            throw new TaskPersistenceMigrationException(
                inspection.Status == TaskPersistenceMigrationStatus.UnsupportedServerVersion
                    ? TaskPersistenceMigrationError.UnsupportedServerVersion
                    : TaskPersistenceMigrationError.SchemaIncompatible);
        }

        await using (var bootstrap = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS infrastructure;
            CREATE TABLE IF NOT EXISTS infrastructure.schema_migrations (
                version integer PRIMARY KEY,
                name text NOT NULL,
                sha256 char(64) NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """,
            connection,
            transaction))
        {
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        }

        var appliedCount = inspection.Status == TaskPersistenceMigrationStatus.Pending
            ? inspection.AppliedMigrationCount
            : 0;
        foreach (var migration in TaskPersistenceMigrationCatalog.All.Skip(appliedCount))
        {
            await using (var command = new NpgsqlCommand(migration.Sql, connection, transaction))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var record = new NpgsqlCommand(
                """
                INSERT INTO infrastructure.schema_migrations (version, name, sha256)
                VALUES ($1, $2, $3);
                """,
                connection,
                transaction);
            record.Parameters.Add(new NpgsqlParameter<int> { TypedValue = migration.Version });
            record.Parameters.Add(new NpgsqlParameter<string> { TypedValue = migration.Name });
            record.Parameters.Add(new NpgsqlParameter<string> { TypedValue = migration.Sha256 });
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    internal static TaskPersistenceMigrationStatus EvaluateHistory(
        IReadOnlyList<TaskPersistenceAppliedMigration> applied)
    {
        var expected = TaskPersistenceMigrationCatalog.All;
        if (applied.Count > expected.Count)
        {
            return TaskPersistenceMigrationStatus.HistoryMismatch;
        }

        for (var index = 0; index < applied.Count; index++)
        {
            if (applied[index].Version != expected[index].Version ||
                !string.Equals(applied[index].Name, expected[index].Name, StringComparison.Ordinal) ||
                !string.Equals(applied[index].Sha256, expected[index].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return TaskPersistenceMigrationStatus.HistoryMismatch;
            }
        }

        return applied.Count == expected.Count
            ? TaskPersistenceMigrationStatus.Current
            : TaskPersistenceMigrationStatus.Pending;
    }

    private static async Task<TaskPersistenceMigrationInspection> InspectConnectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var serverVersionNumber = await ReadServerVersionNumberAsync(connection, transaction, cancellationToken);
        if (serverVersionNumber < MinimumPostgresVersionNumber)
        {
            return Result(TaskPersistenceMigrationStatus.UnsupportedServerVersion, serverVersionNumber);
        }

        await using (var exists = new NpgsqlCommand(
            "SELECT to_regclass('infrastructure.schema_migrations') IS NOT NULL;",
            connection,
            transaction))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                return Result(TaskPersistenceMigrationStatus.HistoryMissing, serverVersionNumber);
            }
        }

        var applied = new List<TaskPersistenceAppliedMigration>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT version, name, btrim(sha256)
            FROM infrastructure.schema_migrations
            ORDER BY version;
            """,
            connection,
            transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var historyStatus = EvaluateHistory(applied);
        if (historyStatus != TaskPersistenceMigrationStatus.Current)
        {
            return Result(historyStatus, serverVersionNumber, applied);
        }

        await using var requiredTables = new NpgsqlCommand(
            """
            SELECT to_regclass('core.organizations') IS NOT NULL
               AND to_regclass('core.objects') IS NOT NULL
               AND to_regclass('work.tasks') IS NOT NULL
               AND to_regclass('org.employee_profiles') IS NOT NULL
               AND to_regclass('iam.user_accounts') IS NOT NULL
               AND to_regclass('governance.audit_entries') IS NOT NULL
               AND to_regclass('iam.idempotency_records') IS NOT NULL
               AND to_regclass('governance.domain_events') IS NOT NULL
               AND to_regclass('governance.outbox_messages') IS NOT NULL
               AND to_regclass('calendar.recurrence_series') IS NOT NULL
               AND to_regclass('calendar.recurrence_occurrences') IS NOT NULL
               AND to_regclass('calendar.recurrence_commands') IS NOT NULL
               AND to_regclass('projects.projects') IS NOT NULL
               AND to_regclass('crm.contacts') IS NOT NULL
               AND to_regclass('files.catalog_items') IS NOT NULL
               AND to_regclass('notify.notifications') IS NOT NULL
               AND to_regclass('crm.companies') IS NOT NULL
               AND to_regclass('crm.interactions') IS NOT NULL
               AND to_regclass('crm.communication_channels') IS NOT NULL
               AND to_regclass('crm.addresses') IS NOT NULL
               AND to_regclass('crm.company_contacts') IS NOT NULL
               AND to_regclass('projects.members') IS NOT NULL
               AND to_regclass('files.file_locations') IS NOT NULL
               AND to_regclass('files.location_checks') IS NOT NULL
               AND to_regclass('files.network_resources') IS NOT NULL
               AND to_regclass('core.object_links') IS NOT NULL
               AND to_regclass('core.product_search_snapshots') IS NOT NULL
               AND to_regclass('iam.product_api_commands') IS NOT NULL
               AND to_regclass('core.organization_settings') IS NOT NULL
               AND to_regclass('org.user_settings') IS NOT NULL
               AND to_regclass('notify.notification_preferences') IS NOT NULL
               AND to_regclass('governance.archive_entries') IS NOT NULL
               AND to_regclass('governance.trash_entries') IS NOT NULL
               AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_record_product_lifecycle'
                    AND tgrelid = to_regclass('core.objects') AND NOT tgisinternal AND tgenabled IN ('O', 'A'));
            """,
            connection,
            transaction);
        var objectsExist = (bool)(await requiredTables.ExecuteScalarAsync(cancellationToken) ?? false);
        return Result(
            objectsExist ? TaskPersistenceMigrationStatus.Current : TaskPersistenceMigrationStatus.SchemaObjectsMissing,
            serverVersionNumber,
            applied);
    }

    private static async Task<int> ReadServerVersionNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW server_version_num;", connection, transaction);
        var raw = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new InvalidOperationException("PostgreSQL returned an invalid server version.");
    }

    private static TaskPersistenceMigrationInspection Result(
        TaskPersistenceMigrationStatus status,
        int serverVersionNumber,
        IReadOnlyList<TaskPersistenceAppliedMigration>? applied = null) =>
        new(
            status,
            serverVersionNumber,
            TaskPersistenceMigrationCatalog.LatestVersion,
            applied is { Count: > 0 } ? applied[^1].Version : null,
            applied?.Count ?? 0);
}

public sealed record TaskPersistenceMigrationInspection(
    TaskPersistenceMigrationStatus Status,
    int ServerVersionNumber,
    int ExpectedMigrationVersion,
    int? ActualMigrationVersion,
    int AppliedMigrationCount);

public enum TaskPersistenceMigrationStatus
{
    Current,
    HistoryMissing,
    Pending,
    UnsupportedServerVersion,
    SchemaObjectsMissing,
    HistoryMismatch,
}

public sealed class TaskPersistenceMigrationException : Exception
{
    public TaskPersistenceMigrationException(TaskPersistenceMigrationError error)
        : base("The persistence migration operation could not be completed safely.")
    {
        Error = error;
    }

    public TaskPersistenceMigrationError Error { get; }
}

public enum TaskPersistenceMigrationError
{
    LockUnavailable,
    UnsupportedServerVersion,
    SchemaIncompatible,
}

internal sealed record TaskPersistenceAppliedMigration(int Version, string Name, string Sha256);
