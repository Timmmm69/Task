namespace Task.Application.Security;

/// <summary>
/// Authoritative request state for an access token's session, evaluated by the persistence
/// port against the PostgreSQL server clock. Every access-token request must pass through
/// this evaluation before any identity is established.
/// </summary>
public enum SessionRequestState
{
    /// <summary>Session exists, is not revoked or expired, the account is active and the
    /// credential and authorization-scope versions match the claims.</summary>
    Active,

    /// <summary>The session was revoked and must not be accepted.</summary>
    SessionRevoked,

    /// <summary>The session id is absent (no row or another organization's row) or the
    /// session expiry has passed.</summary>
    SessionExpired,

    /// <summary>The owning user account is not in the 'active' state.</summary>
    AccountBlocked,

    /// <summary>The current credential version or authorization-scope version no longer
    /// matches the version claims in the token.</summary>
    VersionMismatch,
}