using System.Reflection;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Server;

namespace Task.Api.Server;

/// <summary>
/// Maps GET /api/v1/system/version — authenticated build/API/schema version (canonical
/// contract row 240). The response exposes only public, non-sensitive deployment metadata:
/// the server release version, the API version label, the applied database schema version and
/// the minimum supported desktop version. Connection strings, configuration, secrets and
/// paths never enter the payload.
/// </summary>
internal static class SystemVersionEndpoints
{
    private const string SystemVersionRoute = "/api/v1/system/version";
    private const string ServerVersionConfigKey = "Task:Server:Version";

    private static readonly Lazy<string> FallbackServerVersion = new(() =>
        ReadInformationalVersion(Assembly.GetExecutingAssembly()));

    public static IEndpointRouteBuilder MapSystemVersionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(SystemVersionRoute, async (
            HttpContext context,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var capabilities = context.RequestServices.GetService<ServerCapabilitiesService>();
            if (capabilities is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Capabilities are not configured",
                    retryable: true);
            }

            var serverVersion = configuration[ServerVersionConfigKey];
            if (string.IsNullOrWhiteSpace(serverVersion))
            {
                serverVersion = FallbackServerVersion.Value;
            }

            return Results.Json(new SystemVersionResponse(
                NormalizeServerVersion(serverVersion),
                capabilities.MinimumApiVersion,
                capabilities.SchemaVersion,
                capabilities.MinimumDesktopVersion));
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

    private static string NormalizeServerVersion(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 32)
        {
            return "1.0.0";
        }

        return trimmed;
    }

    private static string ReadInformationalVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var core = informational.Split('+')[0].Split('-')[0];
            if (ServerCapabilitiesService.TryParseReleaseVersion(core, out var version))
            {
                return version.ToString(3);
            }
        }

        var assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? "1.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }

    internal sealed record SystemVersionResponse(
        [property: JsonPropertyName("serverVersion")] string ServerVersion,
        [property: JsonPropertyName("apiVersion")] string ApiVersion,
        [property: JsonPropertyName("databaseSchemaVersion")] int DatabaseSchemaVersion,
        [property: JsonPropertyName("minimumDesktopVersion")] string MinimumDesktopVersion);
}
