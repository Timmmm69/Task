using Npgsql;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of IAuthorizationPolicyStore over iam.user_accounts,
/// iam.roles, iam.role_permissions and iam.user_roles (migrations 002 and 004). All
/// queries are parameterized, read-only and filtered by organization and permission code.
/// A user with no iam.user_accounts row returns null from GetUserOrgAsync and the engine
/// evaluates it as DENIED_NO_ORG. With the current schema every applicable rule is reached
/// through a direct iam.user_roles membership, so HasDirectRoleMembership is always true;
/// the flag keeps the engine independent of future membership sources.
/// </summary>
public sealed class PostgresAuthorizationPolicyStore : IAuthorizationPolicyStore
{
    private const string GrantEffect = "grant";
    private const string DenyEffect = "deny";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAuthorizationPolicyStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async Task<Guid?> GetUserOrgAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT organization_id FROM iam.user_accounts WHERE id = $1 AND account_status = 'active';",
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : (Guid)result;
    }

    public async Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
        Guid orgId,
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(orgId, nameof(orgId));
        EnsureIdentifier(userId, nameof(userId));
        EnsureCode(permissionCode, nameof(permissionCode));

        var rows = await ReadRuleRowsAsync(
            orgId, userId, permissionCode, GrantEffect,
            static direct => new PolicyGrantRow(direct),
            cancellationToken);
        return rows;
    }

    public async Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
        Guid orgId,
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(orgId, nameof(orgId));
        EnsureIdentifier(userId, nameof(userId));
        EnsureCode(permissionCode, nameof(permissionCode));

        var rows = await ReadRuleRowsAsync(
            orgId, userId, permissionCode, DenyEffect,
            static direct => new PolicyDenyRow(direct),
            cancellationToken);
        return rows;
    }

    private async Task<IReadOnlyList<T>> ReadRuleRowsAsync<T>(
        Guid orgId,
        Guid userId,
        string permissionCode,
        string effect,
        Func<bool, T> rowFactory,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT 1
            FROM iam.role_permissions rp
            JOIN iam.roles r ON r.id = rp.role_id AND r.organization_id = $1
            JOIN iam.user_roles ur ON ur.role_id = rp.role_id AND ur.user_account_id = $2
            JOIN iam.permissions p ON p.code = rp.permission_code AND p.is_active
            WHERE rp.permission_code = $3 AND rp.effect = $4
              AND (ur.valid_until IS NULL OR ur.valid_until > statement_timestamp())
              AND ($4 <> 'deny' OR ur.department_id IS NULL);
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = orgId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = permissionCode });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = effect });

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(rowFactory(true));
        }

        return rows;
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsureCode(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Permission code is required.", parameterName);
        }
    }
}
