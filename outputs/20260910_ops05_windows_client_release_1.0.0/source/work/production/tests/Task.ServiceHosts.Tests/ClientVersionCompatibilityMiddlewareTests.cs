using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Task.Api.Capabilities;
using Task.Application.Server;

namespace Task.ServiceHosts.Tests;

public sealed class ClientVersionCompatibilityMiddlewareTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task SupportedClient_ContinuesAndReceivesPolicyHeaders()
    {
        var continued = false;
        var middleware = new ClientVersionCompatibilityMiddleware(context =>
        {
            continued = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return global::System.Threading.Tasks.Task.CompletedTask;
        });
        var context = CreateContext("2.0.0");

        await middleware.InvokeAsync(context, CreateService());

        Assert.True(continued);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal("2.0.0", context.Response.Headers[ClientVersionCompatibilityMiddleware.MinimumVersionHeader]);
        Assert.Equal("2.1.0", context.Response.Headers[ClientVersionCompatibilityMiddleware.RecommendedVersionHeader]);
    }

    [Theory]
    [InlineData("1.9.9")]
    [InlineData("not-a-version")]
    public async global::System.Threading.Tasks.Task UnsupportedClient_ReturnsStable426Problem(string version)
    {
        var middleware = new ClientVersionCompatibilityMiddleware(_ =>
            throw new InvalidOperationException("The blocked request continued."));
        var context = CreateContext(version);

        await middleware.InvokeAsync(context, CreateService());

        Assert.Equal(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("CLIENT_VERSION_UNSUPPORTED", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NonDesktopRequestWithoutHeader_RemainsCompatible()
    {
        var continued = false;
        var middleware = new ClientVersionCompatibilityMiddleware(_ =>
        {
            continued = true;
            return global::System.Threading.Tasks.Task.CompletedTask;
        });
        var context = CreateContext(null);

        await middleware.InvokeAsync(context, CreateService());

        Assert.True(continued);
    }

    private static DefaultHttpContext CreateContext(string? version)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "ops05-test";
        if (version is not null)
        {
            context.Request.Headers[ClientVersionCompatibilityMiddleware.ClientVersionHeader] = version;
        }

        return context;
    }

    private static ServerCapabilitiesService CreateService() =>
        new(minimumClientVersion: "2.0.0", recommendedClientVersion: "2.1.0");
}
