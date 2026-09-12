using Task.Application.Server;

namespace Task.Tests.Server;

public sealed class ServerCapabilitiesServiceTests
{
    [Fact]
    public void GetCapabilities_WithDefaults_ReturnsExpectedValues()
    {
        var service = new ServerCapabilitiesService();

        var capabilities = service.GetCapabilities();

        Assert.Empty(capabilities.Capabilities);
        Assert.Equal("v1", capabilities.MinimumApiVersion);
        Assert.Equal("1.0.0", capabilities.MinimumDesktopVersion);
        Assert.Equal("1.0.0", service.MinimumDesktopVersion);
        Assert.Equal("1.0.0", service.RecommendedDesktopVersion);
        Assert.Equal(1, service.SchemaVersion);
    }

    [Fact]
    public void GetCapabilities_WithCustomValues_ReturnsConfiguredValues()
    {
        var flags = new[] { "flag-a", "flag-b" };
        var service = new ServerCapabilitiesService(
            capabilities: flags,
            minimumApiVersion: "v2",
            minimumDesktopVersion: "2.0.0",
            recommendedDesktopVersion: "2.1.0",
            schemaVersion: 3);

        var capabilities = service.GetCapabilities();

        Assert.Equal(flags, capabilities.Capabilities);
        Assert.Equal("v2", capabilities.MinimumApiVersion);
        Assert.Equal("2.0.0", capabilities.MinimumDesktopVersion);
        Assert.Equal("2.1.0", service.RecommendedDesktopVersion);
        Assert.Equal(3, service.SchemaVersion);
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

    [Fact]
    public void GetCapabilities_PayloadExposesOnlyCanonicalFields()
    {
        var service = new ServerCapabilitiesService(
            capabilities: ["projects"],
            minimumDesktopVersion: "2.0.0",
            recommendedDesktopVersion: "2.1.0",
            schemaVersion: 5);

        var capabilities = service.GetCapabilities();

        Assert.Equal(["projects"], capabilities.Capabilities);
        Assert.Equal("v1", capabilities.MinimumApiVersion);
        Assert.Equal("2.0.0", capabilities.MinimumDesktopVersion);
    }

    [Theory]
    [InlineData("1.9.9", false)]
    [InlineData("2.0.0", true)]
    [InlineData("2.1.0", true)]
    [InlineData("2.0", false)]
    [InlineData("2.0.0-preview", false)]
    public void IsClientVersionSupported_UsesThreePartReleaseFloor(string version, bool expected)
    {
        var service = new ServerCapabilitiesService(
            minimumDesktopVersion: "2.0.0",
            recommendedDesktopVersion: "2.1.0");

        Assert.Equal(expected, service.IsClientVersionSupported(version));
    }

    [Fact]
    public void Constructor_RejectsInvertedCompatibilityWindow()
    {
        Assert.Throws<ArgumentException>(() => new ServerCapabilitiesService(
            minimumDesktopVersion: "2.0.0",
            recommendedDesktopVersion: "1.9.9"));
    }
}
