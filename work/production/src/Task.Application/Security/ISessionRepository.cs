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

    /// <summary>
    /// Hard-deletes all non-revoked sessions of a user together with their refresh tokens,
    /// optionally keeping one session alive. Returns the number of deleted sessions; the kept
    /// session remains untouched. Used by credential rotation so that a password change
    /// invalidates the user's other sessions immediately.
    /// </summary>
    global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
        Guid organizationId,
        Guid userId,
        Guid? exceptSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes up to maxCount expired refresh tokens whose expires_at is older than the
    /// cutoff, oldest first. Consumed and revoked tokens are purged once expired. Returns the
    /// actual number of deleted tokens.
    /// </summary>
    global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
        DateTimeOffset olderThanUtc,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes up to maxCount expired sessions whose absolute_expires_at is older than the
    /// cutoff, oldest first. Sessions still referenced by append-only audit entries
    /// (governance.audit_entries.actor_session_id) are skipped; their removal is decided by the
    /// separate audit retention policy. Callers must purge refresh tokens before sessions
    /// because iam.refresh_tokens references iam.sessions with ON DELETE RESTRICT. Returns the
    /// actual number of deleted sessions.
    /// </summary>
    global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
        DateTimeOffset olderThanUtc,
        int maxCount,
        CancellationToken cancellationToken = default);
}
