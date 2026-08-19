namespace Task.Application.Security;

/// <summary>
/// Device registration snapshot used by refresh-token device checks.
/// </summary>
public sealed record DeviceRegistrationRecord(
    Guid DeviceId,
    Guid UserId,
    string FingerprintHash,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Persistence port for desktop device registration (iam.devices).
/// Callers hash the client deviceKey (SHA-256 hex); the store accepts the hash only.
/// </summary>
public interface IDeviceRegistrationStore
{
    /// <summary>
    /// Upserts a device for the user+fingerprint pair within the organization.
    /// Existing pair: updates last_seen_at and display_name when displayName is not null;
    /// does not clear revoked_at. New pair: inserts core.objects + iam.devices and returns the new id.
    /// </summary>
    global::System.Threading.Tasks.Task<Guid> UpsertAsync(
        Guid organizationId,
        Guid userId,
        string fingerprintHash,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a device by organization and device id, or null when missing.
    /// </summary>
    global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(
        Guid organizationId,
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
