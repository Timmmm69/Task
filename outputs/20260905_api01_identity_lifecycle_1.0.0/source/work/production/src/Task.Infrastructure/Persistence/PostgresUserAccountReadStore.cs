using Npgsql;
using Task.Application.Security;

namespace Task.Infrastructure.Persistence;

/// <summary>
/// Tenant-scoped PostgreSQL projection for the Stage 2.2 User resource.
/// Credential hashes, password metadata and session state are deliberately not selected.
/// </summary>
public sealed class PostgresUserAccountReadStore : IUserAccountReadStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresUserAccountReadStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<UserAccountReadProjection?> GetByIdAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                ua.id, ua.organization_id, o.version, o.created_at, o.updated_at,
                ep.display_name, ep.first_name, ep.last_name, ua.login::text,
                ep.work_email::text, ep.department_id, ep.job_title, ua.account_status
            FROM iam.user_accounts AS ua
            INNER JOIN core.objects AS o
                ON o.id = ua.id AND o.organization_id = ua.organization_id
            INNER JOIN org.employee_profiles AS ep
                ON ep.id = ua.employee_profile_id AND ep.organization_id = ua.organization_id
            WHERE ua.organization_id = $1 AND ua.id = $2
              AND o.object_type = 'user_account' AND o.lifecycle_state = 'active';
            """, connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProjection(reader) : null;
    }

    private static UserAccountReadProjection ReadProjection(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2),
        ReadUtcTimestamp(reader, 3), ReadUtcTimestamp(reader, 4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetGuid(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        ParseAccountStatus(reader.GetString(12)));

    public async global::System.Threading.Tasks.Task<UserAccountReadPage> GetPageAsync(
        UserAccountReadPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdentifier(request.OrganizationId, nameof(request.OrganizationId));
        if (request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var filter = string.IsNullOrWhiteSpace(request.Filter) ? null : request.Filter.Trim();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                ua.id, ua.organization_id, o.version, o.created_at, o.updated_at,
                ep.display_name, ep.first_name, ep.last_name, ua.login::text,
                ep.work_email::text, ep.department_id, ep.job_title, ua.account_status
            FROM iam.user_accounts AS ua
            JOIN core.objects AS o ON o.id = ua.id AND o.organization_id = ua.organization_id
            JOIN org.employee_profiles AS ep ON ep.id = ua.employee_profile_id AND ep.organization_id = ua.organization_id
            WHERE ua.organization_id = $1
              AND o.object_type = 'user_account' AND o.lifecycle_state = 'active'
              AND ($2::text IS NULL OR ep.display_name ILIKE '%' || $2 || '%'
                   OR ua.login::text ILIKE '%' || $2 || '%'
                   OR ep.work_email::text ILIKE '%' || $2 || '%')
              AND ($3::uuid IS NULL OR ua.id > $3)
            ORDER BY ua.id
            OFFSET $4
            LIMIT $5;
            """, connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = request.OrganizationId });
        command.Parameters.Add(new NpgsqlParameter { Value = filter is null ? DBNull.Value : filter });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = request.Cursor is null ? DBNull.Value : request.Cursor.Value });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = request.Cursor is null ? (request.Page - 1) * request.PageSize : 0 });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = request.PageSize + 1 });

        var items = new List<UserAccountReadProjection>(request.PageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) items.Add(ReadProjection(reader));
        }

        var hasMore = items.Count > request.PageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        await using var count = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM iam.user_accounts ua
            JOIN core.objects o ON o.id=ua.id AND o.organization_id=ua.organization_id
            JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id
            WHERE ua.organization_id=$1 AND o.lifecycle_state='active'
              AND ($2::text IS NULL OR ep.display_name ILIKE '%' || $2 || '%'
                   OR ua.login::text ILIKE '%' || $2 || '%'
                   OR ep.work_email::text ILIKE '%' || $2 || '%');
            """, connection);
        count.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = request.OrganizationId });
        count.Parameters.Add(new NpgsqlParameter { Value = filter is null ? DBNull.Value : filter });
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
        return new UserAccountReadPage(items, hasMore ? items[^1].Id : null, total);
    }

    private static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime();

    private static UserAccountStatus ParseAccountStatus(string value) => value switch
    {
        "pending" => UserAccountStatus.PendingActivation,
        "active" => UserAccountStatus.Active,
        "blocked" => UserAccountStatus.Blocked,
        "deactivated" => UserAccountStatus.Deactivated,
        _ => throw new InvalidOperationException($"Unknown stored account status '{value}'."),
    };

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
