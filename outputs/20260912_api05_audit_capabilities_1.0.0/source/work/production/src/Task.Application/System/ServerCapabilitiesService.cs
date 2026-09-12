using System.Text.Json.Serialization;

namespace Task.Application.Server;

/// <summary>
/// Describes the capabilities exposed by the Task server to desktop clients, serialized with
/// the canonical Stage 2.2 <c>ServerCapabilities</c> shape
/// (<c>capabilities</c>, <c>minimumApiVersion</c>, <c>minimumDesktopVersion</c>). The payload is
/// intentionally free of operational detail: it never carries configuration values, connection
/// strings, secrets, paths or user data, so an authenticated client can discover server
/// capability and version floors without leaking anything beyond the public contract.
/// </summary>
public sealed record ServerCapabilities
{
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    [JsonPropertyName("minimumApiVersion")]
    public string MinimumApiVersion { get; init; } = "v1";

    [JsonPropertyName("minimumDesktopVersion")]
    public string MinimumDesktopVersion { get; init; } = "1.0.0";
}

/// <summary>
/// Stateless provider of <see cref="ServerCapabilities"/>. Values are supplied through the
/// constructor and exposed synchronously via <see cref="GetCapabilities"/>. The service also
/// carries the internal-only operational values the compatibility middleware and the
/// <c>system/version</c> endpoint need (recommended desktop version and database schema
/// version); those are exposed as plain properties, never as part of the public capabilities
/// payload. No configuration abstractions are used; callers wire constants or
/// deployment-specific values at registration.
/// </summary>
public sealed class ServerCapabilitiesService
{
    private const string DefaultMinimumApiVersion = "v1";
    private const string DefaultMinimumDesktopVersion = "1.0.0";

    private readonly IReadOnlyList<string> _capabilities;
    private readonly string _minimumApiVersion;
    private readonly string _minimumDesktopVersion;
    private readonly string _recommendedDesktopVersion;
    private readonly int _schemaVersion;

    public ServerCapabilitiesService(
        IReadOnlyList<string>? capabilities = null,
        string? minimumApiVersion = null,
        string? minimumDesktopVersion = null,
        string? recommendedDesktopVersion = null,
        int? schemaVersion = null)
    {
        _capabilities = capabilities ?? Array.Empty<string>();
        _minimumApiVersion = string.IsNullOrWhiteSpace(minimumApiVersion)
            ? DefaultMinimumApiVersion
            : minimumApiVersion;
        _minimumDesktopVersion = NormalizeReleaseVersion(
            minimumDesktopVersion ?? DefaultMinimumDesktopVersion, nameof(minimumDesktopVersion));
        _recommendedDesktopVersion = NormalizeReleaseVersion(
            recommendedDesktopVersion ?? _minimumDesktopVersion, nameof(recommendedDesktopVersion));
        if (ParseReleaseVersion(_recommendedDesktopVersion) < ParseReleaseVersion(_minimumDesktopVersion))
        {
            throw new ArgumentException("The recommended desktop version cannot be lower than the minimum desktop version.");
        }

        _schemaVersion = schemaVersion ?? 1;
    }

    /// <summary>Public canonical capabilities payload (no operational detail).</summary>
    public ServerCapabilities GetCapabilities() => new()
    {
        Capabilities = _capabilities,
        MinimumApiVersion = _minimumApiVersion,
        MinimumDesktopVersion = _minimumDesktopVersion,
    };

    public string MinimumApiVersion => _minimumApiVersion;

    public string MinimumDesktopVersion => _minimumDesktopVersion;

    public string RecommendedDesktopVersion => _recommendedDesktopVersion;

    public int SchemaVersion => _schemaVersion;

    public bool IsClientVersionSupported(string? clientVersion) =>
        TryParseReleaseVersion(clientVersion, out var parsed)
        && parsed >= ParseReleaseVersion(_minimumDesktopVersion);

    public static bool TryParseReleaseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 3
            || !parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit))
            || !Version.TryParse(value, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string NormalizeReleaseVersion(string value, string parameterName)
    {
        if (!TryParseReleaseVersion(value, out var version))
        {
            throw new ArgumentException("A three-part numeric release version is required.", parameterName);
        }

        return version.ToString(3);
    }

    private static Version ParseReleaseVersion(string value)
    {
        _ = TryParseReleaseVersion(value, out var version);
        return version;
    }
}
