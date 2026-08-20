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
        _minimumClientVersion = minimumClientVersion ?? "1.0.0";
        _recommendedClientVersion = recommendedClientVersion ?? "1.0.0";
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
}
