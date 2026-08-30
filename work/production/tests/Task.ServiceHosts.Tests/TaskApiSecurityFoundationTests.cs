using System.Text.Json;
using System.Security.Cryptography;
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
    public void IdentityOptions_AreValidWhenFullyConfigured()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            FullyConfiguredOptions());

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
    public void IdentityOptions_RejectConfigurationWithoutVerificationKeysDirectory()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            new TaskIdentityFoundationOptions
            {
                Issuer = "https://task.example.internal",
                Audience = "task-desktop",
                SigningKeyReference = "file:/run/secrets/task-signing",
                PepperReference = "file:/run/secrets/task-pepper",
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("VerificationKeysDirectory", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityOptions_RejectInlineVerificationKeysDirectory()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            FullyConfiguredOptions(
                verificationKeysDirectory: "C:\\run\\keys"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("VerificationKeysDirectory", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityOptions_RejectEmptyVerificationKeysDirectory()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            FullyConfiguredOptions(
                verificationKeysDirectory: "file:"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("VerificationKeysDirectory", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityOptions_RejectEmptyStringVerificationKeysDirectory()
    {
        var result = new TaskIdentityFoundationOptionsValidator().Validate(
            Options.DefaultName,
            FullyConfiguredOptions(
                verificationKeysDirectory: ""));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("VerificationKeysDirectory", StringComparison.Ordinal));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task KeyMaterialStartupValidator_WithValidRingAndPepper_Succeeds()
    {
        var material = CreateKeyMaterial(new string('p', 32));
        try
        {
            var keys = JwtVerificationKeys.Load(material.Options);
            var validator = new TaskIdentityKeyMaterialStartupValidator(Options.Create(material.Options), keys);

            await validator.StartAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(material.Root, recursive: true);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task KeyMaterialStartupValidator_WithShortPepper_FailsClosed()
    {
        var material = CreateKeyMaterial("too-short");
        try
        {
            var keys = JwtVerificationKeys.Load(material.Options);
            var validator = new TaskIdentityKeyMaterialStartupValidator(Options.Create(material.Options), keys);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => validator.StartAsync(CancellationToken.None));

            Assert.Contains("at least 32", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("too-short", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(material.Root, recursive: true);
        }
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

    private static TaskIdentityFoundationOptions FullyConfiguredOptions(
        string? verificationKeysDirectory = "file:/run/secrets/task-keys") =>
        new()
        {
            Issuer = "https://task.example.internal",
            Audience = "task-desktop",
            SigningKeyReference = "file:/run/secrets/task-signing",
            PepperReference = "file:/run/secrets/task-pepper",
            VerificationKeysDirectory = verificationKeysDirectory,
        };

    private static (string Root, TaskIdentityFoundationOptions Options) CreateKeyMaterial(string pepper)
    {
        var root = Path.Combine(Path.GetTempPath(), $"task-identity-startup-{Guid.NewGuid():N}");
        var verificationDirectory = Path.Combine(root, "verification");
        Directory.CreateDirectory(verificationDirectory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPath = Path.Combine(root, "active");
        var pepperPath = Path.Combine(root, "pepper");
        File.WriteAllText(privateKeyPath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(verificationDirectory, "active.pem"), key.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(pepperPath, pepper);

        return (root, new TaskIdentityFoundationOptions
        {
            Issuer = "https://task.example.internal",
            Audience = "task-desktop",
            SigningKeyReference = $"file:{privateKeyPath}",
            PepperReference = $"file:{pepperPath}",
            VerificationKeysDirectory = $"file:{verificationDirectory}",
        });
    }
}
