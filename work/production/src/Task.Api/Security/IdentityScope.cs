using Task.Application.Security;

namespace Task.Api.Security;

internal static class IdentityScope
{
    public static async Task<bool> CanManageAllAsync(HttpContext context, AuthenticatedRequestContext identity)
    {
        var decisions = context.RequestServices.GetRequiredService<PermissionDecisionService>();
        return (await decisions.EvaluateAsync(identity.OrganizationId, identity.UserAccountId,
            "identity.account.manage", context.RequestAborted)).Allowed;
    }
}
