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
            var permissionDecisionService = context.RequestServices.GetService<PermissionDecisionService>();
            if (credentialStore is null || permissionDecisionService is null)
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

            IReadOnlyList<string> capabilities;
            try
            {
                capabilities = await ResolveTaskCapabilitiesAsync(
                    permissionDecisionService,
                    requestContext,
                    context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "INTERNAL_ERROR",
                    "Session capabilities are temporarily unavailable",
                    retryable: true);
            }

            var users = context.RequestServices.GetService<IUserAccountReadStore>();
            var devices = context.RequestServices.GetService<IDeviceRegistrationStore>();
            var sessions = context.RequestServices.GetService<ISessionRepository>();
            var user = users is null ? null : await users.GetByIdAsync(requestContext.OrganizationId, requestContext.UserAccountId, context.RequestAborted);
            var session = sessions?.GetSession(requestContext.OrganizationId, requestContext.SessionId);
            var device = devices is null || session?.DeviceId is null ? null : await devices.GetReadModelAsync(requestContext.OrganizationId, session.DeviceId.Value, context.RequestAborted);
            if (user is null || device is null)
                return await WriteProblemAsync(context,503,"INTERNAL_ERROR","Session profile is unavailable.",true);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new {
                // Retain the desktop foundation metadata as additive compatibility fields.
                userId = requestContext.UserAccountId, sessionId = requestContext.SessionId,
                organizationId = requestContext.OrganizationId, credentialVersion = requestContext.CredentialVersion,
                authorizationScopeVersion = requestContext.AuthorizationScopeVersion, capabilities, mustChangePassword,
                user = Task.Api.Users.UserEndpoints.ToResponse(user), device = DeviceEndpoints.ToResponse(device),
                permissionCodes = capabilities, scopeVersion = requestContext.AuthorizationScopeVersion,
                accessExpiresAt = (DateTime)context.Items["Task.AccessExpiresAtUtc"]!
            });
        }).RequireAuthorization();

        app.MapGet(SessionsRoute, async (HttpContext context, Guid? userId, int? page, string? cursor) =>
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

            if (page is < 1 or > 100000 || (cursor is not null && (!int.TryParse(cursor, out var parsed) || parsed < 1 || parsed > 100000)))
                return await WriteProblemAsync(context,400,"VALIDATION_FAILED","Invalid session page.",false);
            var all = await IdentityScope.CanManageAllAsync(context, requestContext);
            if (!all && userId.HasValue && userId.Value != requestContext.UserAccountId)
                return await WriteProblemAsync(context,404,"OBJECT_NOT_VISIBLE","The requested sessions are not visible.",false);
            var result = sessionRepository.GetSessionPage(requestContext.OrganizationId,
                all ? userId : requestContext.UserAccountId, cursor is null ? page ?? 1 : int.Parse(cursor));
            var items = result.Items.Select(item => new {
                id = item.SessionId, userAccountId = item.UserAccountId, deviceId = item.DeviceId,
                status = item.RevokedAtUtc is not null ? "revoked" : item.IdleExpiresAtUtc <= DateTimeOffset.UtcNow || item.AbsoluteExpiresAtUtc <= DateTimeOffset.UtcNow ? "expired" : "active",
                createdAt = item.CreatedAtUtc.UtcDateTime, lastSeenAt = item.LastSeenAtUtc.UtcDateTime,
                idleExpiresAt = item.IdleExpiresAtUtc.UtcDateTime, absoluteExpiresAt = item.AbsoluteExpiresAtUtc.UtcDateTime
            });
            return Results.Json(new { items, nextCursor = result.NextCursor, total = result.Total });
        }).RequireAuthorization(TaskPermissionAuthorization.SessionReadPolicyName);

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

            if (target.UserAccountId != requestContext.UserAccountId && !await IdentityScope.CanManageAllAsync(context, requestContext))
            {
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "OBJECT_NOT_VISIBLE",
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
        }).RequireAuthorization(TaskPermissionAuthorization.SessionRevokePolicyName);

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
            Guid? userId,
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

            var page = await auditEntryStore.ReadAsync(
                new AuditQuery(
                    requestContext.OrganizationId,
                    ActionFilter: null,
                    OutcomeFilter: string.IsNullOrWhiteSpace(result) ? null : result,
                    FromUtc: from,
                    ToUtc: to,
                    PageToken: string.IsNullOrWhiteSpace(pageToken) ? null : pageToken,
                    PageSize: pageSize ?? AuditQueryDefaultPageSize,
                    ActorUserId: userId, LoginAttemptsOnly: true),
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

    private static async global::System.Threading.Tasks.Task<IReadOnlyList<string>> ResolveTaskCapabilitiesAsync(
        PermissionDecisionService decisionService,
        AuthenticatedRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var capabilities = new List<string>(TaskPermissionAuthorization.TaskCapabilities.Count);
        foreach (var (permissionCode, capability) in TaskPermissionAuthorization.TaskCapabilities)
        {
            var decision = await decisionService.EvaluateAsync(
                requestContext.OrganizationId,
                requestContext.UserAccountId,
                permissionCode,
                cancellationToken);
            if (decision.Allowed)
            {
                capabilities.Add(capability);
            }
        }

        return capabilities;
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
        [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
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
