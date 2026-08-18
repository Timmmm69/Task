using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of IAccountLockoutStore over iam.user_accounts
/// (migration 002). The failed-login counter is incremented atomically with a single
/// conditional UPDATE ... RETURNING; the lock deadline is written in the same statement.
/// All time is read from the database server clock (clock_timestamp()).
/// </summary>
public sealed class PostgresAccountLockoutStore : IAccountLockoutStore
{
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
        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.user_accounts
            SET failed_login_count = $3, locked_until = $4
            WHERE organization_id = $1 AND id = $2 AND account_status <> 'blocked'
            RETURNING failed_login_count;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = newFailedCount });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = lockedUntilUtcOrNull is null ? DBNull.Value : lockedUntilUtcOrNull.Value,
        });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0 : (int)result;
    }

    public async global::System.Threading.Tasks.Task RecordSuccessfulLoginAsync(
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
            SET failed_login_count = 0, locked_until = NULL
            WHERE organization_id = $1 AND id = $2 AND account_status <> 'blocked';
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
