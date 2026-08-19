namespace Task.Application.Security;

/// <summary>
/// Immutable server-side session record stored in iam.sessions. Idle and absolute expiry are
/// always evaluated against the database clock (clock_timestamp()).
/// </summary>
public sealed record SessionSnapshot(
    Guid SessionId,
    Guid OrganizationId,
    Guid UserAccountId,
    Guid? DeviceId,
    long CredentialVersion,
    long AuthorizationScopeVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokeReason);

/// <summary>
/// Immutable refresh token record stored in iam.refresh_tokens. TokenHash is the SHA-256 hex
/// digest of the raw token; format (32..256 lowercase hex characters) is guaranteed by the caller.
/// </summary>
public sealed record RefreshTokenRecord(
    Guid Id,
    Guid SessionId,
    string TokenHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    Guid? ReplacedById,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Lifecycle status of a refresh token relative to its owning session, evaluated against the
/// PostgreSQL server clock.
/// </summary>
public enum TokenStatus
{
    /// <summary>Token is unused, not revoked, not expired, and the session is still active.</summary>
    Active = 0,

    /// <summary>Token was already consumed by a prior rotation.</summary>
    Consumed = 1,

    /// <summary>Token was explicitly revoked.</summary>
    Revoked = 2,

    /// <summary>Token expiry has passed, or the owning session is no longer active.</summary>
    Expired = 3,
}

/// <summary>
/// Session identity resolved by refresh-token hash for refresh flows that carry no sessionId.
/// Returned whenever the token row exists so callers can detect reuse of consumed tokens.
/// </summary>
public sealed record SessionRefreshLookup(
    Guid OrganizationId,
    Guid SessionId,
    Guid UserAccountId,
    Guid? DeviceId,
    long CredentialVersion,
    long AuthorizationScopeVersion,
    TokenStatus TokenStatus);