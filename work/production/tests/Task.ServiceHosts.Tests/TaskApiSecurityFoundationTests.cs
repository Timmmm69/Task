using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Security;

namespace Task.ServiceHosts.Tests;

public sealed class TaskApiSecurityFoundationTests
{
    [Fact]
    public void Foundation_UsesAuthenticatedFallbackPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaskApiSecurityFoundation();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        var policy = Assert.IsType<AuthorizationPolicy>(options.FallbackPolicy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(TaskApiSecurityFoundation.FoundationAuthenticationScheme, policy.AuthenticationSchemes);
    }

    [Fact]
    public void IdentityOptions_AreSafeWhenAbsent()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            new TaskIdentityFoundationOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void IdentityOptions_RejectIncompleteOrInlineSecretConfiguration()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            new TaskIdentityFoundationOptions
            {
                Issuer = "https://task.example.internal",
                Audience = "task-desktop",
                SigningKeyReference = "not-a-secret-reference",
                PepperReference = "file:/run/secrets/task-pepper",
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("SigningKeyReference", StringComparison.Ordinal));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ProblemResponse_IsSanitizedAndCorrelated()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-test";
        context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId;

        await TaskApiProblemResponse.WriteAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "AUTHENTICATION_REQUIRED",
            "Authentication is required.",
            retryable: true);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("AUTHENTICATION_REQUIRED", root.GetProperty("code").GetString());
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
        Assert.Equal("trace-test", root.GetProperty("traceId").GetString());
        Assert.True(root.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("exception", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
