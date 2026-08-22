using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.Api.Audit;

/// <summary>
/// Maps GET /api/v1/audit — organization audit journal read (contract row 67).
/// Requires permission.audit.read. Problems go through TaskApiProblemResponse.WriteAsync.
/// </summary>
internal static class AuditEndpoints
{
    private const string AuditRoute = "/api/v1/audit";
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(AuditRoute, async (
            HttpContext context,
            string? action,
            string? outcome,
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
                    "Audit is not configured",
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
                    ActionFilter: string.IsNullOrWhiteSpace(action) ? null : action,
                    OutcomeFilter: string.IsNullOrWhiteSpace(outcome) ? null : outcome,
                    FromUtc: from,
                    ToUtc: to,
                    PageToken: string.IsNullOrWhiteSpace(pageToken) ? null : pageToken,
                    PageSize: pageSize ?? DefaultPageSize),
                cancellationToken);

            var items = page.Entries
                .Select(entry => new AuditEntryItemResponse(
                    entry.Id,
                    entry.OccurredAt,
                    entry.ActorUserId,
                    entry.ActorSessionId,
                    entry.ActionCode,
                    entry.Outcome,
                    entry.ReasonCode,
                    entry.CorrelationId,
                    entry.RequestId))
                .ToArray();

            return Results.Json(new AuditListResponse(items, page.NextPageToken));
        }).RequireAuthorization(TaskPermissionAuthorization.AuditReadPolicyName);

        return app;
    }

    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context) =>
        context.Items.TryGetValue(
            TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName,
            out var value)
            && value is AuthenticatedRequestContext requestContext
            ? requestContext
            : null;

    private static async Task<IResult> WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        bool retryable,
        CancellationToken cancellationToken = default)
    {
        await TaskApiProblemResponse.WriteAsync(
            context,
            statusCode,
            code,
            title,
            retryable);

        return Results.Empty;
    }

    internal sealed record AuditEntryItemResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc,
        [property: JsonPropertyName("actorUserId")] Guid? ActorUserId,
        [property: JsonPropertyName("actorSessionId")] Guid? ActorSessionId,
        [property: JsonPropertyName("actionCode")] string ActionCode,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("reasonCode")] string? ReasonCode,
        [property: JsonPropertyName("correlationId")] Guid CorrelationId,
        [property: JsonPropertyName("requestId")] Guid RequestId);

    internal sealed record AuditListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<AuditEntryItemResponse> Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);
}
