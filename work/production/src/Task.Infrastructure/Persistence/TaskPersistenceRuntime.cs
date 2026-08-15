using System.Globalization;
using Npgsql;
using Task.Application;

namespace Task.Infrastructure.Persistence;

public sealed class TaskPersistenceRuntime : IDisposable, IAsyncDisposable
{
    public static int ExpectedMigrationVersion => TaskPersistenceMigrationCatalog.LatestVersion;
    public static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(3);

    private const int MinimumPostgresVersionNumber = 160000;
    private readonly NpgsqlDataSource? _dataSource;
    private readonly TimeSpan _readinessTimeout;
    private readonly bool _configurationInvalid;

    public TaskPersistenceRuntime(string? connectionString, TimeSpan? readinessTimeout = null)
    {
        _readinessTimeout = readinessTimeout ?? DefaultReadinessTimeout;
        if (_readinessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(readinessTimeout), "Readiness timeout must be positive.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            _dataSource = NpgsqlDataSource.Create(connectionString);
        }
        catch (ArgumentException)
        {
            _configurationInvalid = true;
        }
    }

    public bool IsConfigured => _dataSource is not null;

    public ITaskAggregateStore CreateTaskStore() =>
        new PostgresTaskAggregateStore(GetConfiguredDataSource());

    public TaskPersistenceMigrator CreateMigrator() =>
        new(GetConfiguredDataSource());

    public async Task<TaskPersistenceReadinessResult> CheckReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        if (_configurationInvalid)
        {
            return NotReady(
                TaskPersistenceReadinessCode.InvalidConfiguration,
                "PostgreSQL connection configuration is invalid.");
        }

        if (_dataSource is null)
        {
            return NotReady(
                TaskPersistenceReadinessCode.NotConfigured,
                "PostgreSQL connection is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readinessTimeout);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(timeout.Token);
            var serverVersionNumber = await ReadServerVersionNumber(connection, timeout.Token);
            if (serverVersionNumber < MinimumPostgresVersionNumber)
            {
                return NotReady(
                    TaskPersistenceReadinessCode.UnsupportedServerVersion,
                    "PostgreSQL 16 or newer is required.",
                    serverVersionNumber);
            }

            if (!await MigrationHistoryExists(connection, timeout.Token))
            {
                return NotReady(
                    TaskPersistenceReadinessCode.MigrationsNotApplied,
                    "Persistence migrations have not been applied.",
                    serverVersionNumber);
            }

            if (!await RequiredTablesExist(connection, timeout.Token))
            {
                return NotReady(
                    TaskPersistenceReadinessCode.SchemaObjectsMissing,
                    "Required persistence schema objects are missing.",
                    serverVersionNumber);
            }

            var migrations = await ReadAppliedMigrations(connection, timeout.Token);
            var expectedMigrations = TaskPersistenceMigrationCatalog.All;
            var schemaMatches = migrations.Count == expectedMigrations.Count;
            for (var index = 0; schemaMatches && index < expectedMigrations.Count; index++)
            {
                schemaMatches = migrations[index].Version == expectedMigrations[index].Version &&
                    string.Equals(migrations[index].Name, expectedMigrations[index].Name, StringComparison.Ordinal) &&
                    string.Equals(
                        migrations[index].Checksum,
                        expectedMigrations[index].Sha256,
                        StringComparison.OrdinalIgnoreCase);
            }

            if (!schemaMatches)
            {
                return NotReady(
                    TaskPersistenceReadinessCode.SchemaVersionMismatch,
                    "Applied persistence schema is incompatible with this server version.",
                    serverVersionNumber,
                    migrations.Count == 0 ? null : migrations[^1].Version);
            }

            return new TaskPersistenceReadinessResult(
                Ready: true,
                TaskPersistenceReadinessCode.Ready,
                "PostgreSQL is reachable and the persistence schema is compatible.",
                serverVersionNumber,
                ExpectedMigrationVersion,
                ExpectedMigrationVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return NotReady(
                TaskPersistenceReadinessCode.Timeout,
                "PostgreSQL readiness check timed out.");
        }
        catch (NpgsqlException)
        {
            return NotReady(
                TaskPersistenceReadinessCode.DatabaseUnavailable,
                "PostgreSQL is unavailable.");
        }
        catch (Exception)
        {
            return NotReady(
                TaskPersistenceReadinessCode.CheckFailed,
                "PostgreSQL readiness check failed safely.");
        }
    }

    public void Dispose()
    {
        _dataSource?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    private NpgsqlDataSource GetConfiguredDataSource() =>
        _dataSource ?? throw new InvalidOperationException("PostgreSQL persistence is not configured.");

    private static async Task<int> ReadServerVersionNumber(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW server_version_num;", connection);
        var raw = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new InvalidOperationException("PostgreSQL returned an invalid server version.");
    }

    private static async Task<bool> MigrationHistoryExists(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('infrastructure.schema_migrations') IS NOT NULL;",
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> RequiredTablesExist(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT to_regclass('core.organizations') IS NOT NULL
               AND to_regclass('core.objects') IS NOT NULL
               AND to_regclass('work.tasks') IS NOT NULL;
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<List<AppliedMigration>> ReadAppliedMigrations(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT version, name, btrim(sha256)
            FROM infrastructure.schema_migrations
            ORDER BY version;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new AppliedMigration(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return migrations;
    }

    private static TaskPersistenceReadinessResult NotReady(
        TaskPersistenceReadinessCode code,
        string message,
        int? serverVersionNumber = null,
        int? actualMigrationVersion = null) =>
        new(
            Ready: false,
            code,
            message,
            serverVersionNumber,
            ExpectedMigrationVersion,
            actualMigrationVersion);

    private sealed record AppliedMigration(int Version, string Name, string Checksum);
}

public sealed record TaskPersistenceReadinessResult(
    bool Ready,
    TaskPersistenceReadinessCode Code,
    string Message,
    int? ServerVersionNumber,
    int ExpectedMigrationVersion,
    int? ActualMigrationVersion);

public enum TaskPersistenceReadinessCode
{
    Ready = 0,
    NotConfigured = 1,
    InvalidConfiguration = 2,
    DatabaseUnavailable = 3,
    Timeout = 4,
    UnsupportedServerVersion = 5,
    MigrationsNotApplied = 6,
    SchemaObjectsMissing = 7,
    SchemaVersionMismatch = 8,
    CheckFailed = 9,
}
