using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using Task.Api.Security;
using Task.Application.ProductData;
using Task.Application.Security;

namespace Task.Api.ProductData;

internal static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var route in ProductApiRoutes.All)
        {
            var endpoint = app.MapMethods(route.Path, [route.Method], (Delegate)((HttpContext context) => HandleAsync(context, route)));
            if (route.AlternativePermission is null)
                endpoint.RequireAuthorization(TaskPermissionAuthorization.GetProductRoutePolicyName(route.Permission));
            else
                endpoint.RequireAuthorization();
        }
        return app;
    }

    private static async Task<IResult> HandleAsync(HttpContext context, ProductApiRoute route)
    {
        try
        {
            var identity = context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName]
                as AuthenticatedRequestContext;
            if (identity is null) throw new ProductApiException(401, "UNAUTHENTICATED", "Authentication is required.");
            var permissions = context.RequestServices.GetService<PermissionDecisionService>();
            var store = context.RequestServices.GetService<IProductApiStore>();
            if (permissions is null || store is null)
                throw new ProductApiException(503, "INTERNAL_ERROR", "Product API is not configured.");
            var requested = RequestedPermissions(route);
            var decisions = await permissions.EvaluateManyAsync(identity.OrganizationId, identity.UserAccountId,
                requested.Select(ProductApiRoute.GetBackingPermission), context.RequestAborted);
            var granted = requested.Where(code => decisions[ProductApiRoute.GetBackingPermission(code)].Allowed)
                .ToHashSet(StringComparer.Ordinal);
            if (!route.Allows(granted)) throw new ProductApiException(403, "FORBIDDEN", "Permission denied.");
            if (route.RequiredReadPermission is { } read && !granted.Contains(read))
                throw new ProductApiException(403, "FORBIDDEN",
                    route.Resource == "tasks" ? "Task read access is required." : "Object read access is required.");

            Guid? ReadId(string name)
            {
                if (!context.Request.RouteValues.TryGetValue(name, out var raw)) return null;
                if (!Guid.TryParseExact(raw?.ToString(), "D", out var id) || id == Guid.Empty)
                    throw new ProductApiException(404, "OBJECT_NOT_VISIBLE", "Object is not visible.");
                return id;
            }
            var id = ReadId("id");
            var childId = ReadId("childId");
            int? version = null;
            if (route.Versioned || context.Request.Headers.ContainsKey("If-Match"))
            {
                var header = context.Request.Headers.IfMatch.ToString();
                if (header.Length == 0) throw new ProductApiException(428, "PRECONDITION_REQUIRED", "If-Match is required.");
                if (header.Length < 4 || !header.StartsWith("\"v", StringComparison.Ordinal) || header[^1] != '"' ||
                    !int.TryParse(header.AsSpan(2, header.Length - 3), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                    value < 1 || header != $"\"v{value}\"")
                    throw new ProductApiException(400, "VALIDATION_FAILED", "If-Match must be a single strong version ETag.");
                version = value;
            }
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if ((route.Idempotent || key.Length > 0) && (key.Length is < 8 or > 200 || key.Any(c => c < '!' || c > '~')))
                throw new ProductApiException(400, "VALIDATION_FAILED", "A printable 8-200 character Idempotency-Key is required.");
            var canHaveBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
                ?? context.Request.ContentLength is not 0;
            var body = canHaveBody ? await ReadBodyAsync(context, identity.OrganizationId) : new JsonObject();
            if (route.Method is "POST" or "PUT" or "PATCH" && route.Operation is "create" or "patch" && body.Count == 0)
                throw new ProductApiException(422, "VALIDATION_FAILED", "At least one writable field is required.");
            var query = context.Request.Query.ToDictionary(p => p.Key, p => p.Value.ToString());
            var hash = key.Length == 0 ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                route.Method + context.Request.Path + context.Request.QueryString + "|" + version + "|" + body.ToJsonString())));
            var correlation = Guid.TryParse(context.Response.Headers["X-Correlation-ID"], out var parsed) ? parsed : Guid.NewGuid();
            var response = store.Execute(new(identity.OrganizationId, identity.UserAccountId, identity.SessionId,
                correlation, route, id, childId, body, query, version, key.Length == 0 ? null : key, hash, granted, context.RequestAborted,
                RevalidatePermissions: true, AuthenticatedSession: identity));
            if (route is { Resource: "notifications", Operation: "poll" }) context.Response.Headers.CacheControl = "no-store";
            if (response.Version is { } responseVersion) context.Response.Headers.ETag = $"\"v{responseVersion}\"";
            if (route.Idempotent || key.Length > 0) context.Response.Headers["Idempotency-Replayed"] = response.WasReplayed ? "true" : "false";
            return response.Status == 204 ? Results.NoContent() : Results.Json(response.Body, statusCode: response.Status);
        }
        catch (ProductApiException exception)
        {
            return await Problem(context, exception.Status, exception.Code, exception.Message);
        }
        catch (JsonException) { return await Problem(context, 400, "MALFORMED_JSON", "Invalid JSON or duplicate properties."); }
        catch (ArgumentException) { return await Problem(context, 422, "VALIDATION_FAILED", "Invalid field value."); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Task.Api.ProductData")
                .LogError("Product API operation {Operation} failed ({Type}).", route.Operation, exception.GetType().Name);
            return await Problem(context, 503, "INTERNAL_ERROR", "Product API is temporarily unavailable.");
        }
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpContext context, Guid organizationId)
    {
        var maximumBodyBytes = context.RequestServices.GetService<IProductSettingsStore>()?
            .GetOrganization(organizationId)?.MaxRequestBytes ?? 1_048_576;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + count > maximumBodyBytes)
                throw new ProductApiException(413, "PAYLOAD_TOO_LARGE", "Body exceeds the request limit.");
            buffer.Write(chunk, 0, count);
        }
        if (buffer.Length == 0) return new JsonObject();

        if (!context.Request.HasJsonContentType())
            throw new ProductApiException(415, "UNSUPPORTED_MEDIA_TYPE", "Use application/json.");
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        using var document = JsonDocument.Parse(bytes);
        ValidateJson(document.RootElement);
        return JsonNode.Parse(bytes.Span) as JsonObject ?? throw new JsonException();
    }

    private static HashSet<string> RequestedPermissions(ProductApiRoute route)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal) { route.Permission, "organization.manage" };
        if (route.AlternativePermission is { } alternative) codes.Add(alternative);
        if (route.RequiredReadPermission is { } read) codes.Add(read);
        if (route.Resource is "search" or "archive" or "trash" or "objects" or "interactions" or "tasks" or "reminders" or "inbox-items")
            codes.UnionWith(["Project.Read", "Contact.Read", "FileCatalog.Read", "Task.Read", "Calendar.Read", "Employee.Read"]);
        if (route.Resource == "tasks") codes.Add("History.Read");
        if (route.Operation is "locations" or "resolve" || route.Resource is "network-resources" or "search")
            codes.Add("FileLocation.ReadSensitivePath");
        if (route.Resource == "search") codes.Add("FileReference.Open");
        if (route.Resource == "objects" || route.Operation == "link-targets")
            codes.UnionWith(["Notification.ReadOwn", "Inbox.ReadOwn"]);
        if (route.Resource == "inbox-items") codes.Add("Task.Update");
        if (route.Resource is "archive" or "trash")
            codes.UnionWith(["Project.Restore", "Contact.Restore", "FileCatalog.Restore", "Task.Update", "CalendarEvent.Update", "Interaction.Update"]);
        if (route.Resource == "objects" && route.Method != "GET")
            codes.UnionWith(["Project.Update", "Contact.Update", "FileCatalog.Update", "Task.Update", "CalendarEvent.Update", "Interaction.Update"]);
        return codes;
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateJson(item);
    }

    private static async Task<IResult> Problem(HttpContext context, int status, string code, string title)
    {
        await TaskApiProblemResponse.WriteAsync(context, status, code, title, status == 503 || code == "SESSION_EXPIRED");
        return Results.Empty;
    }
}
