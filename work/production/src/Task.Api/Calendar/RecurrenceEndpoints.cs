using System.Globalization;
using System.Text.Json;
using Task.Api.Security;
using Task.Application.Calendar;
using Task.Application.Security;
using Task.Domain.Recurrence;

namespace Task.Api.Calendar;

internal static class RecurrenceEndpoints
{
    private const string Route = "/api/v1/recurrence-series";
    public static IEndpointRouteBuilder MapRecurrenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, (Delegate)((HttpContext c) => Handle(c, "list"))).RequireAuthorization(TaskPermissionAuthorization.RecurrenceReadPolicyName);
        app.MapGet(Route + "/{id:guid}", (HttpContext c, Guid id) => Handle(c, "get", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceReadPolicyName);
        app.MapGet(Route + "/{id:guid}/occurrences", (HttpContext c, Guid id) => Handle(c, "occurrences", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceReadPolicyName);
        app.MapPost(Route + "/preview", (Delegate)((HttpContext c) => Handle(c, "preview"))).RequireAuthorization(TaskPermissionAuthorization.RecurrenceReadPolicyName);
        app.MapPost(Route + "/{id:guid}/preview", (HttpContext c, Guid id) => Handle(c, "preview", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceReadPolicyName);
        app.MapPost(Route, (Delegate)((HttpContext c) => Handle(c, "create"))).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        app.MapPatch(Route + "/{id:guid}", (HttpContext c, Guid id) => Handle(c, "patch", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        app.MapPost(Route + "/{id:guid}/generate", (HttpContext c, Guid id) => Handle(c, "generate", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        app.MapPost(Route + "/{id:guid}/apply-change", (HttpContext c, Guid id) => Handle(c, "apply-change", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        app.MapDelete(Route + "/{id:guid}", (HttpContext c, Guid id) => Handle(c, "cancelled", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        app.MapPost(Route + "/{id:guid}/resume", (HttpContext c, Guid id) => Handle(c, "active", id)).RequireAuthorization(TaskPermissionAuthorization.RecurrenceManagePolicyName);
        return app;
    }

    private static async global::System.Threading.Tasks.Task<IResult> Handle(HttpContext context, string operation, Guid id = default)
    {
        try
        {
            var identity = context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName] as AuthenticatedRequestContext;
            var service = context.RequestServices.GetService<RecurrenceService>();
            if (identity is null || service is null) return await Problem(context, 503, "INTERNAL_ERROR", "Recurrence access is not configured.");
            var org = identity.OrganizationId; var actor = identity.UserAccountId;
            if (operation == "list") return Results.Json(new { items = service.List(org).Select(RecurrenceService.ToResponse), nextCursor = (string?)null });
            if (operation == "get")
            {
                var record = service.Get(org, id); context.Response.Headers.ETag = $"\"v{record.Version}\"";
                return Results.Json(RecurrenceService.ToResponse(record));
            }
            if (operation == "occurrences") return Results.Json(service.GetOccurrences(org, id));
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            if (body.Length > 100_000) return await Problem(context, 413, "VALIDATION_FAILED", "Recurrence request is too large.");
            if (operation == "preview")
            {
                if (id != Guid.Empty) _ = service.Get(org, id);
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                EnsureFields(root, "rule", "fromDate", "limit");
                var definition = root.GetProperty("rule").Deserialize<RecurrenceDefinition>(RecurrenceService.JsonOptions)!;
                return Results.Json(RecurrenceService.Preview(definition, ReadDate(root, "fromDate"), root.GetProperty("limit").GetInt32()));
            }
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            // PATCH has no required idempotency header in the canonical contract.
            if (operation == "patch" && key.Length == 0) key = Guid.NewGuid().ToString("N");
            RecurrenceReply reply;
            if (operation == "create") reply = service.Create(org, actor, key, body);
            else if (operation == "generate")
            {
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                EnsureFields(root, "throughDate", "expectedSeriesVersion");
                reply = service.Generate(org, actor, id, root.GetProperty("expectedSeriesVersion").GetInt64(), key, ReadDate(root, "throughDate"));
            }
            else
            {
                var version = ReadVersion(context);
                if (operation == "patch") reply = service.Patch(org, actor, id, version, key, body);
                else if (operation == "apply-change")
                {
                    using var document = JsonDocument.Parse(body); var root = document.RootElement;
                    EnsureFields(root, "scope", "patch", "expectedTaskVersion");
                    // The baseline DTO omits the target identity. A required query key
                    // completes the contract without reinterpreting scope or task version.
                    var target = DateOnly.ParseExact(context.Request.Query["occurrenceKey"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var scope = root.GetProperty("scope").GetString() switch
                    { "this_occurrence" => RecurrenceChangeScope.ThisOccurrence, "this_and_future" => RecurrenceChangeScope.ThisAndFuture,
                        "entire_series" => RecurrenceChangeScope.EntireSeries, _ => throw new ArgumentException("Choose an explicit scope.") };
                    var patch = root.GetProperty("patch"); EnsureFields(patch, "title", "priority", "plannedDurationMinutes");
                    var template = service.Get(org, id).Definition.Template;
                    var title = patch.TryGetProperty("title", out var t) ? t.GetString()! : template.Title;
                    var priority = patch.TryGetProperty("priority", out var p) ? p.GetString()! : template.Priority;
                    var duration = patch.TryGetProperty("plannedDurationMinutes", out var d)
                        ? d.ValueKind == JsonValueKind.Null ? (int?)null : d.GetInt32() : template.PlannedDurationMinutes;
                    reply = service.ApplyChange(org, actor, id, version, key, target, root.GetProperty("expectedTaskVersion").GetInt32(), scope, title, priority, duration,
                        JsonSerializer.Serialize(new { version, target, body }));
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var document = JsonDocument.Parse(body); var root = document.RootElement;
                        EnsureFields(root, "reason", "expectedVersion");
                        if (root.TryGetProperty("expectedVersion", out var expected) && expected.GetInt64() != version) throw new ArgumentException("Version does not match If-Match.");
                    }
                    reply = service.SetStatus(org, actor, id, version, key, operation);
                }
            }
            context.Response.Headers.ETag = $"\"v{reply.Version}\"";
            return Results.Content(reply.Json, "application/json", statusCode: reply.Status);
        }
        catch (RecurrenceRequestException exception) { return await Problem(context, exception.Status, exception.Code, exception.Message); }
        catch (JsonException) { return await Problem(context, 400, "MALFORMED_JSON", "The recurrence JSON is invalid."); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException or OverflowException)
        { return await Problem(context, 422, "VALIDATION_FAILED", "Проверьте правило повторения, даты, область изменения и версию."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception) { return await Problem(context, 503, "INTERNAL_ERROR", "Recurrence access is temporarily unavailable."); }
    }
    private static void EnsureFields(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new ArgumentException("Unknown or duplicate property.");
    }
    private static DateOnly ReadDate(JsonElement root, string name) => DateOnly.ParseExact(root.GetProperty(name).GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static long ReadVersion(HttpContext context)
    {
        var tag = context.Request.Headers.IfMatch.ToString();
        if (tag.Length == 0) throw new RecurrenceRequestException(428, "PRECONDITION_REQUIRED", "If-Match is required.");
        if (tag.Length < 4 || !tag.StartsWith("\"v", StringComparison.Ordinal) || !tag.EndsWith('"') || tag[2] == '0'
            || !long.TryParse(tag.AsSpan(2, tag.Length - 3), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new ArgumentException("Invalid strong ETag.");
        return version;
    }
    private static async global::System.Threading.Tasks.Task<IResult> Problem(HttpContext context, int status, string code, string title)
    { await TaskApiProblemResponse.WriteAsync(context, status, code, title, status >= 500); return Results.Empty; }
}
