using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private static string RequiredTaskText(JsonObject body, string field, int maximum)
    {
        var value = Text(body, field)?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > maximum)
            throw Invalid(field + " is required and must fit the length limit.");
        return value;
    }

    private ProductApiResponse TaskOptions(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        ValidateQuery(r, "q limit");
        var query = r.Query.GetValueOrDefault("q", "").Trim();
        if (query.Length > 200) throw Invalid("Search text is too long.");
        var limit = QueryInt(r, "limit", 200, 1, 200);
        var body = new JsonObject();
        foreach (var (key, sql, permission) in new[] {
            ("people", "SELECT u.id,e.display_name AS name FROM iam.user_accounts u JOIN org.employee_profiles e ON e.id=u.employee_profile_id AND e.organization_id=u.organization_id JOIN core.objects o ON o.id=u.id WHERE u.organization_id=@org AND u.account_status='active' AND o.lifecycle_state='active'", "Employee.Read"),
            ("projects", "SELECT p.id,p.name FROM projects.projects p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND o.lifecycle_state='active' AND work.task_project_visible(@org,p.id,@user)", "Project.Read"),
            ("counterparties", "SELECT p.id,p.display_name AS name FROM crm.contacts p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND o.lifecycle_state='active' UNION ALL SELECT p.id,p.name FROM crm.companies p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND o.lifecycle_state='active'", "Contact.Read"),
            ("tasks", "SELECT p.id,p.title AS name FROM work.tasks p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND o.lifecycle_state='active' AND work.task_visible(@org,p.id,@user)", "Task.Read"),
            ("files", "SELECT p.id,p.name FROM files.catalog_items p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND o.lifecycle_state='active'", "FileCatalog.Read") })
        {
            var rows = r.Permissions.Contains(permission) ? Many(c, t,
                "SELECT to_jsonb(options) FROM (" + sql + ") options WHERE name ILIKE @q ORDER BY name,id LIMIT @limit;", r,
                ("q", "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"), ("limit", limit + 1)) : [];
            body[key + "HasMore"] = rows.Count > limit;
            body[key] = new JsonArray(rows.Take(limit).ToArray<JsonNode?>());
        }
        return new(body);
    }

    private ProductApiResponse TaskWorkspace(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        if (r.Route.Operation == "task-options") return TaskOptions(c, t, r);
        var resource = Resources["tasks"];
        var current = RequireObject(c, t, r, resource);
        if (Text(current, "lifecycleState") != "active") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the task first.");
        if (r.Route.Operation == "task-workspace")
        {
            ValidateQuery(r, "");
            JsonArray Rows(string sql) => new(Many(c, t, sql, r, ("id", r.Id)).ToArray<JsonNode?>());
            var body = new JsonObject
            {
                ["checklist"] = Rows("SELECT to_jsonb(i) FROM work.task_checklist i WHERE organization_id=@org AND task_id=@id ORDER BY sort_order,id;"),
                ["comments"] = Rows("SELECT to_jsonb(i)||jsonb_build_object('authorName',e.display_name) FROM work.task_comments i JOIN iam.user_accounts u ON u.organization_id=i.organization_id AND u.id=i.author_user_id JOIN org.employee_profiles e ON e.organization_id=u.organization_id AND e.id=u.employee_profile_id WHERE i.organization_id=@org AND i.task_id=@id ORDER BY i.created_at DESC,i.id LIMIT 200;"),
                ["subtasks"] = Rows("SELECT jsonb_build_object('id',p.id,'name',p.title,'status',p.status) FROM work.tasks p JOIN core.objects o ON o.id=p.id WHERE p.organization_id=@org AND p.parent_task_id=@id AND o.lifecycle_state='active' AND work.task_visible(@org,p.id,@user) ORDER BY p.title,p.id;"),
                ["dependencies"] = Rows("SELECT jsonb_build_object('id',d.id,'targetId',p.id,'name',p.title,'status',p.status) FROM work.task_dependencies d JOIN work.tasks p ON p.organization_id=d.organization_id AND p.id=d.predecessor_id JOIN core.objects o ON o.id=p.id WHERE d.organization_id=@org AND d.task_id=@id AND o.lifecycle_state='active' AND work.task_visible(@org,p.id,@user) ORDER BY p.title,p.id;"),
                ["files"] = r.Permissions.Contains("FileCatalog.Read") ? Rows("SELECT jsonb_build_object('id',l.id,'targetId',p.id,'name',p.name) FROM core.object_links l JOIN files.catalog_items p ON p.organization_id=l.organization_id AND p.id=l.target_object_id JOIN core.objects o ON o.id=p.id WHERE l.organization_id=@org AND l.source_object_id=@id AND l.link_type='task_file' AND o.lifecycle_state='active' ORDER BY p.name,p.id;") : new JsonArray(),
                ["history"] = r.Permissions.Contains("History.Read") ? Rows("SELECT jsonb_build_object('id',id,'action',event_type,'version',aggregate_version,'occurredAt',occurred_at,'changedFields',changed_fields) FROM governance.domain_events WHERE organization_id=@org AND aggregate_id=@id ORDER BY occurred_at DESC,id LIMIT 200;") : new JsonArray(),
                ["filesVisible"] = r.Permissions.Contains("FileCatalog.Read"),
                ["historyVisible"] = r.Permissions.Contains("History.Read")
            };
            return new(body, Version: Version(current));
        }
        if (r.ExpectedVersion != Version(current)) throw Error(412, "VERSION_CONFLICT", "Task version has changed.");
        if (r.Route.Operation != "task-comment-add" && Text(current, "status") is "completed" or "cancelled") throw Error(409, "INVALID_STATE_TRANSITION", "Task is closed.");
        var id = Guid.NewGuid();
        switch (r.Route.Operation)
        {
            case "task-check-add":
                ValidateFields(r.Body, "text");
                Run(c, t, "INSERT INTO work.task_checklist(id,organization_id,task_id,text,sort_order,updated_by) VALUES(@child,@org,@id,@text,(SELECT COALESCE(MAX(sort_order),0)+1 FROM work.task_checklist WHERE organization_id=@org AND task_id=@id),@user);", r, ("child", id), ("id", r.Id), ("text", RequiredTaskText(r.Body, "text", 2000))); break;
            case "task-check-patch":
                ValidateFields(r.Body, "text isCompleted sortOrder");
                if (r.Body.Count == 0) throw Invalid("A checklist change is required.");
                var check = One(c, t, "SELECT to_jsonb(i) FROM work.task_checklist i WHERE organization_id=@org AND task_id=@id AND id=@child;", r, ("id", r.Id), ("child", r.ChildId)) ?? throw Error(404, "OBJECT_NOT_VISIBLE", "Checklist item is absent.");
                foreach (var (key, value) in r.Body) check[key] = value?.DeepClone();
                Run(c, t, "UPDATE work.task_checklist SET text=@text,is_completed=@done,sort_order=@sort,updated_by=@user,updated_at=statement_timestamp() WHERE organization_id=@org AND task_id=@id AND id=@child;", r, ("text", RequiredTaskText(check, "text", 2000)), ("done", check["isCompleted"]!.GetValue<bool>()), ("sort", check["sortOrder"]!.GetValue<int>()), ("id", r.Id), ("child", r.ChildId)); break;
            case "task-check-remove":
                ValidateFields(r.Body, "");
                if (Run(c, t, "DELETE FROM work.task_checklist WHERE organization_id=@org AND task_id=@id AND id=@child;", r, ("id", r.Id), ("child", r.ChildId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Checklist item is absent."); break;
            case "task-comment-add":
                ValidateFields(r.Body, "body");
                Run(c, t, "INSERT INTO work.task_comments(id,organization_id,task_id,body,author_user_id) VALUES(@child,@org,@id,@body,@user);", r, ("child", id), ("id", r.Id), ("body", RequiredTaskText(r.Body, "body", 50000))); break;
            case "task-dependency-add":
                ValidateFields(r.Body, "predecessorId");
                var predecessor = GuidValue(r.Body, "predecessorId");
                _ = RequireObject(c, t, r with { Id = predecessor }, resource);
                if (predecessor == r.Id || One(c, t, "WITH RECURSIVE chain(id) AS (SELECT predecessor_id FROM work.task_dependencies WHERE organization_id=@org AND task_id=@pre UNION SELECT d.predecessor_id FROM work.task_dependencies d JOIN chain ON chain.id=d.task_id WHERE d.organization_id=@org) SELECT jsonb_build_object('cycle',true) FROM chain WHERE id=@id LIMIT 1;", r, ("pre", predecessor), ("id", r.Id)) is not null) throw Invalid("Dependency would create a cycle.");
                Run(c, t, "INSERT INTO work.task_dependencies(id,organization_id,task_id,predecessor_id) VALUES(@child,@org,@id,@pre);", r, ("child", id), ("id", r.Id), ("pre", predecessor)); break;
            case "task-dependency-remove":
                ValidateFields(r.Body, "");
                if (Run(c, t, "DELETE FROM work.task_dependencies WHERE organization_id=@org AND task_id=@id AND id=@child;", r, ("id", r.Id), ("child", r.ChildId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Dependency is absent."); break;
            default: throw Invalid("Unsupported task operation.");
        }
        var updated = Bump(c, t, r, resource, current);
        return new(new JsonObject { ["id"] = id }, Version: Version(updated));
    }
}
