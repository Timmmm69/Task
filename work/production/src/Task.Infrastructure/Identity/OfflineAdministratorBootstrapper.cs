using Npgsql;
using Task.Infrastructure.Persistence;

namespace Task.Infrastructure.Identity;

public sealed class OfflineAdministratorBootstrapper
{
    private const long BootstrapLockId = 0x5441534B424F4F54;
    private readonly NpgsqlDataSource _dataSource;
    private readonly Argon2idPasswordHasher _passwordHasher;

    public OfflineAdministratorBootstrapper(NpgsqlDataSource dataSource, Argon2idPasswordHasher? passwordHasher = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _passwordHasher = passwordHasher ?? new Argon2idPasswordHasher();
    }

    public async Task<OfflineAdministratorBootstrapResult> BootstrapAsync(
        OfflineAdministratorBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var passwordHash = _passwordHasher.Hash(request.InitialPassword, request.PasswordPepper);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, cancellationToken);
        await EnsureMigrationCurrentAsync(connection, transaction, cancellationToken);
        await EnsureNotBootstrappedAsync(connection, transaction, cancellationToken);

        var organizationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var passwordHistoryId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var displayName = $"{request.FirstName.Trim()} {request.LastName.Trim()}";

        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """,
            cancellationToken,
            organizationId, request.OrganizationCode.Trim(), request.OrganizationName.Trim(), request.TimeZone.Trim());
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO core.objects (id, organization_id, object_type, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'employee_profile', clock_timestamp(), $3, clock_timestamp(), $3);
            """,
            cancellationToken, employeeId, organizationId, userId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO org.employee_profiles (id, organization_id, first_name, last_name, display_name, preferred_time_zone)
            VALUES ($1, $2, $3, $4, $5, $6);
            """,
            cancellationToken, employeeId, organizationId, request.FirstName.Trim(), request.LastName.Trim(), displayName, request.TimeZone.Trim());
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO core.objects (id, organization_id, object_type, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $2, 'user_account', clock_timestamp(), $1, clock_timestamp(), $1);
            """,
            cancellationToken, userId, organizationId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.user_accounts
                (id, organization_id, employee_profile_id, login, password_hash, password_algorithm, password_parameters, must_change_password)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, true);
            """,
            cancellationToken, userId, organizationId, employeeId, request.Login.Trim(), passwordHash.Encoded,
            passwordHash.Algorithm, passwordHash.ParametersJson);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.password_history (id, user_account_id, password_hash, password_algorithm, password_parameters)
            VALUES ($1, $2, $3, $4, $5::jsonb);
            """,
            cancellationToken, passwordHistoryId, userId, passwordHash.Encoded, passwordHash.Algorithm, passwordHash.ParametersJson);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.roles (id, organization_id, code, display_name, is_system)
            VALUES ($1, $2, 'system_administrator', 'System administrator', true);
            """,
            cancellationToken, roleId, organizationId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.role_permissions (role_id, permission_code)
            SELECT $1, code FROM iam.permissions WHERE is_active;
            """,
            cancellationToken, roleId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.user_roles (user_account_id, role_id, granted_by)
            VALUES ($1, $2, $1);
            """,
            cancellationToken, userId, roleId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO iam.authorization_scope_versions (user_account_id, version)
            VALUES ($1, 1);
            """,
            cancellationToken, userId);
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO governance.audit_entries
                (id, organization_id, actor_user_id, action_code, object_id, object_type, outcome, reason_code, correlation_id, request_id, metadata, redaction_level)
            VALUES ($1, $2, $3, 'identity.bootstrap_administrator.created', $3, 'user_account', 'success', 'OFFLINE_BOOTSTRAP', $4, $5,
                jsonb_build_object('login', $6, 'role', 'system_administrator'), 'restricted');
            """,
            cancellationToken, Guid.NewGuid(), organizationId, userId, correlationId, requestId, request.Login.Trim());

        await transaction.CommitAsync(cancellationToken);
        return new OfflineAdministratorBootstrapResult(organizationId, userId, roleId, request.Login.Trim());
    }

    private static async global::System.Threading.Tasks.Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1);", connection, transaction);
        command.Parameters.AddWithValue(BootstrapLockId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async global::System.Threading.Tasks.Task EnsureMigrationCurrentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var expected = TaskPersistenceMigrationCatalog.All[^1];
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM infrastructure.schema_migrations) = $1
               AND EXISTS (
                   SELECT 1 FROM infrastructure.schema_migrations
                   WHERE version = $1 AND name = $2 AND btrim(sha256) = $3);
            """, connection, transaction);
        command.Parameters.AddWithValue(expected.Version);
        command.Parameters.AddWithValue(expected.Name);
        command.Parameters.AddWithValue(expected.Sha256);
        if (!(bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            throw new OfflineAdministratorBootstrapException(OfflineAdministratorBootstrapError.MigrationsRequired);
        }
    }

    private static async global::System.Threading.Tasks.Task EnsureNotBootstrappedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM core.organizations) OR EXISTS (SELECT 1 FROM iam.user_accounts);", connection, transaction);
        if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? true))
        {
            throw new OfflineAdministratorBootstrapException(OfflineAdministratorBootstrapError.AlreadyCompleted);
        }
    }

    private static async global::System.Threading.Tasks.Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue(values[index]);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Validate(OfflineAdministratorBootstrapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OrganizationCode) || request.OrganizationCode.Trim().Length is < 2 or > 64 ||
            string.IsNullOrWhiteSpace(request.OrganizationName) || request.OrganizationName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.TimeZone) || request.TimeZone.Trim().Length > 64 ||
            string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Login) || request.Login.Trim().Length is < 3 or > 128 ||
            string.IsNullOrWhiteSpace(request.InitialPassword) || request.InitialPassword.Length < 16 ||
            string.IsNullOrWhiteSpace(request.PasswordPepper) || request.PasswordPepper.Length < 16)
        {
            throw new OfflineAdministratorBootstrapException(OfflineAdministratorBootstrapError.InvalidInput);
        }
    }
}

public sealed record OfflineAdministratorBootstrapRequest(
    string OrganizationCode,
    string OrganizationName,
    string TimeZone,
    string FirstName,
    string LastName,
    string Login,
    string InitialPassword,
    string PasswordPepper);

public sealed record OfflineAdministratorBootstrapResult(Guid OrganizationId, Guid UserId, Guid RoleId, string Login);

public sealed class OfflineAdministratorBootstrapException(OfflineAdministratorBootstrapError error) : Exception
{
    public OfflineAdministratorBootstrapError Error { get; } = error;
}

public enum OfflineAdministratorBootstrapError
{
    InvalidInput,
    MigrationsRequired,
    AlreadyCompleted,
}
