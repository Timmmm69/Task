using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task.Application.Security;

namespace Task.Api.Security;

internal static class TaskApiSecurityFoundation
{
    public const string FoundationAuthenticationScheme = "TaskFoundation";

    public static IServiceCollection AddTaskApiSecurityFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(FoundationAuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TaskJwtAuthenticationHandler>(
                FoundationAuthenticationScheme,
                _ => { });

        // Transient registration so each request gets a fresh handler instance; the base
        // AuthenticationHandler<T> caches the AuthenticateResult internally, so a singleton
        // would reuse the first request's identity for all subsequent requests.
        services.AddTransient<TaskJwtAuthenticationHandler>(provider => new TaskJwtAuthenticationHandler(
            provider.GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<UrlEncoder>(),
            provider.GetRequiredService<AccessTokenValidator>(),
            provider.GetRequiredService<JwtVerificationKeys>(),
            provider.GetService<ISessionRepository>()));

        services.AddSingleton<JwtVerificationKeys>(provider =>
            JwtVerificationKeys.Load(
                provider.GetRequiredService<IOptions<TaskIdentityFoundationOptions>>().Value));
        services.AddSingleton<AccessTokenValidator>(provider =>
            new AccessTokenValidator(
                provider.GetRequiredService<JwtVerificationKeys>(),
                provider.GetRequiredService<IOptions<TaskIdentityFoundationOptions>>().Value));

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(FoundationAuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddSingleton<IValidateOptions<TaskIdentityFoundationOptions>, TaskIdentityFoundationOptionsValidator>();
        services.AddExceptionHandler<TaskApiExceptionHandler>();

        return services;
    }
}

internal sealed class TaskIdentityFoundationOptions
{
    public const string SectionName = "Task:Identity";

    public string? Issuer { get; init; }

    public string? Audience { get; init; }

    public string? SigningKeyReference { get; init; }

    public string? PepperReference { get; init; }

    public string? VerificationKeysDirectory { get; init; }

    public bool IsUnconfigured =>
        string.IsNullOrWhiteSpace(Issuer) &&
        string.IsNullOrWhiteSpace(Audience) &&
        string.IsNullOrWhiteSpace(SigningKeyReference) &&
        string.IsNullOrWhiteSpace(PepperReference) &&
        string.IsNullOrWhiteSpace(VerificationKeysDirectory);
}

internal sealed class TaskIdentityFoundationOptionsValidator : IValidateOptions<TaskIdentityFoundationOptions>
{
    public ValidateOptionsResult Validate(string? name, TaskIdentityFoundationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // An absent section is intentionally fail-closed by the JWT authentication handler
        // (empty verification keys). A partially supplied section is always invalid.
        if (options.IsUnconfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        RequireNonEmpty(options.Issuer, "Task:Identity:Issuer", failures);
        RequireNonEmpty(options.Audience, "Task:Identity:Audience", failures);
        RequireExternalReference(options.SigningKeyReference, "Task:Identity:SigningKeyReference", failures);
        RequireExternalReference(options.PepperReference, "Task:Identity:PepperReference", failures);
        RequireKeysDirectory(options.VerificationKeysDirectory, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireNonEmpty(string? value, string name, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{name} is required when Task:Identity is configured.");
        }
    }

    private static void RequireExternalReference(string? value, string name, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{name} is required when Task:Identity is configured.");
            return;
        }

        if (!value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{name} must be a file: reference, not a secret value.");
        }
    }

    private static void RequireKeysDirectory(string? value, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add("Task:Identity:VerificationKeysDirectory is required when Task:Identity is configured.");
            return;
        }

        if (!value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Task:Identity:VerificationKeysDirectory must be a file: reference, not a secret value.");
            return;
        }

        if (string.IsNullOrWhiteSpace(value.Substring("file:".Length)))
        {
            failures.Add("Task:Identity:VerificationKeysDirectory must reference a non-empty directory.");
        }
    }
}
