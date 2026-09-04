using Npgsql;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed login lookup over iam.user_accounts with LEFT JOIN to
/// iam.authorization_scope_versions (migration 002). Single-org deploy: resolve by login
/// only; zero or multiple matches both yield null (fail closed on org collision).
/// The parameter is explicitly cast to citext so matching follows the case-insensitive
/// semantics of the uq_user_accounts_org_login constraint (an uncast text parameter would
/// resolve to the case-sensitive text = text operator). Never logs login or password material.
/// </summary>
public sealed class PostgresAccountLookupStore : IAccountLookupStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAccountLookupStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<AccountLoginRecord?> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                ua.organization_id,
                ua.id,
                ua.login::text,
                ua.password_hash,
                ua.password_parameters,
                ua.credential_version,
                COALESCE(asv.version, 1),
                ua.account_status,
                ua.failed_login_count,
                ua.locked_until,
                clock_timestamp(),
                ua.must_change_password,
                ua.temporary_password_expires_at
            FROM iam.user_accounts ua
            LEFT JOIN iam.authorization_scope_versions asv
                ON asv.user_account_id = ua.id
            WHERE ua.login = $1::citext
            LIMIT 2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = login });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var record = ReadRecord(reader);

        if (await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return record;
    }

    private static AccountLoginRecord ReadRecord(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
}
