using Task.Api.Security;
using Task.Application.Server;

namespace Task.Api.Capabilities;

/// <summary>
/// Exposes the server capabilities endpoints. Fail-closed by design: when the capability
/// service is not registered the endpoints return 503 INTERNAL_ERROR before any meaningful
/// work is performed. The public endpoint is available to any authenticated client; the
/// administrative alias additionally requires the organization administration permission
/// (canonical <c>System.Configure</c>, production capability <c>organization.manage</c>).
/// Both return the same leak-free canonical payload.
/// </summary>
internal static class CapabilitiesEndpoints
{
    private const string CapabilitiesRoute = "/api/v1/capabilities";
    private const string AdminServerCapabilitiesRoute = "/api/v1/admin/server-capabilities";

    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(CapabilitiesRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var service = context.RequestServices.GetService<ServerCapabilitiesService>();
            if (service is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Capabilities are not configured",
                    retryable: true);
            }

            return Results.Json(service.GetCapabilities());
        }).RequireAuthorization();

        app.MapGet(AdminServerCapabilitiesRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var service = context.RequestServices.GetService<ServerCapabilitiesService>();
            if (service is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Capabilities are not configured",
                    retryable: true);
            }

            return Results.Json(service.GetCapabilities());
        }).RequireAuthorization(TaskPermissionAuthorization.OrganizationManagePolicyName);

        return app;
    }

    private static async Task<IResult> WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        bool retryable)
    {
        await TaskApiProblemResponse.WriteAsync(
            context,
            statusCode,
            code,
            title,
            retryable);

        return Results.Empty;
    }
}
