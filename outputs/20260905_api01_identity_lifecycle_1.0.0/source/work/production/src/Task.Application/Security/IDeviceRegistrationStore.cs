namespace Task.Application.Security;

/// <summary>
/// Device registration snapshot used by refresh-token device checks.
/// </summary>
public sealed record DeviceRegistrationRecord(
    Guid DeviceId,
    Guid UserId,
    string FingerprintHash,
    DateTimeOffset? RevokedAtUtc);

public sealed record DeviceReadProjection(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string DeviceName,
    string Platform,
    string AppVersion,
    string? OsVersion,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record DeviceReadPage(IReadOnlyList<DeviceReadProjection> Items, Guid? NextCursor, long Total);

public sealed record DevicePatchCommand(string? DeviceName, string? Platform, string? AppVersion, bool NameSpecified, bool PlatformSpecified, bool AppVersionSpecified);

public sealed record DeviceCommandResult(IdentityCommandDisposition Disposition, DeviceReadProjection? Device = null, int? RetryAfterSeconds = null);

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

    global::System.Threading.Tasks.Task UpdateMetadataAsync(
        Guid organizationId, Guid deviceId, string platform, string appVersion, string? osVersion,
        CancellationToken cancellationToken = default) => global::System.Threading.Tasks.Task.CompletedTask;

    global::System.Threading.Tasks.Task<DeviceReadProjection?> GetReadModelAsync(
        Guid organizationId, Guid deviceId, CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult<DeviceReadProjection?>(null);

    global::System.Threading.Tasks.Task<DeviceReadPage> GetPageAsync(
        Guid organizationId, Guid requestingUserId, bool includeAll, string? filter, int page, Guid? cursor,
        CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult(new DeviceReadPage([], null, 0));

    global::System.Threading.Tasks.Task<DeviceCommandResult> PatchAsync(
        IdentityCommandContext context, Guid deviceId, long expectedVersion, DevicePatchCommand patch,
        CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult(new DeviceCommandResult(IdentityCommandDisposition.NotFound));

    global::System.Threading.Tasks.Task<bool> HeartbeatAsync(
        Guid organizationId, Guid userId, Guid deviceId, string appVersion, string? osVersion,
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult(false);

    global::System.Threading.Tasks.Task<DeviceCommandResult> RevokeAsync(
        IdentityCommandContext context, Guid deviceId, long expectedVersion, string reason,
        CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult(new DeviceCommandResult(IdentityCommandDisposition.NotFound));
}
