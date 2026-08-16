using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Task.Api.Security;

internal static class TaskApiSecurityFoundation
{
    public const string FoundationAuthenticationScheme = "TaskFoundation";

    public static IServiceCollection AddTaskApiSecurityFoundation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(FoundationAuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TaskFoundationAuthenticationHandler>(
                FoundationAuthenticationScheme,
                _ => { });

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

    public bool IsUnconfigured =>
        string.IsNullOrWhiteSpace(Issuer) &&
        string.IsNullOrWhiteSpace(Audience) &&
        string.IsNullOrWhiteSpace(SigningKeyReference) &&
        string.IsNullOrWhiteSpace(PepperReference);
}

internal sealed class TaskIdentityFoundationOptionsValidator : IValidateOptions<TaskIdentityFoundationOptions>
{
    public ValidateOptionsResult Validate(string? name, TaskIdentityFoundationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Until the JWT/session adapter is introduced, an absent section is intentionally fail-closed
        // by the foundation authentication handler. A partially supplied section is always invalid.
        if (options.IsUnconfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        RequireNonEmpty(options.Issuer, "Task:Identity:Issuer", failures);
        RequireNonEmpty(options.Audience, "Task:Identity:Audience", failures);
        RequireExternalReference(options.SigningKeyReference, "Task:Identity:SigningKeyReference", failures);
        RequireExternalReference(options.PepperReference, "Task:Identity:PepperReference", failures);

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
}
