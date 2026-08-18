using Npgsql;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of IAccountCredentialStore over iam.user_accounts and
/// iam.password_history (migration 002). iam.password_history carries no organization_id
/// column; history rows are scoped to an organization through iam.user_accounts so that
/// reads and writes only ever touch accounts of the given organization.
/// </summary>
public sealed class PostgresAccountCredentialStore : IAccountCredentialStore
{
    private const string Argon2idAlgorithm = "argon2id";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAccountCredentialStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<AccountCredential?> GetCredentialAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_parameters, credential_version, account_status
            FROM iam.user_accounts
            WHERE organization_id = $1 AND id = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AccountCredential(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3));
    }

    public async global::System.Threading.Tasks.Task<bool> UpdateCredentialAsync(
        Guid organizationId,
        Guid userId,
        PasswordHashRecord hash,
        int newCredentialVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(hash);
        if (newCredentialVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newCredentialVersion),
                "Credential version must be positive.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET password_hash = $3, password_parameters = $4::jsonb, credential_version = $5
            WHERE organization_id = $1 AND id = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = hash.Hash });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = hash.Parameters });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = newCredentialVersion });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async global::System.Threading.Tasks.Task AddPasswordToHistoryAsync(
        Guid organizationId,
        Guid userId,
        PasswordHashRecord hash,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(hash);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO iam.password_history (
                id, user_account_id, password_hash, password_algorithm, password_parameters)
            SELECT $1, ua.id, $2, $3, $4::jsonb
            FROM iam.user_accounts ua
            WHERE ua.organization_id = $5 AND ua.id = $6;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = hash.Hash });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Argon2idAlgorithm });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = hash.Parameters });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<PasswordHashRecord>> GetRecentPasswordHistoryAsync(
        Guid organizationId,
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "History limit must be at least 1.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT ph.password_hash, ph.password_parameters
            FROM iam.password_history ph
            JOIN iam.user_accounts ua ON ua.id = ph.user_account_id
            WHERE ua.organization_id = $1 AND ph.user_account_id = $2
            ORDER BY ph.created_at DESC
            LIMIT $3;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });

        var results = new List<PasswordHashRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PasswordHashRecord(reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}