using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Task.Api.Security;

namespace Task.ServiceHosts.Tests;

public sealed class TaskFoundationAuthenticationHandlerTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithoutCredentials_ReturnsNoResult()
    {
        var (context, handler) = await CreateHandlerAsync();

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Authenticate_WithClientIdentityHeaders_StillReturnsNoResult()
    {
        var (context, handler) = await CreateHandlerAsync();
        context.Request.Headers["X-User-ID"] = "user-123";
        context.Request.Headers["X-Organization-ID"] = "org-42";
        context.Request.Headers["X-Role"] = "admin";

        var result = await handler.AuthenticateAsync();

        Assert.Equal("user-123", context.Request.Headers["X-User-ID"].ToString());
        Assert.True(result.None);
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Challenge_WritesCorrelated401ProblemResponse()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (context, handler) = await CreateHandlerAsync();
        context.TraceIdentifier = "trace-test";
        context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId;

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("AUTHENTICATION_REQUIRED", root.GetProperty("code").GetString());
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        Assert.Equal("trace-test", root.GetProperty("traceId").GetString());
        Assert.True(root.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Forbid_Writes403ProblemResponse()
    {
        var (context, handler) = await CreateHandlerAsync();

        await handler.ForbidAsync(new AuthenticationProperties());

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("FORBIDDEN", root.GetProperty("code").GetString());
        Assert.False(root.GetProperty("retryable").GetBoolean());
    }

    private static async global::System.Threading.Tasks.Task<(DefaultHttpContext Context, IAuthenticationHandler Handler)> CreateHandlerAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(UrlEncoder.Default);
        services.AddTaskApiSecurityFoundation();

        using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext();
        context.RequestServices = provider;
        context.Response.Body = new MemoryStream();

        var handlerProvider = provider.GetRequiredService<IAuthenticationHandlerProvider>();
        var handler = await handlerProvider.GetHandlerAsync(
            context,
            TaskApiSecurityFoundation.FoundationAuthenticationScheme);

        Assert.NotNull(handler);
        return (context, handler);
    }
}