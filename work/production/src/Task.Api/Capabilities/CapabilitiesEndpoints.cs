using Task.Api.Security;
using Task.Application.Server;

namespace Task.Api.Capabilities;

/// <summary>
/// Exposes the server capabilities endpoint. Fail-closed by design: when the capability service is
/// not registered the endpoint returns 503 INTERNAL_ERROR before any meaningful work is performed.
/// </summary>
internal static class CapabilitiesEndpoints
{
    private const string CapabilitiesRoute = "/api/v1/capabilities";

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

            var capabilities = service.GetCapabilities();
            return Results.Json(capabilities);
        }).RequireAuthorization();

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
