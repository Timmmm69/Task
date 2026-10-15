using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse Inbox(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var resource = Resources["inbox-items"];
        if (r.Route.Operation == "list")
        {
            var query = r.Query.ToDictionary(pair => pair.Key, pair => pair.Value);
            query.TryAdd("status", "unprocessed");
            if (query["status"] is not ("unprocessed" or "converted" or "discarded")) throw Invalid("Invalid inbox status.");
            var page = List(c, t, r with { Query = query }, resource);
            foreach (var item in page.Body!["items"]!.AsArray()) RedactInbox(item!.AsObject());
            return page;
        }
        if (r.Route.Operation == "create")
        {
            var body = (JsonObject)r.Body.DeepClone(); ValidateInbox(body, create: true);
            var id = Guid.NewGuid();
            Run(c, t, "INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES(@id,@org,'inbox_item',@user,@user,statement_timestamp(),statement_timestamp());", r, ("id", id));
            body["id"] = id; body["organizationId"] = r.OrganizationId; body["ownerUserId"] = r.UserId;
            Insert(c, t, r, resource.Table, body);
            var created = RequireObject(c, t, r with { Id = id }, resource);
            Record(c, t, r, "inbox_item", id, 1, null, created);
            return new(RedactInbox(created), 201, 1);
        }
        var current = RequireObject(c, t, r, resource);
        if (Text(current, "lifecycleState") != "active") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the inbox item before editing.");
        if (r.Route.Operation == "get") return new(RedactInbox(current), Version: Version(current));
        if (r.ExpectedVersion != Version(current)) throw Error(412, "VERSION_CONFLICT", "Inbox item has changed.");
        if (Text(current, "status") != "unprocessed") throw Error(409, "INVALID_STATE_TRANSITION", "Inbox item has already been processed.");
        if (r.Route.Operation == "patch")
        {
            var body = (JsonObject)r.Body.DeepClone(); ValidateInbox(body, create: false);
            var merged = (JsonObject)current.DeepClone(); foreach (var pair in body) merged[pair.Key] = pair.Value?.DeepClone();
            ValidateInboxContent(merged);
            Update(c, t, r, resource.Table, body, r.Id!.Value);
            AdvanceInbox(c, t, r);
            var updated = RequireObject(c, t, r, resource);
            Record(c, t, r, "inbox_item", r.Id.Value, Version(updated), current, updated);
            return new(RedactInbox(updated), Version: Version(updated));
        }
        if (!r.Permissions.Contains("Task.Read")) throw Error(403, "FORBIDDEN", "Task read access is required.");
        var allowed = new[] { "projectId", "scheduledDate", "expectedInboxVersion" };
        if (r.Body.Any(pair => !allowed.Contains(pair.Key)) || r.Body["expectedInboxVersion"]?.GetValue<int>() != r.ExpectedVersion)
            throw Invalid("expectedInboxVersion must match If-Match.");
        var card = new JsonObject();
        if (Text(current, "content") is { Length: > 0 } content) card["description"] = content;
        if (r.Body["projectId"] is not null)
        {
            var project = RequireObject(c, t, r with { Id = GuidValue(r.Body, "projectId") }, Resources["projects"]);
            if (!r.Permissions.Contains("Project.Read") || Text(project, "lifecycleState") != "active") throw Error(403, "FORBIDDEN", "Project access is required.");
            card["projectId"] = r.Body["projectId"]!.DeepClone();
        }
        if (r.Body["scheduledDate"] is not null)
        {
            if (!DateOnly.TryParseExact(Text(r.Body, "scheduledDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw Invalid("Invalid scheduled date.");
            card["scheduledDate"] = r.Body["scheduledDate"]!.DeepClone();
        }
        Guid taskId;
        if (current["legacyTaskId"] is not null)
        {
            taskId = GuidValue(current, "legacyTaskId");
            var existing = RequireObject(c, t, r with { Id = taskId }, Resources["tasks"]);
            if (Text(existing, "lifecycleState") != "active") throw Error(409, "INVALID_STATE_TRANSITION", "The task is not active.");
            if (r.Body["projectId"] is not null || r.Body["scheduledDate"] is not null)
            {
                if (!r.Permissions.Contains("Task.Update")) throw Error(403, "FORBIDDEN", "Task update access is required.");
                CheckObjectAction(c, t, r, taskId, "Task.Update");
                var merged = existing["cardContent"]!.AsObject();
                foreach (var field in card.Where(field => field.Key is "projectId" or "scheduledDate")) merged[field.Key] = field.Value?.DeepClone();
                Run(c, t, "UPDATE work.tasks SET card_content=@card::jsonb WHERE organization_id=@org AND id=@id;", r, ("id", taskId), ("card", merged.ToJsonString()));
                AdvanceInbox(c, t, r with { Id = taskId });
                var updated = RequireObject(c, t, r with { Id = taskId }, Resources["tasks"]);
                Record(c, t, r with { Route = r.Route with { Operation = "patch" } }, "task", taskId, Version(updated), existing, updated);
            }
        }
        else
        {
            var title = Text(current, "title");
            if (string.IsNullOrWhiteSpace(title)) title = Text(current, "content");
            if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 500) throw Invalid("A task title between 1 and 500 characters is required.");
            taskId = Guid.NewGuid();
            Run(c, t, "INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES(@id,@org,'task',@user,@user,statement_timestamp(),statement_timestamp());", r, ("id", taskId));
            Run(c, t, "INSERT INTO work.tasks(id,organization_id,title,card_content) VALUES(@id,@org,@title,@card::jsonb);", r, ("id", taskId), ("title", title.Trim()), ("card", card.ToJsonString()));
            var created = RequireObject(c, t, r with { Id = taskId }, Resources["tasks"]);
            Record(c, t, r with { Route = r.Route with { Operation = "create" } }, "task", taskId, 1, null, created);
        }
        Run(c, t, "UPDATE work.inbox_items SET status='converted',converted_object_id=@task,processed_at=statement_timestamp() WHERE organization_id=@org AND id=@id;", r, ("id", r.Id), ("task", taskId));
        AdvanceInbox(c, t, r);
        var converted = RequireObject(c, t, r, resource);
        Record(c, t, r, "inbox_item", r.Id!.Value, Version(converted), current, converted);
        var task = RequireObject(c, t, r with { Id = taskId }, Resources["tasks"]);
        var taskCard = task["cardContent"]?.AsObject() ?? new JsonObject();
        foreach (var field in taskCard) task[field.Key] = field.Value?.DeepClone();
        task.Remove("cardContent"); task["authorUserId"] = task["createdBy"]?.DeepClone();
        task["assigneeIds"] ??= new JsonArray(); task["watcherIds"] ??= new JsonArray();
        return new(task, 201, Version(task));
    }

    private static void AdvanceInbox(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r) =>
        Run(c, t, "UPDATE core.objects SET version=version+1,updated_at=statement_timestamp(),updated_by=@user WHERE organization_id=@org AND id=@id;", r, ("id", r.Id));
    private static JsonObject RedactInbox(JsonObject item)
    {
        var fields = "id organizationId version createdAt updatedAt ownerUserId itemType title content rawUrl rawPath status convertedObjectId".Split(' ').ToHashSet();
        foreach (var key in item.Select(pair => pair.Key).Where(key => !fields.Contains(key)).ToArray()) item.Remove(key);
        return item;
    }
    private static void ValidateInbox(JsonObject body, bool create)
    {
        var fields = new Dictionary<string, int> { ["itemType"] = 20, ["title"] = 500, ["content"] = 20000, ["rawUrl"] = 2048, ["rawPath"] = 4096, ["status"] = 20 };
        if (body.Count == 0 || body.Any(pair => !fields.ContainsKey(pair.Key))) throw Invalid("Invalid inbox fields.");
        foreach (var pair in body)
            if (pair.Value is not null && (pair.Value.GetValueKind() != JsonValueKind.String || pair.Value.GetValue<string>().Length > fields[pair.Key])) throw Invalid("Invalid inbox field value.");
        if (create && Text(body, "itemType") is null || body.ContainsKey("itemType") && Text(body, "itemType") is not ("task" or "note" or "file_link" or "web_link" or "idea" or "assignment")) throw Invalid("Invalid inbox type.");
        if (body.ContainsKey("status") && Text(body, "status") is not ("unprocessed" or "discarded")) throw Invalid("Use conversion to set converted status.");
        if (create) ValidateInboxContent(body);
    }
    private static void ValidateInboxContent(JsonObject body)
    {
        if (new[] { "title", "content", "rawUrl", "rawPath" }.All(key => string.IsNullOrWhiteSpace(Text(body, key)))) throw Invalid("Inbox content is required.");
    }
}
