using System.Text.Json.Serialization;

namespace Task.Application.Server;

/// <summary>
/// Describes the capabilities exposed by the Task server to desktop clients.
/// All values are immutable and serialized with camelCase JSON names.
/// </summary>
public sealed record ServerCapabilities
{
    private static readonly IReadOnlyList<string> DefaultApiVersions = new[] { "v1" };

    [JsonPropertyName("apiVersions")]
    public IReadOnlyList<string> ApiVersions { get; init; } = DefaultApiVersions;

    [JsonPropertyName("minimumClientVersion")]
    public string MinimumClientVersion { get; init; } = "1.0.0";

    [JsonPropertyName("recommendedClientVersion")]
    public string RecommendedClientVersion { get; init; } = "1.0.0";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("featureFlags")]
    public IReadOnlyList<string> FeatureFlags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Stateless provider of <see cref="ServerCapabilities"/>. Values are supplied through the
/// constructor and exposed synchronously via <see cref="GetCapabilities"/>. No configuration
/// abstractions are used; callers wire constants or deployment-specific values at registration.
/// </summary>
public sealed class ServerCapabilitiesService
{
    private static readonly IReadOnlyList<string> DefaultApiVersions = new[] { "v1" };

    private readonly IReadOnlyList<string> _apiVersions;
    private readonly string _minimumClientVersion;
    private readonly string _recommendedClientVersion;
    private readonly int _schemaVersion;
    private readonly IReadOnlyList<string> _featureFlags;

    public ServerCapabilitiesService(
        IReadOnlyList<string>? apiVersions = null,
        string? minimumClientVersion = null,
        string? recommendedClientVersion = null,
        int? schemaVersion = null,
        IReadOnlyList<string>? featureFlags = null)
    {
        _apiVersions = apiVersions ?? DefaultApiVersions;
        _minimumClientVersion = NormalizeReleaseVersion(minimumClientVersion ?? "1.0.0", nameof(minimumClientVersion));
        _recommendedClientVersion = NormalizeReleaseVersion(recommendedClientVersion ?? "1.0.0", nameof(recommendedClientVersion));
        if (ParseReleaseVersion(_recommendedClientVersion) < ParseReleaseVersion(_minimumClientVersion))
        {
            throw new ArgumentException("The recommended client version cannot be lower than the minimum client version.");
        }
        _schemaVersion = schemaVersion ?? 1;
        _featureFlags = featureFlags ?? Array.Empty<string>();
    }

    public ServerCapabilities GetCapabilities() => new()
    {
        ApiVersions = _apiVersions,
        MinimumClientVersion = _minimumClientVersion,
        RecommendedClientVersion = _recommendedClientVersion,
        SchemaVersion = _schemaVersion,
        FeatureFlags = _featureFlags,
    };

    public bool IsClientVersionSupported(string? clientVersion) =>
        TryParseReleaseVersion(clientVersion, out var parsed)
        && parsed >= ParseReleaseVersion(_minimumClientVersion);

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
