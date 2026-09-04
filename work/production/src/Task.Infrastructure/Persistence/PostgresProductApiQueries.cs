using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse List(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource)
    {
        ValidateQuery(r, "limit page cursor q lifecycle status parentId depth includeArchived sort");
        var limit = QueryInt(r, "limit", 50, 1, 200);
        var page = QueryInt(r, "page", 1, 1, 10000);
        if (r.Query.TryGetValue("sort", out var sort) && sort != "id") throw Invalid("Supported sort is id.");
        var lifecycle = r.Query.GetValueOrDefault("lifecycle", "active");
        if (lifecycle is not ("active" or "archived" or "trashed")) throw Invalid("Invalid lifecycle filter.");
        if (r.Query.GetValueOrDefault("includeArchived") == "true") lifecycle = "all";
        else if (r.Query.ContainsKey("includeArchived") && r.Query["includeArchived"] != "false") throw Invalid("Invalid includeArchived filter.");
        var (after, _) = ReadCursor(r);
        var clauses = new List<string> { "o.organization_id=@org", Visibility(resource),
            "(o.lifecycle_state=@state OR (@state='all' AND o.lifecycle_state<>'trashed'))", "(@after='' OR o.id>nullif(@after,'')::uuid)" };
        var args = new List<(string, object?)> { ("state", lifecycle), ("after", after), ("limit", limit + 1), ("offset", (page - 1) * limit) };
        if (r.Query.TryGetValue("q", out var q))
        {
            if (q.Length is < 1 or > 200) throw Invalid("Search text must contain 1-200 characters.");
            clauses.Add("strpos(lower(" + SearchText(resource) + "),lower(@q))>0"); args.Add(("q", q));
        }
        if (r.Query.TryGetValue("status", out var status))
        { clauses.Add("to_jsonb(p)->>'status'=@status"); args.Add(("status", status)); }
        if (r.Route.Resource == "notifications") clauses.Add("p.not_before<=statement_timestamp() AND (p.expires_at IS NULL OR p.expires_at>statement_timestamp())");
        if (r.Query.TryGetValue("parentId", out var parent))
        {
            if (resource.Type != "catalog_item") throw Invalid("parentId applies only to catalog items.");
            if (!Guid.TryParse(parent, out var parentId)) throw Invalid("Invalid parentId.");
            clauses.Add("p.parent_item_id=@parent"); args.Add(("parent", parentId));
        }
        else if (r.Route.Operation == "tree") clauses.Add("p.parent_item_id IS NULL");
        var rows = Many(c, t, $"SELECT to_jsonb(p)||to_jsonb(o) FROM {resource.Table} p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id " +
            $"WHERE {string.Join(" AND ", clauses)} ORDER BY o.id LIMIT @limit OFFSET @offset;", r, args.ToArray());
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(limit);
        if (r.Route.Operation == "tree")
        {
            var depth = QueryInt(r, "depth", 1, 1, 8);
            var remaining = 1000 - rows.Count;
            foreach (var row in rows) Expand(row, depth - 1);
            void Expand(JsonObject row, int levels)
            {
                if (levels == 0 || Text(row, "itemType") != "virtual_folder") return;
                var children = Many(c, t, "SELECT to_jsonb(p)||to_jsonb(o) FROM files.catalog_items p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                    "WHERE p.organization_id=@org AND p.parent_item_id=@parent AND (o.lifecycle_state=@state OR (@state='all' AND o.lifecycle_state<>'trashed')) ORDER BY p.sort_order,p.id LIMIT @limit;",
                    r, ("parent", GuidValue(row, "id")), ("state", lifecycle), ("limit", remaining + 1));
                if (children.Count > remaining) throw Invalid("Tree is too large; reduce depth or select a parent.");
                remaining -= children.Count;
                foreach (var child in children) Expand(child, levels - 1);
                row["children"] = new JsonArray(children.ToArray<JsonNode?>());
            }
        }
        return new(new JsonObject
        {
            ["items"] = new JsonArray(rows.ToArray<JsonNode?>()),
            ["hasMore"] = more,
            ["nextCursor"] = more ? Cursor(r, rows[^1]["id"]!.ToString()) : null
        });
    }

    private ProductApiResponse Discovery(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        if (r.Route.Operation is "restore" or "unarchive")
        {
            foreach (var (name, resource) in Resources)
            {
                if (resource.Type is "employee_profile" or "notification" or "network_resource") continue;
                if (!r.Permissions.Contains(resource.ReadPermission)) continue;
                var found = One(c, t, $"SELECT jsonb_build_object('id',o.id) FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                    $"WHERE o.organization_id=@org AND o.id=@id AND {Visibility(resource)};", r, ("id", r.Id));
                if (found is null) continue;
                var redirected = r with { Route = r.Route with { Resource = name } };
                var current = RequireObject(c, t, redirected, resource);
                if (Version(current) != r.ExpectedVersion) throw Error(412, "VERSION_CONFLICT", "Object version has changed.");
                if (resource.Type == "project") CheckProjectWrite(c, t, r, current);
                return Change(c, t, redirected, resource, current);
            }
            throw Error(404, "OBJECT_NOT_VISIBLE", "Object is not visible.");
        }
        ValidateQuery(r, "q types type lifecycle limit page cursor projectId projectIds status from to deletedBy purgeBefore");
        var limit = QueryInt(r, "limit", 50, 1, 200);
        var page = QueryInt(r, "page", 1, 1, 10000);
        var lifecycle = r.Route.Resource switch { "archive" => "archived", "trash" => "trashed", _ => r.Query.GetValueOrDefault("lifecycle", "active") };
        if (lifecycle is not ("active" or "archived" or "trashed")) throw Invalid("Invalid lifecycle.");
        var q = r.Query.GetValueOrDefault("q", "").Trim();
        if (r.Route.Resource == "search" && q.Length is < 1 or > 200) throw Invalid("Search text must contain 1-200 characters.");
        var types = r.Query.GetValueOrDefault("types", r.Query.GetValueOrDefault("type", "")).Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (types.Any(type => !Resources.Values.Any(resource => resource.Type == type))) throw Invalid("Unknown object type.");
        var (after, _) = ReadCursor(r);
        var union = new List<string>();
        foreach (var resource in Resources.Values)
        {
            if (resource.Type is "notification" or "network_resource" or "employee_profile" || !r.Permissions.Contains(resource.ReadPermission) ||
                (types.Length > 0 && !types.Contains(resource.Type))) continue;
            var extra = "";
            if (r.Query.TryGetValue("projectId", out var project) || r.Query.TryGetValue("projectIds", out project))
            {
                if (!Guid.TryParse(project, out _)) throw Invalid("Specify one project UUID.");
                extra += resource.Type == "project" ? " AND p.id::text=@project" : resource.Type is "task" or "calendar_event" ? " AND to_jsonb(p)->>'project_id'=@project" : " AND FALSE";
            }
            if (r.Query.ContainsKey("status")) extra += " AND to_jsonb(p)->>'status'=@status";
            if (r.Query.ContainsKey("from")) extra += " AND o.updated_at>=@from::timestamptz";
            if (r.Query.ContainsKey("to")) extra += " AND o.updated_at<=@to::timestamptz";
            if (r.Query.ContainsKey("deletedBy")) extra += " AND o.deleted_by=@deletedBy::uuid";
            if (r.Query.ContainsKey("purgeBefore")) extra += " AND EXISTS (SELECT 1 FROM governance.trash_entries te WHERE te.organization_id=@org AND te.object_id=o.id AND te.status='retained' AND te.purge_after<=@purgeBefore::timestamptz)";
            var title = resource.Type == "contact" ? "p.display_name" : resource.Type == "interaction" ? "p.subject" : resource.Type is "task" or "calendar_event" ? "p.title" : "p.name";
            var ledger = r.Route.Resource == "archive" ? "governance.archive_entries" : "governance.trash_entries";
            var ledgerStatus = r.Route.Resource == "archive" ? "archived" : "retained";
            union.Add($"SELECT o.id,jsonb_build_object('objectId',o.id,'objectType',o.object_type,'title',{title},'version',o.version,'lifecycleState',o.lifecycle_state,'updatedAt',o.updated_at) || " +
                $"COALESCE((SELECT to_jsonb(e)-'object_id'-'object_type' FROM {ledger} e WHERE e.organization_id=@org AND e.object_id=o.id AND e.status='{ledgerStatus}'),'{{}}'::jsonb) AS result " +
                $"FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id WHERE o.organization_id=@org AND o.lifecycle_state=@state AND {Visibility(resource)} " +
                $"AND (@q='' OR strpos(lower({SearchText(resource)}),lower(@q))>0){extra}");
        }
        if (union.Count == 0) return new(new JsonObject { ["items"] = new JsonArray(), ["hasMore"] = false, ["nextCursor"] = null });
        var args = new List<(string, object?)> { ("state", lifecycle), ("q", q), ("after", after), ("limit", limit + 1), ("offset", (page - 1) * limit) };
        foreach (var name in new[] { "status", "from", "to", "deletedBy", "purgeBefore" })
            if (r.Query.TryGetValue(name, out var value)) args.Add((name, value));
        if (r.Query.TryGetValue("projectId", out var projectId) || r.Query.TryGetValue("projectIds", out projectId)) args.Add(("project", projectId));
        var rows = Many(c, t, "SELECT result FROM (" + string.Join(" UNION ALL ", union) + ") matched WHERE (@after='' OR id>nullif(@after,'')::uuid) ORDER BY id LIMIT @limit OFFSET @offset;", r, args.ToArray());
        var more = rows.Count > limit; if (more) rows.RemoveAt(limit);
        return new(new JsonObject
        {
            ["items"] = new JsonArray(rows.ToArray<JsonNode?>()),
            ["hasMore"] = more,
            ["nextCursor"] = more ? Cursor(r, rows[^1]["objectId"]!.ToString()) : null
        });
    }

    private static string SearchText(Resource resource) => resource.Type switch
    {
        "project" or "company" => "concat_ws(' ',p.name,to_jsonb(p)->>'description',to_jsonb(p)->>'notes')",
        "contact" => "concat_ws(' ',p.first_name,p.last_name,p.middle_name,p.display_name,p.notes)",
        "catalog_item" => "concat_ws(' ',p.name,p.description,p.note_content)",
        "task" or "calendar_event" => "concat_ws(' ',p.title,to_jsonb(p)->>'description')",
        "notification" => "concat_ws(' ',p.title,p.body)",
        "interaction" => "concat_ws(' ',p.subject,p.details,p.next_step)",
        "employee_profile" => "concat_ws(' ',p.first_name,p.last_name,p.display_name)",
        _ => "p.name",
    };

    private static void Enrich(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject row)
    {
        if (r.Route.Resource == "contacts")
        {
            row["channels"] = new JsonArray(Many(c, t, "SELECT to_jsonb(p) FROM crm.communication_channels p WHERE organization_id=@org AND owner_object_id=@id ORDER BY id;", r, ("id", r.Id)).ToArray<JsonNode?>());
            row["addresses"] = new JsonArray(Many(c, t, "SELECT to_jsonb(p) FROM crm.addresses p WHERE organization_id=@org AND owner_object_id=@id ORDER BY id;", r, ("id", r.Id)).ToArray<JsonNode?>());
        }
        if (r.Route.Resource == "companies")
            row["contacts"] = new JsonArray(Many(c, t, "SELECT to_jsonb(p) FROM crm.company_contacts p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.contact_id " +
                "WHERE p.organization_id=@org AND p.company_id=@id AND o.lifecycle_state='active' ORDER BY contact_id;", r, ("id", r.Id)).ToArray<JsonNode?>());
    }

    private static int QueryInt(ProductApiRequest r, string name, int fallback, int min, int max) =>
        !r.Query.TryGetValue(name, out var raw) ? fallback : int.TryParse(raw, out var value) && value >= min && value <= max ? value : throw Invalid("Invalid " + name + ".");
    private static void ValidateQuery(ProductApiRequest r, string fields)
    {
        var allowed = fields.Split(' ').ToHashSet();
        if (r.Query.Keys.Any(key => !allowed.Contains(key))) throw Invalid("Unsupported query filter.");
        if (r.Query.ContainsKey("cursor") && r.Query.ContainsKey("page")) throw Invalid("Use cursor or page, not both.");
    }
    private static string CursorScope(ProductApiRequest r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        r.OrganizationId + ":" + r.UserId + ":" + r.Route.Path + ":" + r.Id + ":" + string.Join(',', r.Permissions.Order()) + ":" +
        string.Join('&', r.Query.Where(p => p.Key != "cursor").OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)))));
    private static string Cursor(ProductApiRequest r, string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
        new JsonObject { ["id"] = id, ["scope"] = CursorScope(r), ["expires"] = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds() }.ToJsonString()));
    private static (string Id, long Expires) ReadCursor(ProductApiRequest r)
    {
        if (!r.Query.TryGetValue("cursor", out var cursor)) return ("", 0);
        try
        {
            if (cursor.Length > 2048) throw Invalid("Cursor is too long.");
            var parsed = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)))!.AsObject();
            if (Text(parsed, "scope") != CursorScope(r)) throw Invalid("Cursor does not match the request scope.");
            if (parsed["expires"]!.GetValue<long>() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw Error(410, "CURSOR_EXPIRED", "Cursor has expired.");
            return (GuidValue(parsed, "id").ToString(), parsed["expires"]!.GetValue<long>());
        }
        catch (Exception exception) when (exception is not ProductApiException) { throw Invalid("Invalid cursor."); }
    }
}
