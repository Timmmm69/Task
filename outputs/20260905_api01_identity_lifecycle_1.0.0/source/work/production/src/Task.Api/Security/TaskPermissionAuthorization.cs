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
    public const string SessionReadPolicyName = "permission.session.read";
    public const string SessionRevokePolicyName = "permission.session.revoke";
    public const string UserReadPolicyName = "permission.user.read";
    public const string UserCreatePolicyName = "permission.user.create";
    public const string UserUpdatePolicyName = "permission.user.update";
    public const string UserBlockPolicyName = "permission.user.block";
    public const string UserResetPasswordPolicyName = "permission.user.reset-password";
    public const string DeviceReadPolicyName = "permission.device.read";
    public const string DeviceUpdatePolicyName = "permission.device.update";
    public const string DeviceRevokePolicyName = "permission.device.revoke";

    /// <summary>
    /// Named policy with the public Task.Read meaning.
    /// </summary>
    public const string TaskReadPolicyName = "permission.task.read";

    /// <summary>Named policy with the public Calendar.Read meaning.</summary>
    public const string CalendarReadPolicyName = "permission.calendar.read";

    public const string CalendarEventCreatePolicyName = "permission.calendar-event.create";

    public const string CalendarEventUpdatePolicyName = "permission.calendar-event.update";

    public const string CalendarEventDeletePolicyName = "permission.calendar-event.delete";
    public const string RecurrenceReadPolicyName = "permission.recurrence.read";
    public const string RecurrenceManagePolicyName = "permission.recurrence.manage";

    /// <summary>Permission code backing both named policies.</summary>
    public const string AuditEntryReadPermissionCode = "audit.entry.read";

    /// <summary>
    /// Named policy with the public Task.Create meaning.
    /// </summary>
    public const string TaskCreatePolicyName = "permission.task.create";

    /// <summary>
    /// Named policy with the public Task.Update meaning.
    /// </summary>
    public const string TaskUpdatePolicyName = "permission.task.update";

    /// <summary>Named Task.ChangeStatus policy.</summary>
    public const string TaskChangeStatusPolicyName = "permission.task.change-status";

    public const string TaskReadBackingPermissionCode = "task.read";
    public const string UserReadBackingPermissionCode = "user.read";
    public const string UserCreateBackingPermissionCode = "user.create";
    public const string UserUpdateBackingPermissionCode = "user.update";
    public const string UserBlockBackingPermissionCode = "user.block";
    public const string UserResetPasswordBackingPermissionCode = "user.resetpassword";
    public const string DeviceReadBackingPermissionCode = "device.readownorall";
    public const string DeviceUpdateBackingPermissionCode = "device.updateownorall";
    public const string DeviceRevokeBackingPermissionCode = "device.revoke";

    public const string TaskCreateBackingPermissionCode = "task.create";

    public const string TaskUpdateBackingPermissionCode = "task.update";

    public const string TaskChangeStatusBackingPermissionCode = "task.changestatus";

    public const string CalendarEventCreateBackingPermissionCode = "calendarevent.create";

    public const string CalendarEventUpdateBackingPermissionCode = "calendarevent.update";

    public const string CalendarEventDeleteBackingPermissionCode = "calendarevent.delete";

    public static IReadOnlyList<(string PermissionCode, string Capability)> TaskCapabilities { get; } =
    [
        ("audit.entry.read", "SecurityAudit.Read"),
        ("session.readownorall", "Session.ReadOwnOrAll"),
        ("session.revokeownorall", "Session.RevokeOwnOrAll"),
        (UserReadBackingPermissionCode, "User.Read"),
        (UserCreateBackingPermissionCode, "User.Create"),
        (UserUpdateBackingPermissionCode, "User.Update"),
        (UserBlockBackingPermissionCode, "User.Block"),
        (UserResetPasswordBackingPermissionCode, "User.ResetPassword"),
        (DeviceReadBackingPermissionCode, "Device.ReadOwnOrAll"),
        (DeviceUpdateBackingPermissionCode, "Device.UpdateOwnOrAll"),
        (DeviceRevokeBackingPermissionCode, "Device.Revoke"),
        (TaskReadBackingPermissionCode, "Task.Read"),
        (TaskReadBackingPermissionCode, "Calendar.Read"),
        (TaskCreateBackingPermissionCode, "Task.Create"),
        (TaskUpdateBackingPermissionCode, "Task.Update"),
        ("task.assign", "Task.Assign"), ("task.watch", "Task.Watch"),
        (TaskChangeStatusBackingPermissionCode, "Task.ChangeStatus"),
        (CalendarEventCreateBackingPermissionCode, "CalendarEvent.Create"),
        (CalendarEventUpdateBackingPermissionCode, "CalendarEvent.Update"),
        (CalendarEventDeleteBackingPermissionCode, "CalendarEvent.Delete"),
        ("recurrence.read", "Recurrence.Read"),
        ("recurrence.manage", "Recurrence.Manage"),
    .. Task.Application.ProductData.ProductApiRoutes.All.Select(route =>
        (route.Permission.ToLowerInvariant(), route.Permission)).Distinct().Where(pair => pair.Item2 is not ("Task.Read" or "Task.Create" or "Task.Update" or "Task.ChangeStatus")),
    ];

    public static string GetProductRoutePolicyName(string permissionCode) =>
        "permission." + NormalizeProductRoutePermissionCode(permissionCode);

    private static string NormalizeProductRoutePermissionCode(string permissionCode) =>
        PermissionCode.Parse(permissionCode).Value.ToLowerInvariant();

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
            options.AddPolicy(SessionReadPolicyName, policy => policy.RequirePermission("session.readownorall"));
            options.AddPolicy(SessionRevokePolicyName, policy => policy.RequirePermission("session.revokeownorall"));
            options.AddPolicy(UserReadPolicyName, policy => policy.RequirePermission(UserReadBackingPermissionCode));
            options.AddPolicy(UserCreatePolicyName, policy => policy.RequirePermission(UserCreateBackingPermissionCode));
            options.AddPolicy(UserUpdatePolicyName, policy => policy.RequirePermission(UserUpdateBackingPermissionCode));
            options.AddPolicy(UserBlockPolicyName, policy => policy.RequirePermission(UserBlockBackingPermissionCode));
            options.AddPolicy(UserResetPasswordPolicyName, policy => policy.RequirePermission(UserResetPasswordBackingPermissionCode));
            options.AddPolicy(DeviceReadPolicyName, policy => policy.RequirePermission(DeviceReadBackingPermissionCode));
            options.AddPolicy(DeviceUpdatePolicyName, policy => policy.RequirePermission(DeviceUpdateBackingPermissionCode));
            options.AddPolicy(DeviceRevokePolicyName, policy => policy.RequirePermission(DeviceRevokeBackingPermissionCode));
            options.AddPolicy(TaskReadPolicyName, policy => policy.RequirePermission(TaskReadBackingPermissionCode));
            options.AddPolicy(CalendarReadPolicyName, policy => policy.RequirePermission(TaskReadBackingPermissionCode));
            options.AddPolicy(CalendarEventCreatePolicyName, policy => policy.RequirePermission(CalendarEventCreateBackingPermissionCode));
            options.AddPolicy(CalendarEventUpdatePolicyName, policy => policy.RequirePermission(CalendarEventUpdateBackingPermissionCode));
            options.AddPolicy(CalendarEventDeletePolicyName, policy => policy.RequirePermission(CalendarEventDeleteBackingPermissionCode));
            options.AddPolicy(RecurrenceReadPolicyName, policy => policy.RequirePermission("recurrence.read"));
            options.AddPolicy(RecurrenceManagePolicyName, policy => policy.RequirePermission("recurrence.manage"));
            options.AddPolicy(TaskCreatePolicyName, policy => policy.RequirePermission(TaskCreateBackingPermissionCode));
            options.AddPolicy(TaskUpdatePolicyName, policy => policy.RequirePermission(TaskUpdateBackingPermissionCode));
            options.AddPolicy(TaskChangeStatusPolicyName, policy => policy.RequirePermission(TaskChangeStatusBackingPermissionCode));

            foreach (var routePermission in Task.Application.ProductData.ProductApiRoutes.All
                         .Select(static route => route.Permission)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var policyName = GetProductRoutePolicyName(routePermission);
                if (options.GetPolicy(policyName) is not null)
                {
                    continue;
                }

                var permissionCode = NormalizeProductRoutePermissionCode(routePermission);
                options.AddPolicy(
                    policyName,
                    policy => policy.RequirePermission(permissionCode));
            }
        });

        return services;
    }
}
