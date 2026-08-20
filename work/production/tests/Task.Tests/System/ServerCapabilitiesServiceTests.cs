using Task.Application.Server;

namespace Task.Tests.Server;

public sealed class ServerCapabilitiesServiceTests
{
    [Fact]
    public void GetCapabilities_WithDefaults_ReturnsExpectedValues()
    {
        var service = new ServerCapabilitiesService();

        var capabilities = service.GetCapabilities();

        Assert.Equal(new[] { "v1" }, capabilities.ApiVersions);
        Assert.Equal("1.0.0", capabilities.MinimumClientVersion);
        Assert.Equal("1.0.0", capabilities.RecommendedClientVersion);
        Assert.Equal(1, capabilities.SchemaVersion);
        Assert.Empty(capabilities.FeatureFlags);
    }

    [Fact]
    public void GetCapabilities_WithCustomValues_ReturnsConfiguredValues()
    {
        var apiVersions = new[] { "v1", "v2" };
        var featureFlags = new[] { "flag-a", "flag-b" };
        var service = new ServerCapabilitiesService(
            apiVersions: apiVersions,
            minimumClientVersion: "2.0.0",
            recommendedClientVersion: "2.1.0",
            schemaVersion: 3,
            featureFlags: featureFlags);

        var capabilities = service.GetCapabilities();

        Assert.Equal(apiVersions, capabilities.ApiVersions);
        Assert.Equal("2.0.0", capabilities.MinimumClientVersion);
        Assert.Equal("2.1.0", capabilities.RecommendedClientVersion);
        Assert.Equal(3, capabilities.SchemaVersion);
        Assert.Equal(featureFlags, capabilities.FeatureFlags);
    }

    [Fact]
    public void GetCapabilities_ReturnsNewInstanceEachTime()
    {
        var service = new ServerCapabilitiesService();

        var first = service.GetCapabilities();
        var second = service.GetCapabilities();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }
}

