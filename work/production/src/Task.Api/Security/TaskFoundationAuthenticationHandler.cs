using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Task.Api.Security;

/// <summary>
/// Fails closed until the approved JWT plus server-session adapter is implemented.
/// It intentionally accepts no client-supplied identity header or development shortcut.
/// </summary>
internal sealed class TaskFoundationAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TaskFoundationAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override global::System.Threading.Tasks.Task<AuthenticateResult> HandleAuthenticateAsync() =>
        global::System.Threading.Tasks.Task.FromResult(AuthenticateResult.NoResult());

    protected override global::System.Threading.Tasks.Task HandleChallengeAsync(AuthenticationProperties properties) =>
        TaskApiProblemResponse.WriteAsync(
            Context,
            StatusCodes.Status401Unauthorized,
            code: "AUTHENTICATION_REQUIRED",
            title: "Authentication is required.",
            retryable: true);

    protected override global::System.Threading.Tasks.Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        TaskApiProblemResponse.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            code: "FORBIDDEN",
            title: "The requested operation is not permitted.",
            retryable: false);
}
