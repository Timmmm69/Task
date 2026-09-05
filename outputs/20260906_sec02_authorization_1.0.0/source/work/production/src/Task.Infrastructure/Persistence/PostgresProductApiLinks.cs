using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private (Resource Resource, JsonObject Object) RequireAny(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid id)
    {
        foreach (var resource in Resources.Values)
        {
            if (!r.Permissions.Contains(resource.ReadPermission)) continue;
            var row = One(c, t, $"SELECT to_jsonb(p)||to_jsonb(o) FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                $"WHERE o.organization_id=@org AND o.id=@id AND {Visibility(resource)}" + (r.Route.Method == "GET" ? ";" : " FOR UPDATE OF o;"), r, ("id", id));
            if (row is not null) return (resource, row);
        }
        throw Error(404, "OBJECT_NOT_VISIBLE", "Related object is not visible.");
    }

    private ProductApiResponse Links(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var parent = RequireAny(c, t, r, r.Id!.Value);
        if (r.Route.Operation == "links")
        {
            ValidateQuery(r, "type limit page cursor");
            var limit = QueryInt(r, "limit", 50, 1, 200);
            var (after, _) = ReadCursor(r);
            var readable = Resources.Values.Where(resource => r.Permissions.Contains(resource.ReadPermission)).Select(resource =>
                $"SELECT o.id FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                $"WHERE o.organization_id=@org AND o.lifecycle_state<>'trashed' AND {Visibility(resource)}");
            var rows = Many(c, t, "WITH visible AS (" + string.Join(" UNION ALL ", readable) + ") SELECT to_jsonb(l) FROM core.object_links l " +
                "JOIN visible s ON s.id=l.source_object_id JOIN visible d ON d.id=l.target_object_id " +
                "WHERE l.organization_id=@org AND (l.source_object_id=@id OR l.target_object_id=@id) AND (@type='' OR l.link_type=@type) " +
                "AND (@after='' OR l.id>NULLIF(@after,'')::uuid) ORDER BY l.id LIMIT @limit OFFSET @offset;", r,
                ("id", r.Id), ("type", r.Query.GetValueOrDefault("type", "")), ("after", after), ("limit", limit + 1),
                ("offset", (QueryInt(r, "page", 1, 1, 10000) - 1) * limit));
            var more = rows.Count > limit;
            if (more) rows.RemoveAt(limit);
            return new(new JsonObject
            {
                ["items"] = new JsonArray(rows.ToArray<JsonNode?>()),
                ["hasMore"] = more,
                ["nextCursor"] = more ? Cursor(r, rows[^1]["id"]!.ToString()) : null
            }, Version: Version(parent.Object));
        }
        if (r.ExpectedVersion != Version(parent.Object)) throw Error(412, "VERSION_CONFLICT", "Object version has changed.");
        if (Text(parent.Object, "lifecycleState") != "active") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the object before changing links.");
        CheckLinkWrite(c, t, r, parent);
        JsonObject? result = null;
        if (r.Route.Operation == "link-remove")
        {
            ValidateFields(r.Body, "");
            if (Run(c, t, "DELETE FROM core.object_links WHERE organization_id=@org AND source_object_id=@id AND id=@child;", r,
                ("id", r.Id), ("child", r.ChildId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Link is not visible.");
        }
        else
        {
            ValidateFields(r.Body, "sourceObjectId targetObjectId linkType");
            if (GuidValue(r.Body, "sourceObjectId") != r.Id) throw Invalid("sourceObjectId must match the route.");
            var target = RequireAny(c, t, r, GuidValue(r.Body, "targetObjectId"));
            if (Text(target.Object, "lifecycleState") != "active") throw Invalid("Link target must be active.");
            var type = Text(r.Body, "linkType");
            var pair = (parent.Resource.Type, target.Resource.Type);
            var valid = type switch
            {
                "related" => true,
                "task_file" => pair is ("task", "catalog_item"),
                "project_file" => pair is ("project", "catalog_item"),
                "contact_file" => pair is ("contact" or "company", "catalog_item"),
                "task_contact" => pair is ("task", "contact" or "company"),
                "project_contact" => pair is ("project", "contact" or "company"),
                "task_project" => pair is ("task", "project"),
                "parent_reference" => false, // Catalog parenting uses the cycle-checked move endpoint.
                _ => false,
            };
            if (!valid) throw Invalid("Link type is incompatible with the object types.");
            if (type is "project_file" or "project_contact" or "task_file" or "task_contact" or "contact_file")
                CheckObjectAction(c, t, r, GuidValue(target.Object, "id"), target.Resource.Type is "contact" or "company" ? "Contact.Update" : "FileCatalog.Update");
            result = (JsonObject)r.Body.DeepClone(); result["organizationId"] = r.OrganizationId;
            result["id"] = Guid.NewGuid(); result["createdBy"] = r.UserId;
            Insert(c, t, r, "core.object_links", result);
        }
        var updated = Bump(c, t, r, parent.Resource, parent.Object);
        return new(result ?? updated, r.Route.Operation == "link-add" ? 201 : 200, Version(updated));
    }

    private static void CheckLinkWrite(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, (Resource Resource, JsonObject Object) parent)
    {
        if (parent.Resource.Type == "task" && Text(parent.Object, "status") is "completed" or "cancelled")
            throw Error(409, "INVALID_STATE_TRANSITION", "Task is closed.");
        var permission = parent.Resource.Type switch
        {
            "project" => "Project.Update",
            "contact" or "company" => "Contact.Update",
            "catalog_item" => "FileCatalog.Update",
            "task" => r.Route.Operation == "link-add" ? "ObjectLink.Create" : "ObjectLink.Delete",
            "calendar_event" => "CalendarEvent.Update",
            "interaction" => "Interaction.Update",
            _ => ""
        };
        if (!r.Permissions.Contains(permission)) throw Error(403, "FORBIDDEN", "Source object update permission is required.");
        CheckObjectAction(c, t, r, GuidValue(parent.Object, "id"), permission);
        if (parent.Resource.Type == "project") CheckProjectWrite(c, t, r with { Route = r.Route with { Permission = permission } }, parent.Object);
    }

    private ProductApiResponse FileCheck(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        ValidateFields(r.Body, "deviceId status latencyMs osErrorCode checkedAt expectedLocationVersion");
        var device = SessionDevice(c, t, r);
        if (GuidValue(r.Body, "deviceId") != device) throw Error(403, "FORBIDDEN", "Device must match the session.");
        var location = One(c, t, "SELECT to_jsonb(p) FROM files.file_locations p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.catalog_item_id " +
            "WHERE p.organization_id=@org AND p.id=@id AND o.lifecycle_state='active' AND p.is_enabled;", r, ("id", r.Id))
            ?? throw Error(404, "OBJECT_NOT_VISIBLE", "Location is not visible.");
        RequireObject(c, t, r with { Id = GuidValue(location, "catalogItemId") }, Resources["catalog-items"]);
        if (Text(location, "locationType") != "unc_path" && (GuidValue(location, "ownerUserId") != r.UserId || location["deviceId"]?.ToString() != device.ToString()))
            throw Error(403, "FORBIDDEN", "Location belongs to another device.");
        if (r.Body["expectedLocationVersion"]?.GetValue<int>() != Version(location)) throw Error(412, "VERSION_CONFLICT", "Location version has changed.");
        if (!DateTimeOffset.TryParse(Text(r.Body, "checkedAt"), out var checkedAt) || checkedAt > DateTimeOffset.UtcNow.AddMinutes(5) || checkedAt < DateTimeOffset.UtcNow.AddDays(-1))
            throw Invalid("Check timestamp is outside the accepted window.");
        var body = (JsonObject)r.Body.DeepClone(); body.Remove("expectedLocationVersion");
        body["organizationId"] = r.OrganizationId; body["locationId"] = r.Id; body["locationVersion"] = Version(location);
        Run(c, t, "INSERT INTO files.location_checks SELECT * FROM jsonb_populate_record(NULL::files.location_checks,@payload::jsonb) " +
            "ON CONFLICT(organization_id,location_id,device_id) DO UPDATE SET location_version=EXCLUDED.location_version,status=EXCLUDED.status," +
            "checked_at=EXCLUDED.checked_at,latency_ms=EXCLUDED.latency_ms,os_error_code=EXCLUDED.os_error_code WHERE files.location_checks.checked_at<=EXCLUDED.checked_at;",
            r, ("payload", ToDatabase(body).ToJsonString()));
        return new(null, 204, Version(location));
    }
}
