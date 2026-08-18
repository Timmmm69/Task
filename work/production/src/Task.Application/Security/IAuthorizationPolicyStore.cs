namespace Task.Application.Security;

/// <summary>
/// Describes the effect of one role permission rule. A deny rule always outranks grant
/// rules for the same permission code; the decision engine applies that ordering.
/// </summary>
public enum PolicyRuleEffect
{
    Grant,
    Deny,
}

/// <summary>
/// One grant rule applicable to the user for the requested permission code. Only the
/// information required by the decision engine is exposed: the rule type and whether the
/// user holds the role directly through iam.user_roles (as opposed to gaining it
/// indirectly). No role identifiers or other details leave the data layer.
/// </summary>
public sealed record PolicyGrantRow(bool HasDirectRoleMembership);

/// <summary>
/// One deny rule applicable to the user for the requested permission code. Mirrors
/// <see cref="PolicyGrantRow"/>; a single non-empty result denies the permission.
/// </summary>
public sealed record PolicyDenyRow(bool HasDirectRoleMembership);

/// <summary>
/// Data-access port for authorization policy reads (iam.user_accounts, iam.roles,
/// iam.role_permissions, iam.user_roles). This is a pure data layer: it returns raw rule
/// rows and knows nothing about decision ordering, deny precedence or default outcomes.
/// All decision semantics live in <see cref="PermissionDecisionService"/>, so the engine
/// stays independent of the storage backend. Every request is filtered by organization,
/// user, permission code and rule effect; queries are read-only and idempotent.
/// </summary>
public interface IAuthorizationPolicyStore
{
    /// <summary>
    /// Returns the organization the user belongs to, or null when the user account does
    /// not exist. A null result is evaluated by the engine as DENIED_NO_ORG.
    /// </summary>
    Task<Guid?> GetUserOrgAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all grant rules for the given permission code applicable to the user in the
    /// organization through his roles (joined user_roles to roles to role_permissions).
    /// An empty result means the user has no grant for the code.
    /// </summary>
    Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
        Guid orgId,
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all deny rules for the given permission code applicable to the user in the
    /// organization through his roles. A non-empty result is evaluated by the engine as an
    /// explicit denial that outranks any grant.
    /// </summary>
    Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
        Guid orgId,
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default);
}