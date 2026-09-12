using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of IAccountLockoutStore over iam.user_accounts
/// (migration 002). Failed-login increments and successful resets are serialized per account
/// with a transaction-scoped advisory lock, and the counter is incremented on the server
/// (UPDATE ... SET failed_login_count = failed_login_count + 1 ... RETURNING), so concurrent
/// attempts cannot lose increments. The lock deadline is written in the same statement and is
/// only ever extended, never shortened. All time is read from the database server clock
/// (clock_timestamp()).
/// </summary>
public sealed class PostgresAccountLockoutStore : IAccountLockoutStore
{
    private const string AccountLockoutAdvisoryLock =
        "SELECT pg_advisory_xact_lock(hashtextextended('task:account-lockout:' || $1::text, 0));";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAccountLockoutStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<LockoutState?> GetLockoutStateAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT failed_login_count, account_status, locked_until, clock_timestamp()
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

        return new LockoutState(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>
    /// Records one failed login attempt. The attempt is serialized with an advisory lock, and
    /// the counter is incremented atomically on the server, so the caller-supplied absolute
    /// count (computed from a possibly stale read) is ignored. The lock deadline is only ever
    /// extended, never shortened. Returns the incremented count, or 0 when no matching
    /// non-blocked account row exists.
    /// </summary>
    public async global::System.Threading.Tasks.Task<int> RecordFailedLoginAsync(
        Guid organizationId,
        Guid userId,
        int newFailedCount,
        DateTimeOffset? lockedUntilUtcOrNull,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        if (newFailedCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newFailedCount),
                "Failed login count must not be negative.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(AccountLockoutAdvisoryLock, connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET failed_login_count = failed_login_count + 1,
                locked_until = CASE
                    WHEN $3::timestamptz IS NULL THEN locked_until
                    WHEN locked_until IS NULL OR locked_until < $3 THEN $3
                    ELSE locked_until END
            WHERE organization_id = $1 AND id = $2 AND account_status <> 'blocked'
            RETURNING failed_login_count;
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = lockedUntilUtcOrNull is null ? DBNull.Value : lockedUntilUtcOrNull.Value,
        });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result is null ? 0 : (int)result;
    }

    /// <summary>
    /// Resets the failed-login counter and temporary lock. The reset is serialized with the same
    /// per-account advisory lock as <see cref="RecordFailedLoginAsync"/>, so it cannot interleave
    /// with a concurrent increment. Permanently blocked accounts are never reset.
    /// </summary>
    public async global::System.Threading.Tasks.Task RecordSuccessfulLoginAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(AccountLockoutAdvisoryLock, connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET failed_login_count = 0, locked_until = NULL
            WHERE organization_id = $1 AND id = $2 AND account_status <> 'blocked';
            """,
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
