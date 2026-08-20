using System.Security.Cryptography;
using System.Text;
using Task.Application.Audit;

namespace Task.Application.Security;

/// <summary>
/// Outcome of a refresh-token rotation attempt. The caller is not told why a session failed
/// beyond the coarse categories below; collapsing them into one HTTP response is the
/// responsibility of the API layer.
/// </summary>
public abstract record RefreshOutcome
{
    /// <summary>A new refresh token was issued; the presented token was consumed.</summary>
    public sealed record Succeeded(
        Guid SessionId,
        Guid UserId,
        Guid OrganizationId,
        long CredentialVersion,
        long AuthorizationScopeVersion,
        string NewRefreshToken,
        DateTimeOffset RefreshExpiresAtUtc) : RefreshOutcome;

    /// <summary>
    /// No token row exists for the presented hash, the token expired, the owning session is no
    /// longer active, or the owning device disappeared. Opaque by design: the caller must not
    /// infer the exact reason.
    /// </summary>
    public sealed record SessionExpired : RefreshOutcome;

    /// <summary>The token was explicitly revoked.</summary>
    public sealed record SessionRevoked : RefreshOutcome;

    /// <summary>A previously issued token was presented again; the session was revoked.</summary>
    public sealed record ReuseDetected : RefreshOutcome;

    /// <summary>The session is bound to a revoked device.</summary>
    public sealed record DeviceRevoked : RefreshOutcome;
}

/// <summary>
/// Command carrying the client-supplied inputs of a refresh attempt. CorrelationId and RequestId
/// are propagated verbatim into the append-only audit journal.
/// </summary>
public sealed record RefreshCommand(
    string RefreshToken,
    string DeviceKey,
    Guid CorrelationId,
    Guid RequestId);

/// <summary>
/// Orchestrates refresh-token rotation for requests that carry no sessionId: resolves the session
/// by the SHA-256 hash of the presented token, enforces token/device state, rotates the token
/// through <see cref="RefreshTokenRotationService"/> and writes best-effort audit entries. The
/// service never issues access tokens and never touches last_seen_at: rotation consumes the
/// presented token and issues a replacement atomically, and idleness is a separate concern.
/// </summary>
public sealed class RefreshService
{
    private const string RefreshTokenReuseAuditAction = "RefreshTokenReuse";
    private const string SessionRefreshedAuditAction = "SessionRefreshed";
    private const string RefreshTokenReuseReasonCode = "REFRESH_TOKEN_REUSE";
    private const string ReuseRevokeReason = "refresh-token-reuse";
    private const string SuccessOutcome = "success";
    private const string FailedOutcome = "failed";
    private const string StandardRedactionLevel = "standard";

    private readonly ISessionRepository _sessionRepository;
    private readonly RefreshTokenRotationService _rotationService;
    private readonly IDeviceRegistrationStore _deviceStore;
    private readonly IAuditEntryStore _auditStore;

    public RefreshService(
        ISessionRepository sessionRepository,
        RefreshTokenRotationService rotationService,
        IDeviceRegistrationStore deviceStore,
        IAuditEntryStore auditStore)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _deviceStore = deviceStore ?? throw new ArgumentNullException(nameof(deviceStore));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
    }

    /// <summary>
    /// Attempts to rotate the refresh token of the session owning the presented token. Checks
    /// run in a fixed order: token lookup, token status, bound device, then atomic rotation.
    /// The flow is fail-closed: any ambiguity collapses into <see cref="RefreshOutcome.SessionExpired"/>.
    /// </summary>
    public async global::System.Threading.Tasks.Task<RefreshOutcome> RefreshAsync(
        RefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RefreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DeviceKey);
        EnsureIdentifier(command.CorrelationId, nameof(command.CorrelationId));
        EnsureIdentifier(command.RequestId, nameof(command.RequestId));

        var tokenHash = ComputeHash(command.RefreshToken);
        var lookup = _sessionRepository.FindSessionByRefreshTokenHash(tokenHash);
        if (lookup is null)
        {
            return new RefreshOutcome.SessionExpired();
        }

        var organizationId = lookup.OrganizationId;
        var sessionId = lookup.SessionId;

        switch (lookup.TokenStatus)
        {
            case TokenStatus.Expired:
                return new RefreshOutcome.SessionExpired();
            case TokenStatus.Revoked:
                return new RefreshOutcome.SessionRevoked();
            case TokenStatus.Consumed:
                _sessionRepository.RevokeSession(organizationId, sessionId, ReuseRevokeReason);
                await AppendAuditBestEffortAsync(
                    lookup, command, RefreshTokenReuseAuditAction, FailedOutcome, RefreshTokenReuseReasonCode, cancellationToken);
                return new RefreshOutcome.ReuseDetected();
            case TokenStatus.Active:
                break;
            default:
                throw new InvalidOperationException($"Unexpected token status {lookup.TokenStatus}.");
        }

        if (lookup.DeviceId is Guid deviceId)
        {
            var device = await _deviceStore.GetByIdAsync(organizationId, deviceId, cancellationToken);
            if (device is null)
            {
                return new RefreshOutcome.SessionExpired();
            }

            if (device.RevokedAtUtc is not null)
            {
                return new RefreshOutcome.DeviceRevoked();
            }
        }

        var newExpiryUtc = DateTimeOffset.UtcNow + RefreshTokenRotationService.DefaultRefreshTokenLifetime;
        var rotation = await _rotationService.RotateAsync(
            organizationId, sessionId, command.RefreshToken, newExpiryUtc, cancellationToken);

        RefreshOutcome outcome = rotation switch
        {
            RotationOutcome.Rotated rotated => new RefreshOutcome.Succeeded(
                sessionId,
                lookup.UserAccountId,
                organizationId,
                lookup.CredentialVersion,
                lookup.AuthorizationScopeVersion,
                rotated.NewRefreshToken,
                rotated.NewExpiryUtc),
            RotationOutcome.UnknownToken => new RefreshOutcome.SessionExpired(),
            RotationOutcome.ReuseDetected => new RefreshOutcome.ReuseDetected(),
            _ => throw new InvalidOperationException($"Unexpected rotation outcome {rotation}."),
        };

        if (outcome is RefreshOutcome.Succeeded)
        {
            await AppendAuditBestEffortAsync(
                lookup, command, SessionRefreshedAuditAction, SuccessOutcome, reasonCode: null, cancellationToken);
        }

        return outcome;
    }

    /// <summary>
    /// Appends one audit entry, best-effort like the login flow (#21): a failing or unavailable
    /// journal must never break the refresh flow, so non-cancellation exceptions are swallowed.
    /// Cancellation is still propagated, and the entry carries no tokens or secrets.
    /// </summary>
    private async global::System.Threading.Tasks.Task AppendAuditBestEffortAsync(
        SessionRefreshLookup lookup,
        RefreshCommand command,
        string actionCode,
        string outcome,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditStore.AppendAsync(
                new AuditEntryRecord(
                    Guid.NewGuid(),
                    lookup.OrganizationId,
                    DateTimeOffset.UtcNow,
                    lookup.UserAccountId,
                    lookup.SessionId,
                    actionCode,
                    outcome,
                    reasonCode,
                    command.CorrelationId,
                    command.RequestId,
                    AuditEntryRecord.DefaultMetadata,
                    null,
                    null,
                    StandardRedactionLevel),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Audit is best-effort; the refresh must not fail because of the journal.
        }
    }

    private static string ComputeHash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}