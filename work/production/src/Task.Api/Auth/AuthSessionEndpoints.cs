using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.Api.Auth;

/// <summary>
/// Maps the authenticated desktop auth session endpoints: logout, logout-all, current
/// session, session listing, session revocation, password change and login-attempt listing.
/// Every endpoint requires an authenticated request (fallback policy foundation); the
/// identity is read from the AuthenticatedRequestContext stored by TaskJwtAuthenticationHandler.
/// All problems are written through TaskApiProblemResponse.WriteAsync.
/// </summary>
internal static class AuthSessionEndpoints
{
    private const string LogoutRoute = "/api/v1/auth/logout";
    private const string LogoutAllRoute = "/api/v1/auth/logout-all";
    private const string SessionRoute = "/api/v1/auth/session";
    private const string SessionsRoute = "/api/v1/auth/sessions";
    private const string RevokeSessionRoute = "/api/v1/auth/sessions/{sessionId:guid}/revoke";
    private const string ChangePasswordRoute = "/api/v1/auth/change-password";
    private const string LoginAttemptsRoute = "/api/v1/auth/login-attempts";

    private const string UserLogoutRevokeReason = "user-logout";
    private const string UserLogoutAllRevokeReason = "user-logout-all";
    private const string UserRevokedRevokeReason = "user-revoked";

    private const string UserLoggedInActionCode = "UserLoggedIn";
    private const string LoginFailedActionCode = "LoginFailed";

    private const int MaxPageSize = 200;

    private const int AuditQueryDefaultPageSize = 50;

    public static IEndpointRouteBuilder MapAuthSessionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(LogoutRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var sessionRepository = context.RequestServices.GetService<ISessionRepository>();
            if (sessionRepository is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            sessionRepository.RevokeSession(
                requestContext.OrganizationId,
                requestContext.SessionId,
                UserLogoutRevokeReason);

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost(LogoutAllRoute, async (HttpContext context) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true);
            }

            var sessionRepository = context.RequestServices.GetService<ISessionRepository>();
            if (sessionRepository is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true);
            }

            var revokedCount = sessionRepository.RevokeAllUserSessions(
                requestContext.OrganizationId,
                requestContext.UserAccountId,
                exceptSessionId: requestContext.SessionId,
                UserLogoutAllRevokeReason);

