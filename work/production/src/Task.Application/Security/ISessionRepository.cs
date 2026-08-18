namespace Task.Application.Security;

/// <summary>
/// Persistence port for server-side sessions and refresh token rotation.
/// All time comparisons use the PostgreSQL server clock; no application timestamps are written.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Loads a non-revoked, non-expired session; returns null when absent, revoked or expired.
    /// </summary>
    SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId);

    /// <summary>
    /// Evaluates the authoritative request state for an access token's session against the
    /// current server state: session revocation and expiry, account status and the current
    /// credential and authorization-scope versions. All time comparisons use the PostgreSQL
    /// server clock (clock_timestamp()); absence by id or organization yields SessionExpired.
    /// </summary>
    SessionRequestState GetSessionRequestState(
        Guid organizationId,
        Guid sessionId,
        long expectedCredentialVersion,
        long expectedAuthorizationScopeVersion);

    /// <summary>
    /// Persists a session and its initial refresh token atomically in a single transaction.
    /// </summary>
    void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken);

    /// <summary>
    /// Atomically consumes the presented refresh token and inserts a replacement. Returns false
    /// (and changes nothing) when the token is unknown, already consumed or revoked.
    /// </summary>
    bool RotateRefreshToken(
        Guid organizationId,
        Guid sessionId,
        string consumedTokenHash,
        RefreshTokenRecord newRefreshToken);

    /// <summary>
    /// Refreshes last_seen_at for a still-active session. No-op when the session is absent,
    /// revoked or expired.
    /// </summary>
    void TouchSession(Guid organizationId, Guid sessionId);

    /// <summary>
    /// Revokes one session. Repeated calls are no-ops.
    /// </summary>
    void RevokeSession(Guid organizationId, Guid sessionId, string? reason);

    /// <summary>
    /// Revokes all non-revoked sessions of a user, optionally keeping one session alive.
    /// Returns the number of revoked sessions.
    /// </summary>
    int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason);
}
