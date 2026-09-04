using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Api.Security;
using Task.Application.ProductData;
using Task.Application.Security;

namespace Task.Api.ProductData;

internal static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var route in ProductApiRoutes.All)
            app.MapMethods(route.Path, [route.Method], (Delegate)((HttpContext context) => HandleAsync(context, route)))
                .RequireAuthorization();
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
            var granted = new HashSet<string>(StringComparer.Ordinal);
            async System.Threading.Tasks.Task Check(string code)
            {
                if ((await permissions.EvaluateAsync(identity.OrganizationId, identity.UserAccountId,
                    code.ToLowerInvariant(), context.RequestAborted)).Allowed) granted.Add(code);
            }
            await Check(route.Permission);
            if (!granted.Contains(route.Permission)) throw new ProductApiException(403, "FORBIDDEN", "Permission denied.");
            await Check("organization.manage");
            if (route.Resource is "search" or "archive" or "trash" or "objects" or "interactions")
                foreach (var code in new[] { "Project.Read", "Contact.Read", "FileCatalog.Read", "Task.Read", "Calendar.Read", "Employee.Read" })
                    await Check(code);
            if (route.Operation is "locations" or "resolve" || route.Resource is "network-resources" or "search") await Check("FileLocation.ReadSensitivePath");
            if (route.Resource == "search") await Check("FileReference.Open");
            if (route.Resource == "objects" && route.Method != "GET")
                foreach (var code in new[] { "Project.Update", "Contact.Update", "FileCatalog.Update", "Task.Update", "CalendarEvent.Update", "Interaction.Update" })
                    await Check(code);

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
            using var buffer = new MemoryStream();
            var maximumBodyBytes = context.RequestServices.GetService<IProductSettingsStore>()?
                .GetOrganization(identity.OrganizationId)?.MaxRequestBytes ?? 1_048_576;
            var chunk = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (buffer.Length + count > maximumBodyBytes) throw new ProductApiException(413, "PAYLOAD_TOO_LARGE", "Body exceeds the request limit.");
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            JsonObject body = new();
            if (bytes.Length > 0)
            {
                if (!context.Request.HasJsonContentType()) throw new ProductApiException(415, "UNSUPPORTED_MEDIA_TYPE", "Use application/json.");
                using var document = JsonDocument.Parse(bytes);
                ValidateJson(document.RootElement);
                body = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException();
            }
            if (route.Method is "POST" or "PUT" or "PATCH" && route.Operation is "create" or "patch" && body.Count == 0)
                throw new ProductApiException(422, "VALIDATION_FAILED", "At least one writable field is required.");
            var query = context.Request.Query.ToDictionary(p => p.Key, p => p.Value.ToString());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                route.Method + context.Request.Path + context.Request.QueryString + "|" + version + "|" + body.ToJsonString())));
            var correlation = Guid.TryParse(context.Response.Headers["X-Correlation-ID"], out var parsed) ? parsed : Guid.NewGuid();
            var response = store.Execute(new(identity.OrganizationId, identity.UserAccountId, identity.SessionId,
                correlation, route, id, childId, body, query, version, key.Length == 0 ? null : key, hash, granted, context.RequestAborted));
            if (response.Version is { } responseVersion) context.Response.Headers.ETag = $"\"v{responseVersion}\"";
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
        await TaskApiProblemResponse.WriteAsync(context, status, code, title, status == 503);
        return Results.Empty;
    }
}
