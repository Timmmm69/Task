using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Task.Application.Security;

namespace Task.Api.Security;

/// <summary>
/// Authenticates requests bearing an ES256 Bearer access token that passes cryptographic
/// validation and whose referenced server session is authoritative and version-consistent.
/// Fails closed: no token material, claim values or secrets are ever logged.
/// </summary>
internal sealed class TaskJwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticatedRequestContextItemName = "Task.Api.AuthenticatedRequestContext";

    private const string BearerScheme = "Bearer";

    private readonly AccessTokenValidator _accessTokenValidator;
    private readonly JwtVerificationKeys _verificationKeys;
    private readonly ISessionRepository? _sessionRepository;

    public TaskJwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AccessTokenValidator accessTokenValidator,
        JwtVerificationKeys verificationKeys,
        ISessionRepository? sessionRepository)
        : base(options, logger, encoder)
    {
        _accessTokenValidator = accessTokenValidator;
        _verificationKeys = verificationKeys;
        _sessionRepository = sessionRepository;
    }

    protected override async global::System.Threading.Tasks.Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_verificationKeys.HasKeys || _sessionRepository is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!TryReadBearerToken(Context.Request.Headers.Authorization.ToString(), out var accessToken))
        {
            return AuthenticateResult.NoResult();
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return await FailAsync("missing");
        }

        var correlationId = TaskApiProblemResponse.GetCorrelationId(Context);
        var validationResult = await _accessTokenValidator.ValidateAsync(accessToken);
        if (!validationResult.IsSuccess)
        {
            return await FailAsync(
                validationResult.IsExpired ? "expired" : "invalid",
                correlationId);
        }

        var claims = validationResult.Claims!;
        Context.Items["Task.AccessExpiresAtUtc"] = claims.AccessExpiresAtUtc;
        var sessionState = _sessionRepository.GetSessionRequestState(
            claims.Org,
            claims.Sid,
            claims.Cver,
            claims.Sver);
        if (sessionState != SessionRequestState.Active)
        {
            var category = sessionState switch
            {
                SessionRequestState.SessionRevoked => "revoked",
                SessionRequestState.SessionExpired => "expired",
                SessionRequestState.AccountBlocked => "blocked",
                _ => "invalid",
            };
            return await FailAsync(category, correlationId, sessionState);
        }

        Context.Items[AuthenticatedRequestContextItemName] = new AuthenticatedRequestContext(
            claims.Sub,
            claims.Sid,
            claims.Org,
            claims.Cver,
            claims.Sver,
            correlationId,
            Context.TraceIdentifier);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, claims.Sub.ToString("D")),
                new Claim("sub", claims.Sub.ToString("D")),
                new Claim("sid", claims.Sid.ToString("D")),
                new Claim("org", claims.Org.ToString("D")),
                new Claim("cver", claims.Cver.ToString(CultureInfo.InvariantCulture)),
                new Claim("sver", claims.Sver.ToString(CultureInfo.InvariantCulture)),
            ],
            Scheme.Name);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async global::System.Threading.Tasks.Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(TaskApiProblemResponse.AuthenticationResponseWrittenItemName))
        {
            return;
        }

        await TaskApiProblemResponse.WriteAsync(
            Context,
            StatusCodes.Status401Unauthorized,
            code: "AUTHENTICATION_REQUIRED",
            title: "Authentication is required.",
            retryable: true);
    }

    protected override global::System.Threading.Tasks.Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        TaskApiProblemResponse.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            code: "FORBIDDEN",
            title: "The requested operation is not permitted.",
            retryable: false);

    private async global::System.Threading.Tasks.Task<AuthenticateResult> FailAsync(
        string category,
        string? correlationId = null,
        SessionRequestState? sessionState = null)
    {
        Logger.LogWarning(
            "JWT authentication rejected. Category: {Category}; correlation ID: {CorrelationId}",
            category,
            correlationId ?? TaskApiProblemResponse.GetCorrelationId(Context));

        var (statusCode, code, title, retryable) = sessionState switch
        {
            SessionRequestState.SessionRevoked => (
                StatusCodes.Status401Unauthorized,
                "SESSION_REVOKED",
                "The session was revoked.",
                false),
            SessionRequestState.AccountBlocked => (
                StatusCodes.Status423Locked,
                "ACCOUNT_BLOCKED",
                "The account is blocked.",
                false),
            SessionRequestState.SessionExpired or SessionRequestState.VersionMismatch => (
                StatusCodes.Status401Unauthorized,
                "SESSION_EXPIRED",
                "The session has expired.",
                true),
            _ when category == "expired" => (
                StatusCodes.Status401Unauthorized,
                "SESSION_EXPIRED",
                "The session has expired.",
                true),
            _ => (
                StatusCodes.Status401Unauthorized,
                "AUTHENTICATION_REQUIRED",
                "Authentication is required.",
                true),
        };

        await TaskApiProblemResponse.WriteAsync(
            Context,
            statusCode,
            code,
            title,
            retryable);
        Context.Items[TaskApiProblemResponse.AuthenticationResponseWrittenItemName] = true;

        return AuthenticateResult.Fail("Access token validation failed.");
    }

    private static bool TryReadBearerToken(string authorizationHeader, out string accessToken)
    {
        accessToken = string.Empty;
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        var separatorIndex = authorizationHeader.IndexOf(' ');
        if (separatorIndex <= 0)
        {
            return false;
        }

        if (!string.Equals(
                authorizationHeader.Substring(0, separatorIndex),
                BearerScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        accessToken = authorizationHeader.Substring(separatorIndex + 1).Trim();
        return true;
    }
}
