using Task.Api.Security;
using Task.Application.Server;

namespace Task.Api.Capabilities;

internal sealed class ClientVersionCompatibilityMiddleware
{
    public const string ClientVersionHeader = "X-Task-Client-Version";
    public const string MinimumVersionHeader = "X-Task-Minimum-Client-Version";
    public const string RecommendedVersionHeader = "X-Task-Recommended-Client-Version";

    private readonly RequestDelegate _next;

    public ClientVersionCompatibilityMiddleware(RequestDelegate next) =>
        _next = next ?? throw new ArgumentNullException(nameof(next));

    public async global::System.Threading.Tasks.Task InvokeAsync(
        HttpContext context,
        ServerCapabilitiesService capabilitiesService)
    {
        var supplied = context.Request.Headers[ClientVersionHeader].ToString();
        if (string.IsNullOrWhiteSpace(supplied))
        {
            await _next(context);
            return;
        }

        var capabilities = capabilitiesService.GetCapabilities();
        context.Response.Headers[MinimumVersionHeader] = capabilities.MinimumClientVersion;
        context.Response.Headers[RecommendedVersionHeader] = capabilities.RecommendedClientVersion;

        if (capabilitiesService.IsClientVersionSupported(supplied))
        {
            await _next(context);
            return;
        }

        await TaskApiProblemResponse.WriteAsync(
            context,
            StatusCodes.Status426UpgradeRequired,
            "CLIENT_VERSION_UNSUPPORTED",
            "The Task desktop client must be updated.",
            retryable: false);
    }
}