            return Results.Json(new LogoutAllResponse(revokedCount));
        }).RequireAuthorization();

        app.MapGet(SessionRoute, async (HttpContext context) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true);
            }

            var credentialStore = context.RequestServices.GetService<IAccountCredentialStore>();
            if (credentialStore is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true);
            }

            var mustChangePassword = await credentialStore.GetMustChangePasswordAsync(
                requestContext.OrganizationId,
                requestContext.UserAccountId,
                context.RequestAborted);

            return Results.Json(new CurrentSessionResponse(
                requestContext.UserAccountId,
                requestContext.SessionId,
                requestContext.OrganizationId,
                requestContext.CredentialVersion,
                requestContext.AuthorizationScopeVersion,
                mustChangePassword));
        }).RequireAuthorization();

        // v1 simplification: the session list is not paginated. The repository returns at
        // most 200 rows (newest activity first), which bounds the response; pagination of
        // the session list is deferred to a later API version.
        app.MapGet(SessionsRoute, async (HttpContext context) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true);
            }

            var sessionRepository = context.RequestServices.GetService<ISessionRepository>();
            if (sessionRepository is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true);
            }

            var items = sessionRepository
                .GetUserSessions(requestContext.OrganizationId, requestContext.UserAccountId)
                .Select(ToResponse)
                .ToArray();

            return Results.Json(items);
        }).RequireAuthorization();

        app.MapPost(RevokeSessionRoute, async (
            HttpContext context,
            Guid sessionId,
            CancellationToken cancellationToken) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var sessionRepository = context.RequestServices.GetService<ISessionRepository>();
            if (sessionRepository is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var target = sessionRepository.GetSession(requestContext.OrganizationId, sessionId);
            if (target is null)
            {
                // The session is not disclosed: an absent session and an invisible session
                // produce the same response.
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "OBJECT_NOT_VISIBLE",
                    "The requested session is not visible.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            if (target.UserAccountId != requestContext.UserAccountId)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "FORBIDDEN",
                    "The requested operation is not permitted.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            // Idempotent by design: revoking an already revoked session is a no-op and still
            // succeeds, because GetSession returns sessions in any state.
            sessionRepository.RevokeSession(
                requestContext.OrganizationId,
                target.SessionId,
                UserRevokedRevokeReason);

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost(ChangePasswordRoute, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var passwordChangeService = context.RequestServices.GetService<PasswordChangeService>();
            if (passwordChangeService is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            ChangePasswordRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ChangePasswordRequest>(cancellationToken);
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
                || !ValidateChangePasswordRequest(request, out validationMessage))
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

            var result = await passwordChangeService.ChangePasswordAsync(
                requestContext.OrganizationId,
                requestContext.UserAccountId,
                request.CurrentPassword,
                request.NewPassword,
                requestContext.SessionId,
                cancellationToken);

            return result.Outcome switch
            {
                PasswordChangeOutcome.Success => Results.NoContent(),

                // UnknownAccount and InvalidCurrentPassword collapse into one caller-facing
                // error so the account existence is never disclosed.
                PasswordChangeOutcome.UnknownAccount or PasswordChangeOutcome.InvalidCurrentPassword =>
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        "INVALID_CREDENTIALS",
                        "The current password is incorrect.",
                        retryable: false,
                        cancellationToken: cancellationToken),

                PasswordChangeOutcome.AccountBlocked => await WriteProblemAsync(
                    context,
                    StatusCodes.Status423Locked,
                    "ACCOUNT_BLOCKED",
                    "The account is blocked.",
                    retryable: false,
                    cancellationToken: cancellationToken),

                PasswordChangeOutcome.WeakPassword or PasswordChangeOutcome.PasswordReuseDetected =>
                    await WriteProblemAsync(
                        context,
                        StatusCodes.Status422UnprocessableEntity,
                        "VALIDATION_FAILED",
                        "The new password does not meet the password policy.",
                        retryable: false,
                        cancellationToken: cancellationToken),

                _ => throw new InvalidOperationException(
                    $"Unexpected password change outcome {result.Outcome}.")
            };
        }).RequireAuthorization();

        app.MapGet(LoginAttemptsRoute, async (
            HttpContext context,
            string? result,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? pageToken,
            int? pageSize,
            CancellationToken cancellationToken) =>
        {
            var requestContext = ReadRequestContext(context);
            if (requestContext is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The authenticated request context is unavailable.",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            var auditEntryStore = context.RequestServices.GetService<IAuditEntryStore>();
            if (auditEntryStore is null)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Auth endpoints are not configured",
                    retryable: true,
                    cancellationToken: cancellationToken);
            }

            if (pageSize is < 1 or > MaxPageSize)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_FAILED",
                    "pageSize must be between 1 and 200.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            if (from.HasValue && to.HasValue && from > to)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_FAILED",
                    "from must not be later than to.",
                    retryable: false,
                    cancellationToken: cancellationToken);
            }

            // The login-attempt audience spans UserLoggedIn and LoginFailed entries; the
            // store's ActionFilter matches a single action code, so no action filter is
            // passed and the projection narrows the returned page to login events. The
            // "result" query parameter maps to the audit outcome (success/failed).
            var page = await auditEntryStore.ReadAsync(
                new AuditQuery(
                    requestContext.OrganizationId,
                    ActionFilter: null,
                    OutcomeFilter: string.IsNullOrWhiteSpace(result) ? null : result,
                    FromUtc: from,
                    ToUtc: to,
                    PageToken: string.IsNullOrWhiteSpace(pageToken) ? null : pageToken,
                    PageSize: pageSize ?? AuditQueryDefaultPageSize),
                cancellationToken);

            var items = page.Entries
                .Where(entry =>
                    entry.ActionCode is UserLoggedInActionCode or LoginFailedActionCode)
                .Select(entry => new LoginAttempt(
                    entry.OccurredAt,
                    entry.Outcome,
                    entry.ReasonCode,
                    entry.ActorUserId))
                .ToArray();

            return Results.Json(new LoginAttemptsResponse(items, page.NextPageToken));
        }).RequireAuthorization(TaskPermissionAuthorization.LoginAttemptsReadPolicyName);

        return app;
    }

    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context) =>
        context.Items.TryGetValue(
            TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName,
            out var value)
            && value is AuthenticatedRequestContext requestContext
            ? requestContext
            : null;

    private static bool ValidateChangePasswordRequest(ChangePasswordRequest request, out string? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            message = "Current password is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            message = "New password is required.";
            return false;
        }

        return true;
    }

    private static UserSessionItemResponse ToResponse(UserSessionListItem item) =>
        new(
            item.SessionId,
            item.DeviceDisplayName,
            item.CreatedAtUtc,
            item.LastSeenAtUtc,
            item.IdleExpiresAtUtc,
            item.AbsoluteExpiresAtUtc,
            item.RevokedAtUtc,
            item.RevokeReason);

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

    internal sealed record ChangePasswordRequest(
        [property: JsonPropertyName("currentPassword")] string CurrentPassword,
        [property: JsonPropertyName("newPassword")] string NewPassword);

    internal sealed record LogoutAllResponse(
        [property: JsonPropertyName("revokedSessionCount")] int RevokedSessionCount);

    internal sealed record CurrentSessionResponse(
        [property: JsonPropertyName("userId")] Guid UserId,
        [property: JsonPropertyName("sessionId")] Guid SessionId,
        [property: JsonPropertyName("organizationId")] Guid OrganizationId,
        [property: JsonPropertyName("credentialVersion")] long CredentialVersion,
        [property: JsonPropertyName("authorizationScopeVersion")] long AuthorizationScopeVersion,
        [property: JsonPropertyName("mustChangePassword")] bool MustChangePassword);

    internal sealed record UserSessionItemResponse(
        [property: JsonPropertyName("sessionId")] Guid SessionId,
        [property: JsonPropertyName("deviceDisplayName")] string? DeviceDisplayName,
        [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("lastSeenAtUtc")] DateTimeOffset LastSeenAtUtc,
        [property: JsonPropertyName("idleExpiresAtUtc")] DateTimeOffset IdleExpiresAtUtc,
        [property: JsonPropertyName("absoluteExpiresAtUtc")] DateTimeOffset AbsoluteExpiresAtUtc,
        [property: JsonPropertyName("revokedAtUtc")] DateTimeOffset? RevokedAtUtc,
        [property: JsonPropertyName("revokeReason")] string? RevokeReason);

    internal sealed record LoginAttempt(
        [property: JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("reasonCode")] string? ReasonCode,
        [property: JsonPropertyName("actorUserId")] Guid? ActorUserId);

    internal sealed record LoginAttemptsResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<LoginAttempt> Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);
}
