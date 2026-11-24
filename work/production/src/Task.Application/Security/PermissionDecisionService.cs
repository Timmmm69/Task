namespace Task.Application.Security;

/// <summary>
/// High-level outcome of an access decision. The reason is restricted to a fixed public
/// set and intentionally carries no details about which rule matched: detailed diagnostics
/// remain available only to privileged tooling, never to callers or responses.
/// </summary>
public enum AuthorizationDenyReason
{
    None,
    NoOrg,
    ExplicitDeny,
    DefaultDeny,
}

/// <summary>
/// Result of one authorization evaluation. When <see cref="Allowed"/> is true the reason is
/// <see cref="AuthorizationDenyReason.None"/>; otherwise it is one of the deny reasons above.
/// </summary>
public sealed record AuthorizationDecision(
    bool Allowed,
    AuthorizationDenyReason Reason,
    string PermissionCode);

/// <summary>
/// Stateless authorization decision engine (solution D3). Evaluates whether a user of an
/// organization may exercise a permission code, with outcomes in this order:
/// 1) no organization for the user (or an organization mismatch) — DENIED_NO_ORG;
/// 2) an explicit deny rule for the user — DENIED_EXPLICIT (deny outranks any grant);
/// 3) a grant through a role the user holds directly (iam.user_roles);
/// 4) a grant through any role of the user;
/// 5) otherwise — DENIED_DEFAULT.
/// The engine holds no state and depends only on <see cref="IAuthorizationPolicyStore"/>,
/// which is a pure data-access port and never makes decisions itself.
/// </summary>
public sealed class PermissionDecisionService
{
    private readonly IAuthorizationPolicyStore _store;

    public PermissionDecisionService(IAuthorizationPolicyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<AuthorizationDecision> EvaluateAsync(
        Guid orgId,
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(orgId, nameof(orgId));
        EnsureIdentifier(userId, nameof(userId));
        EnsureCode(permissionCode, nameof(permissionCode));
        var rules = await _store.ReadRulesAsync(orgId, userId, [permissionCode], cancellationToken);
        return Decide(rules, permissionCode);
    }

    public async Task<IReadOnlyDictionary<string, AuthorizationDecision>> EvaluateManyAsync(
        Guid orgId,
        Guid userId,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(orgId, nameof(orgId));
        EnsureIdentifier(userId, nameof(userId));
        var codes = permissionCodes.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var code in codes)
            EnsureCode(code, nameof(permissionCodes));

        var rules = await _store.ReadRulesAsync(orgId, userId, codes, cancellationToken);
        return codes.ToDictionary(code => code, code => Decide(rules, code), StringComparer.Ordinal);
    }

    public static AuthorizationDecision Decide(PermissionRuleSet rules, string code)
    {
        var reason = !rules.IsOrganizationMember ? AuthorizationDenyReason.NoOrg
            : rules.DenyCodes.Contains(code) ? AuthorizationDenyReason.ExplicitDeny
            : rules.GrantCodes.Contains(code) ? AuthorizationDenyReason.None
            : AuthorizationDenyReason.DefaultDeny;
        return new(reason == AuthorizationDenyReason.None, reason, code);
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
