using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application.Security;
using Task.Domain;

namespace Task.Api.Tasks;

internal static partial class TaskEndpoints
{
    private static string? ReadCardPatch(JsonElement root)
    {
        var patch = new JsonObject();
        foreach (var property in root.EnumerateObject())
            if (TaskCardContent.Fields.Contains(property.Name)) patch[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return patch.Count == 0 ? null : patch.ToJsonString();
    }

    private static async System.Threading.Tasks.Task<bool> CanWriteCard(HttpContext context, AuthenticatedRequestContext identity, string body)
    {
        using var document = JsonDocument.Parse(body);
        var service = context.RequestServices.GetService<PermissionDecisionService>();
        foreach (var (field, permission) in new[] { ("assigneeIds", "task.assign"), ("watcherIds", "task.watch"),
                     ("projectId", "project.read"), ("primaryCounterpartyObjectId", "contact.read"), ("parentTaskId", "task.read") })
        {
            if (!document.RootElement.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            if (context.Request.Method == "POST" && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) continue;
            if (service is null || !(await service.EvaluateAsync(identity.OrganizationId, identity.UserAccountId, permission, context.RequestAborted)).Allowed)
                return false;
        }
        return true;
    }
}
