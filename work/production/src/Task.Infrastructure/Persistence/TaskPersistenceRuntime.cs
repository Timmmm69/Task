using Npgsql;
using Task.Application;
using Task.Application.Audit;
using Task.Application.Calendar;
using Task.Application.ProductData;
using Task.Application.Security;
using Task.Infrastructure.Identity;
using Task.Infrastructure.Postgres;

namespace Task.Infrastructure.Persistence;

public sealed class TaskPersistenceRuntime : IDisposable, IAsyncDisposable
{
    public static int ExpectedMigrationVersion => TaskPersistenceMigrationCatalog.LatestVersion;
    public static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(3);

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

    public ITaskWriteCommandExecutor CreateTaskWriteCommandExecutor() =>
        new PostgresTaskWriteCommandExecutor(GetConfiguredDataSource());

    public ITaskReadStore CreateTaskReadStore() =>
        new PostgresTaskReadStore(GetConfiguredDataSource());

    public ICalendarEventStore CreateCalendarEventStore() =>
        new PostgresCalendarEventStore(GetConfiguredDataSource());

    public IScheduleStore CreateScheduleStore() =>
        new PostgresScheduleStore(GetConfiguredDataSource());

    public IRecurrenceStore CreateRecurrenceStore() =>
        new PostgresRecurrenceStore(GetConfiguredDataSource());

    public IProjectStore CreateProjectStore() =>
        new PostgresProjectStore(GetConfiguredDataSource());

    public IContactStore CreateContactStore() =>
        new PostgresContactStore(GetConfiguredDataSource());

    public ICatalogItemStore CreateCatalogItemStore() =>
        new PostgresCatalogItemStore(GetConfiguredDataSource());

    public INotificationStore CreateNotificationStore() =>
        new PostgresNotificationStore(GetConfiguredDataSource());

    public IProductSettingsStore CreateProductSettingsStore() =>
        new PostgresProductSettingsStore(GetConfiguredDataSource());

    public IProductLifecycleStore CreateProductLifecycleStore() =>
        new PostgresProductLifecycleStore(GetConfiguredDataSource());

    public ISessionRepository CreateSessionRepository() =>
        new PostgresSessionRepository(GetConfiguredDataSource());

    public IAccountLookupStore CreateAccountLookupStore() =>
        new PostgresAccountLookupStore(GetConfiguredDataSource());

    public IDeviceRegistrationStore CreateDeviceRegistrationStore() =>
        new PostgresDeviceRegistrationStore(GetConfiguredDataSource());

    public IAccountLockoutStore CreateAccountLockoutStore() =>
        new PostgresAccountLockoutStore(GetConfiguredDataSource());

    public IAuditEntryStore CreateAuditEntryStore() =>
        new PostgresAuditEntryStore(GetConfiguredDataSource());

    public IAccountCredentialStore CreateAccountCredentialStore() =>
        new PostgresAccountCredentialStore(GetConfiguredDataSource());

    public IAuthorizationPolicyStore CreateAuthorizationPolicyStore() =>
        new PostgresAuthorizationPolicyStore(GetConfiguredDataSource());

    public TaskPersistenceMigrator CreateMigrator() =>
        new(GetConfiguredDataSource());

    public OfflineAdministratorBootstrapper CreateOfflineAdministratorBootstrapper() =>
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
            var inspection = await new TaskPersistenceMigrator(_dataSource).InspectAsync(timeout.Token);
            return inspection.Status switch
            {
                TaskPersistenceMigrationStatus.Current => new TaskPersistenceReadinessResult(
                    Ready: true,
                    TaskPersistenceReadinessCode.Ready,
                    "PostgreSQL is reachable and the persistence schema is compatible.",
                    inspection.ServerVersionNumber,
                    inspection.ExpectedMigrationVersion,
                    inspection.ActualMigrationVersion),
                TaskPersistenceMigrationStatus.HistoryMissing or TaskPersistenceMigrationStatus.Pending => NotReady(
                    TaskPersistenceReadinessCode.MigrationsNotApplied,
                    "Persistence migrations have not been applied.",
                    inspection.ServerVersionNumber,
                    inspection.ActualMigrationVersion),
                TaskPersistenceMigrationStatus.UnsupportedServerVersion => NotReady(
                    TaskPersistenceReadinessCode.UnsupportedServerVersion,
                    "PostgreSQL 16 or newer is required.",
                    inspection.ServerVersionNumber,
                    inspection.ActualMigrationVersion),
                TaskPersistenceMigrationStatus.SchemaObjectsMissing => NotReady(
                    TaskPersistenceReadinessCode.SchemaObjectsMissing,
                    "Required persistence schema objects are missing.",
                    inspection.ServerVersionNumber,
                    inspection.ActualMigrationVersion),
                TaskPersistenceMigrationStatus.HistoryMismatch => NotReady(
                    TaskPersistenceReadinessCode.SchemaVersionMismatch,
                    "Applied persistence schema is incompatible with this server version.",
                    inspection.ServerVersionNumber,
                    inspection.ActualMigrationVersion),
                _ => NotReady(
                    TaskPersistenceReadinessCode.CheckFailed,
                    "PostgreSQL readiness check failed safely."),
            };
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
