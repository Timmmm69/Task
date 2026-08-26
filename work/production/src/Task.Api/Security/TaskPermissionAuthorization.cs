using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Task.Application.Security;

namespace Task.Api.Security;

/// <summary>
/// ASP.NET authorization plumbing for permission codes (decision engine D3, increment #8).
/// A policy built with <see cref="RequirePermission"/> carries one
/// <see cref="PermissionRequirement"/> whose code is evaluated by
/// <see cref="PermissionDecisionService"/>. The engine owns all semantics: an explicit deny
/// outranks any grant (deny-wins), and a user with no or a mismatched organization is
/// denied (DENIED_NO_ORG). The handler fails closed: any absence of the server-derived
/// <see cref="AuthenticatedRequestContext"/> or any store failure denies the request, and
/// no exception ever escapes to the pipeline.
/// </summary>
internal static class TaskPermissionAuthorization
{
    /// <summary>Named policy: read the audit entry log.</summary>
    public const string AuditReadPolicyName = "permission.audit.read";

    /// <summary>Named policy: read login attempt records.</summary>
    public const string LoginAttemptsReadPolicyName = "permission.login-attempts.read";

    /// <summary>
    /// Named policy with the public Task.Read meaning. It is temporarily backed by
    /// task.manage and therefore grants no new capabilities to any role.
    /// </summary>
    public const string TaskReadPolicyName = "permission.task.read";

    /// <summary>Permission code backing both named policies.</summary>
    public const string AuditEntryReadPermissionCode = "audit.entry.read";

    /// <summary>
    /// Named policy with the public Task.Create meaning. It is temporarily backed by
    /// task.manage and therefore grants no new capabilities to any role.
    /// </summary>
    public const string TaskCreatePolicyName = "permission.task.create";

    /// <summary>Fail-closed backing permission for the temporary Task.Read bridge.</summary>
    public const string TaskReadBackingPermissionCode = "task.manage";

    /// <summary>Fail-closed backing permission for the temporary Task.Create bridge.</summary>
    public const string TaskCreateBackingPermissionCode = "task.manage";

    /// <summary>
    /// Requires the caller to hold the given permission code within the organization of the
    /// authenticated request. Denial is decided solely by the decision engine D3 (#8):
    /// deny-wins ordering and DENIED_NO_ORG both produce a failed authorization.
    /// </summary>
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Permission code is required.", nameof(code));
            }

            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// Evaluates <see cref="PermissionRequirement"/> against the server-derived
    /// <see cref="AuthenticatedRequestContext"/> that <see cref="TaskJwtAuthenticationHandler"/>
    /// stores in the request items. Fails closed, mirroring D3 (#8): a missing context
    /// (no authenticated request), DENIED_NO_ORG, an explicit or a default deny, or any
    /// store failure all deny the request. Exceptions are never propagated to the pipeline.
    /// </summary>
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly PermissionDecisionService _decisionService;

        public PermissionAuthorizationHandler(PermissionDecisionService decisionService)
        {
            ArgumentNullException.ThrowIfNull(decisionService);
            _decisionService = decisionService;
        }

        protected override async global::System.Threading.Tasks.Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            try
            {
                var httpContext = context.Resource as HttpContext;
                var requestContext =
                    httpContext?.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName]
                        as AuthenticatedRequestContext;
                if (requestContext is null)
                {
                    context.Fail();
                    return;
                }

                var decision = await _decisionService.EvaluateAsync(
                    requestContext.OrganizationId,
                    requestContext.UserAccountId,
                    requirement.Code,
                    httpContext!.RequestAborted);

                if (!decision.Allowed)
                {
                    context.Fail();
                    return;
                }

                context.Succeed(requirement);
            }
            catch (Exception)
            {
                context.Fail();
            }
        }
    }

    /// <summary>Adds a <see cref="PermissionRequirement"/> for the given code to the policy.</summary>
    public static AuthorizationPolicyBuilder RequirePermission(
        this AuthorizationPolicyBuilder builder,
        string code)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Requirements.Add(new PermissionRequirement(code));
        return builder;
    }

    /// <summary>
    /// Registers the permission authorization handler (Transient) and the named permission
    /// policies. The AddAuthorization actions compose with the foundation registration, so
    /// the fallback authentication policy stays in effect. The
    /// <see cref="PermissionDecisionService"/> registration itself belongs to the
    /// authorization DI increment (#30/#31).
    /// </summary>
    public static IServiceCollection AddTaskPermissionAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuditReadPolicyName, policy => policy.RequirePermission(AuditEntryReadPermissionCode));
            options.AddPolicy(LoginAttemptsReadPolicyName, policy => policy.RequirePermission(AuditEntryReadPermissionCode));
            options.AddPolicy(TaskReadPolicyName, policy => policy.RequirePermission(TaskReadBackingPermissionCode));
            options.AddPolicy(TaskCreatePolicyName, policy => policy.RequirePermission(TaskCreateBackingPermissionCode));
        });

        return services;
    }
}
