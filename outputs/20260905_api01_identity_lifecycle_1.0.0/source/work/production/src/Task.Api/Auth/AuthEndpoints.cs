using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.Api.Auth;

internal static class AuthEndpoints
{
    private const string LoginRoute = "/api/v1/auth/login";
    private const string RefreshRoute = "/api/v1/auth/refresh";

    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maps the desktop auth endpoints. Fail-closed by design: when the auth services are not
    /// registered (deployment not configured with Task:Identity secrets and a database), every
    /// request is answered with 503 INTERNAL_ERROR "Auth endpoints are not configured" before
    /// any request body is read, so no token can ever be issued without full configuration.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(LoginRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var loginService = context.RequestServices.GetService<LoginService>();
            var accessTokenIssuer = context.RequestServices.GetService<JwtAccessTokenIssuer>();

            if (loginService is null || accessTokenIssuer is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            LoginRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<LoginRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "MALFORMED_JSON",
                    "The request body is not valid JSON.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            var validationMessage = (string?)null;
            if (request is null
                || !ValidateLoginRequest(request, out validationMessage))
            {
                var message = validationMessage ?? "The request failed validation.";
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_FAILED",
                    message,
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            var rateLimiter = context.RequestServices.GetService<LoginRateLimiter>();
            if (rateLimiter is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateLimitKey = $"{ip}|{request.Login.ToLowerInvariant()}";
            var rateLimitDecision = rateLimiter.TryRecord(rateLimitKey);
            if (!rateLimitDecision.IsAllowed)
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(rateLimitDecision.RetryAfter.TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Items[TaskApiProblemResponse.AuthenticationResponseWrittenItemName] = true;

                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "RATE_LIMITED",
                    "Too many login attempts.",
                    retryable: true,
                    retryAfterSeconds: retryAfterSeconds,
                    cancellationToken: cancellationToken);
            }

            var correlationId = ParseCorrelationId(context);
            var requestId = Guid.NewGuid();
            var fingerprintHash = ComputeSha256Hex(request.Device.DeviceKey);

            var command = new LoginCommand(
                request.Login,
                request.Password,
                request.Device.DeviceKey,
                request.Device.DeviceName,
                fingerprintHash,
                correlationId,
                requestId,
                request.Device.Platform.ToLowerInvariant(),
                request.Device.AppVersion,
                request.Device.OsVersion);

            var outcome = await loginService.LoginAsync(command, cancellationToken);

            if (outcome is LoginOutcome.Succeeded)
            {
                rateLimiter.Reset(rateLimitKey);
            }

            return outcome switch
            {
                LoginOutcome.Succeeded succeeded => await WriteTokensAsync(
                    context,
                    accessTokenIssuer,
                    succeeded.UserId,
                    succeeded.SessionId,
                    succeeded.OrganizationId,
                    succeeded.CredentialVersion,
                    succeeded.AuthorizationScopeVersion,
                    succeeded.RawRefreshToken,
                    succeeded.RefreshExpiresAtUtc,
                    cancellationToken),

                LoginOutcome.InvalidCredentials => await WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "INVALID_CREDENTIALS",
                    "The login or password is incorrect.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                LoginOutcome.AccountBlocked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status423Locked,
                    "ACCOUNT_BLOCKED",
                    "The account is blocked.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                LoginOutcome.LockedTemporarily locked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status423Locked,
                    "ACCOUNT_LOCKED_TEMPORARILY",
                    "The account is temporarily locked.",
                    retryable: true,
                    retryAfterSeconds: (int)locked.Remaining.TotalSeconds,
                    cancellationToken: cancellationToken),

                LoginOutcome.DeviceRevoked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "DEVICE_REVOKED",
                    "The device is revoked.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                _ => throw new InvalidOperationException($"Unexpected login outcome {outcome.GetType().Name}.")
            };
        }).AllowAnonymous();

        app.MapPost(RefreshRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var refreshService = context.RequestServices.GetService<RefreshService>();
            var accessTokenIssuer = context.RequestServices.GetService<JwtAccessTokenIssuer>();

            if (refreshService is null || accessTokenIssuer is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            RefreshRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<RefreshRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "MALFORMED_JSON",
                    "The request body is not valid JSON.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            if (request is null
                || string.IsNullOrWhiteSpace(request.RefreshToken)
                || string.IsNullOrWhiteSpace(request.DeviceKey))
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_FAILED",
                    "The request failed validation.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            var correlationId = ParseCorrelationId(context);
            var requestId = Guid.NewGuid();
            var command = new RefreshCommand(request.RefreshToken, request.DeviceKey, correlationId, requestId);

            var outcome = await refreshService.RefreshAsync(command, cancellationToken);

            return outcome switch
            {
                RefreshOutcome.Succeeded succeeded => await WriteTokensAsync(
                    context,
                    accessTokenIssuer,
                    succeeded.UserId,
                    succeeded.SessionId,
                    succeeded.OrganizationId,
                    succeeded.CredentialVersion,
                    succeeded.AuthorizationScopeVersion,
                    succeeded.NewRefreshToken,
                    succeeded.RefreshExpiresAtUtc,
                    cancellationToken),

                RefreshOutcome.SessionExpired => await WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "SESSION_EXPIRED",
                    "The session has expired.",
                    retryable: true,
                    cancellationToken: cancellationToken),

                RefreshOutcome.SessionRevoked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "SESSION_REVOKED",
                    "The session was revoked.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                RefreshOutcome.ReuseDetected => await WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "REFRESH_TOKEN_REUSE",
                    "The refresh token was reused.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                RefreshOutcome.DeviceRevoked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "DEVICE_REVOKED",
                    "The device is revoked.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                _ => throw new InvalidOperationException($"Unexpected refresh outcome {outcome.GetType().Name}.")
            };
        }).AllowAnonymous();

        return app;
    }

    private static bool ValidateLoginRequest(LoginRequest request, out string? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(request.Login))
        {
            message = "Login is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            message = "Password is required.";
            return false;
        }

        if (request.Device is null)
        {
            message = "Device information is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Device.DeviceKey) || request.Device.DeviceKey.Length < 16)
        {
            message = "Device key must be at least 16 characters.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Device.DeviceName))
        {
            message = "Device name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Device.AppVersion))
        {
            message = "App version is required.";
            return false;
        }

        if (!IsKnownPlatform(request.Device.Platform))
        {
            message = "Platform is not supported.";
            return false;
        }

        return true;
    }

    private static bool IsKnownPlatform(string? platform) => platform?.ToLowerInvariant() switch
    {
        "windows" or "linux" or "macos" => true,
        _ => false,
    };

    private static Guid ParseCorrelationId(HttpContext context)
    {
        var value = TaskApiProblemResponse.GetCorrelationId(context);
        return Guid.TryParseExact(value, "D", out var parsed) ? parsed : Guid.NewGuid();
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<IResult> WriteTokensAsync(
        HttpContext context,
        JwtAccessTokenIssuer issuer,
        Guid userId,
        Guid sessionId,
        Guid organizationId,
        long credentialVersion,
        long authorizationScopeVersion,
        string refreshToken,
        DateTimeOffset refreshExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpiresAt = now + AccessTokenLifetime;

        var accessToken = await issuer.IssueAsync(
            new JwtIssuanceRequest(
                userId,
                sessionId,
                organizationId,
                credentialVersion,
                authorizationScopeVersion,
                now.UtcDateTime,
                AccessTokenLifetime),
            cancellationToken);

        var response = new SessionTokensResponse(
            accessToken,
            accessExpiresAt,
            refreshToken,
            refreshExpiresAtUtc,
            sessionId);

        return Results.Json(response);
    }

    private static async Task<IResult> WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        bool retryable,
        int? retryAfterSeconds = null,
        CancellationToken cancellationToken = default)
    {
        await TaskApiProblemResponse.WriteAsync(
            context,
            statusCode,
            code,
            title,
            retryable,
            retryAfterSeconds);

        return Results.Empty;
    }

    internal sealed record LoginRequest(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("device")] DeviceRequest Device);

    internal sealed record DeviceRequest(
        [property: JsonPropertyName("deviceKey")] string DeviceKey,
        [property: JsonPropertyName("deviceName")] string DeviceName,
        [property: JsonPropertyName("platform")] string Platform,
        [property: JsonPropertyName("appVersion")] string AppVersion,
        [property: JsonPropertyName("osVersion")] string? OsVersion);

    internal sealed record RefreshRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("deviceKey")] string DeviceKey);

    internal sealed record SessionTokensResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("accessExpiresAt")] DateTimeOffset AccessExpiresAt,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("refreshExpiresAt")] DateTimeOffset RefreshExpiresAt,
        [property: JsonPropertyName("sessionId")] Guid SessionId);
}
