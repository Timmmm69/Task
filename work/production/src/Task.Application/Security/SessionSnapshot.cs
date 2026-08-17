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
