namespace Task.Application.Security;

/// <summary>
/// Login lookup projection for single-org deploy auth. LoginRequest carries no organizationId;
/// the store resolves the account by login alone and fail-closes on multi-org collisions.
/// </summary>
public sealed record AccountLoginRecord(
    Guid OrganizationId,
    Guid UserId,
    string Login,
    string PasswordHash,
    string PasswordParameters,
    long CredentialVersion,
    long AuthorizationScopeVersion,
    string AccountStatus,
    int FailedLoginCount,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset DbNowUtc,
    bool MustChangePassword = false);

/// <summary>
/// Persistence port for resolving a user account by login for the login flow.
/// Any account_status is returned; lockout and login services decide acceptance.
/// </summary>
public interface IAccountLookupStore
{
    /// <summary>
    /// Finds the account matching login. Matching is case-insensitive (citext), consistent
    /// with the uq_user_accounts_org_login unique constraint. Returns null when no row
    /// matches or when more than one organization shares the same login (fail closed).
    /// DbNowUtc is the PostgreSQL server clock (clock_timestamp()). AuthorizationScopeVersion
    /// comes from iam.authorization_scope_versions.version, or 1 when no scope row exists.
    /// </summary>
    global::System.Threading.Tasks.Task<AccountLoginRecord?> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken = default);
}
