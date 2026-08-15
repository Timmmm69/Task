using Npgsql;
using NpgsqlTypes;

namespace Task.Infrastructure.Persistence;

public sealed class TaskPersistenceMigrator
{
    private const long MigrationLockId = 0x5441534B;

    private readonly NpgsqlDataSource _dataSource;

    public TaskPersistenceMigrator(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public void ApplyPending()
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var bootstrap = new NpgsqlCommand(
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
            bootstrap.ExecuteNonQuery();
        }

        using (var migrationLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock($1);",
            connection,
            transaction))
        {
            migrationLock.Parameters.Add(new NpgsqlParameter<long> { TypedValue = MigrationLockId });
            migrationLock.ExecuteNonQuery();
        }

        foreach (var migration in TaskPersistenceMigrationCatalog.All)
        {
            var appliedChecksum = GetAppliedChecksum(connection, transaction, migration.Version);

            if (appliedChecksum is not null)
            {
                if (!string.Equals(appliedChecksum, migration.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Applied migration {migration.Version} has a different SHA-256 checksum.");
                }

                continue;
            }

            using (var command = new NpgsqlCommand(migration.Sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            using var record = new NpgsqlCommand(
                """
                INSERT INTO infrastructure.schema_migrations (version, name, sha256)
                VALUES ($1, $2, $3);
                """,
                connection,
                transaction);
            record.Parameters.Add(new NpgsqlParameter<int> { TypedValue = migration.Version });
            record.Parameters.Add(new NpgsqlParameter<string> { TypedValue = migration.Name });
            record.Parameters.Add(new NpgsqlParameter<string> { TypedValue = migration.Sha256 });
            record.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string? GetAppliedChecksum(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int version)
    {
        using var command = new NpgsqlCommand(
            "SELECT sha256 FROM infrastructure.schema_migrations WHERE version = $1;",
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = version });
        return command.ExecuteScalar() as string;
    }

}
