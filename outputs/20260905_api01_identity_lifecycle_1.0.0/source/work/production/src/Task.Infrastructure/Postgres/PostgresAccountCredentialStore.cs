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

    public async global::System.Threading.Tasks.Task<bool> GetMustChangePasswordAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT must_change_password
            FROM iam.user_accounts
            WHERE organization_id = $1 AND id = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is true;
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

    public async global::System.Threading.Tasks.Task<PasswordChangeCommitResult> CommitPasswordChangeAsync(
        Guid organizationId,
        Guid userId,
        PasswordHashRecord expectedCurrentHash,
        PasswordHashRecord newHash,
        long expectedCredentialVersion,
        Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(expectedCurrentHash);
        ArgumentNullException.ThrowIfNull(newHash);
        if (expectedCredentialVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedCredentialVersion),
                "Credential version must be positive.");
        }

        var newCredentialVersion = checked(expectedCredentialVersion + 1);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var credentialCommand = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET password_hash = $3,
                password_parameters = $4::jsonb,
                credential_version = $5,
                must_change_password = false, temporary_password_expires_at = NULL
            WHERE organization_id = $1
                AND id = $2
                AND credential_version = $6
                AND password_hash = $7
                AND password_parameters = $8::jsonb
                AND account_status = 'active';
            """,
            connection,
            transaction))
        {
            credentialCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            credentialCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            credentialCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = newHash.Hash });
            credentialCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = newHash.Parameters });
            credentialCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = newCredentialVersion });
            credentialCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedCredentialVersion });
            credentialCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = expectedCurrentHash.Hash });
            credentialCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = expectedCurrentHash.Parameters });
            if (await credentialCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PasswordChangeCommitResult(false, 0);
            }
        }

        if (currentSessionId is Guid sessionId)
        {
            EnsureIdentifier(sessionId, nameof(currentSessionId));
            await using var currentSessionCommand = new NpgsqlCommand(
                """
                UPDATE iam.sessions
                SET credential_version = $4
                WHERE organization_id = $1
                    AND user_account_id = $2
                    AND id = $3
                    AND credential_version = $5
                    AND revoked_at IS NULL
                    AND absolute_expires_at > clock_timestamp()
                    AND idle_expires_at > clock_timestamp();
                """,
                connection,
                transaction);
            currentSessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
            currentSessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            currentSessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
            currentSessionCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = newCredentialVersion });
            currentSessionCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedCredentialVersion });
            if (await currentSessionCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PasswordChangeCommitResult(false, 0);
            }
        }

        await using (var historyCommand = new NpgsqlCommand(
            """
            INSERT INTO iam.password_history (
                id, user_account_id, password_hash, password_algorithm, password_parameters)
            VALUES ($1, $2, $3, $4, $5::jsonb);
            """,
            connection,
            transaction))
        {
            historyCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = Guid.NewGuid() });
            historyCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            historyCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = expectedCurrentHash.Hash });
            historyCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = Argon2idAlgorithm });
            historyCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = expectedCurrentHash.Parameters });
            await historyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var tokenCommand = new NpgsqlCommand(
            """
            UPDATE iam.refresh_tokens
            SET revoked_at = COALESCE(revoked_at, clock_timestamp())
            WHERE session_id IN (
                SELECT id
                FROM iam.sessions
                WHERE organization_id = $1
                    AND user_account_id = $2
                    AND revoked_at IS NULL
                    AND ($3::uuid IS NULL OR id <> $3));
            """,
            connection,
            transaction))
        {
            AddCurrentSessionParameters(tokenCommand, organizationId, userId, currentSessionId);
            await tokenCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        int revokedSessionCount;
        await using (var sessionCommand = new NpgsqlCommand(
            """
            UPDATE iam.sessions
            SET revoked_at = clock_timestamp(),
                revoke_reason = 'password-change'
            WHERE organization_id = $1
                AND user_account_id = $2
                AND revoked_at IS NULL
                AND ($3::uuid IS NULL OR id <> $3);
            """,
            connection,
            transaction))
        {
            AddCurrentSessionParameters(sessionCommand, organizationId, userId, currentSessionId);
            revokedSessionCount = await sessionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PasswordChangeCommitResult(true, revokedSessionCount);
    }

    public async global::System.Threading.Tasks.Task<bool> ResetMustChangePasswordAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET must_change_password = false, temporary_password_expires_at = NULL
            WHERE organization_id = $1 AND id = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void AddCurrentSessionParameters(
        NpgsqlCommand command,
        Guid organizationId,
        Guid userId,
        Guid? currentSessionId)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Uuid,
            Value = currentSessionId is null ? DBNull.Value : currentSessionId.Value,
        });
    }
}
