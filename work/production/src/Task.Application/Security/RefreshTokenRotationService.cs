using System.Security.Cryptography;
using System.Text;

namespace Task.Application.Security;

/// <summary>
/// Describes a freshly generated refresh token: the opaque raw value presented to the caller and
/// its SHA-256 hex hash stored in the database. The raw value is generated only once and is never
/// persisted or logged.
/// </summary>
public sealed record RefreshTokenDescriptor(string RawToken, string TokenHash);

/// <summary>
/// Outcome of a refresh token rotation attempt. The caller is intentionally not told whether a
/// failure was caused by an unknown token or a session that is no longer active.
/// </summary>
public abstract record RotationOutcome
{
    /// <summary>The rotation succeeded; a new single-use refresh token was issued.</summary>
    public sealed record Rotated(string NewRefreshToken, DateTimeOffset NewExpiryUtc) : RotationOutcome;

    /// <summary>
    /// The presented token could not be accepted. From the caller's perspective this is opaque:
    /// it covers an unknown token, an inactive session or any other invalid-token state.
    /// </summary>
    public sealed record UnknownToken : RotationOutcome;

    /// <summary>
    /// A previously issued refresh token was presented again. The whole token family is revoked.
    /// </summary>
    public sealed record ReuseDetected : RotationOutcome;
}

/// <summary>
/// Issues opaque refresh tokens and rotates them atomically through <see cref="ISessionRepository"/>.
/// Tokens are one-time use: each successful rotation consumes the presented token and issues a new
/// one. If rotation fails while the session is still active, the attempt is treated as reuse and
/// the whole session is revoked. The service is fail-closed: it uses a CSPRNG, SHA-256 hashing and
/// never logs raw tokens or hashes.
/// </summary>
public sealed class RefreshTokenRotationService
{
    /// <summary>Default lifetime of a newly issued refresh token when the caller does not override it.</summary>
    public static readonly TimeSpan DefaultRefreshTokenLifetime = TimeSpan.FromDays(30);

    private const string ReuseRevokeReason = "refresh-token-reuse";
    private const int TokenByteLength = 32;

    private readonly ISessionRepository _sessionRepository;

    /// <summary>
    /// Creates the rotation service backed by the supplied session repository.
    /// </summary>
    public RefreshTokenRotationService(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    /// <summary>
    /// Generates a new cryptographically random refresh token. The returned descriptor contains the
    /// raw base64url token (43 characters, no padding) and its lowercase SHA-256 hex hash.
    /// </summary>
    public RefreshTokenDescriptor GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var raw = ToBase64Url(bytes);
        var hash = ComputeHash(raw);
        return new RefreshTokenDescriptor(raw, hash);
    }

    /// <summary>
    /// Rotates the refresh token of an active session.
    /// </summary>
    /// <param name="organizationId">Organization that owns the session.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="rawToken">Raw refresh token presented by the caller.</param>
    /// <param name="newExpiryUtc">UTC expiry instant for the newly issued refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="RotationOutcome.Rotated"/> on success, <see cref="RotationOutcome.UnknownToken"/>
    /// when the token or session is not valid, or <see cref="RotationOutcome.ReuseDetected"/> when
    /// a previously consumed token is reused.
    /// </returns>
    /// <exception cref="ArgumentException">An identifier is empty or the raw token is null/whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="newExpiryUtc"/> is not in the future.</exception>
    /// <remarks>
    /// Because the repository distinguishes token states only after the initial rotation attempt,
    /// a single call may collapse several invalid-token conditions into
    /// <see cref="RotationOutcome.UnknownToken"/>. The caller must not infer the exact reason from
    /// this outcome.
    /// </remarks>
    public global::System.Threading.Tasks.Task<RotationOutcome> RotateAsync(
        Guid organizationId,
        Guid sessionId,
        string rawToken,
        DateTimeOffset newExpiryUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        if (newExpiryUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newExpiryUtc),
                "New refresh token expiry must be in the future.");
        }

        var presentedHash = ComputeHash(rawToken);
        var newDescriptor = GenerateToken();
        var newRefreshToken = new RefreshTokenRecord(
            Guid.NewGuid(),
            sessionId,
            newDescriptor.TokenHash,
            DateTimeOffset.UtcNow,
            newExpiryUtc,
            null,
            null,
            null);

        if (_sessionRepository.RotateRefreshToken(organizationId, sessionId, presentedHash, newRefreshToken))
        {
            return global::System.Threading.Tasks.Task.FromResult<RotationOutcome>(
                new RotationOutcome.Rotated(newDescriptor.RawToken, newExpiryUtc));
        }

        if (_sessionRepository.GetActiveSession(organizationId, sessionId) is null)
        {
            return global::System.Threading.Tasks.Task.FromResult<RotationOutcome>(
                new RotationOutcome.UnknownToken());
        }

        _sessionRepository.RevokeSession(organizationId, sessionId, ReuseRevokeReason);
        return global::System.Threading.Tasks.Task.FromResult<RotationOutcome>(
            new RotationOutcome.ReuseDetected());
    }

    private static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
