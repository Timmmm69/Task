using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse Search(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        ValidateQuery(r, "q types projectIds userIds departments contactIds hasFiles lifecycle from to cursor limit");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var q = r.Query.GetValueOrDefault("q", "").Trim();
        if (q.Length < (r.Route.Operation == "suggestions" ? 1 : 2) || q.Length > 200) throw Invalid("Invalid search text length.");
        var limit = QueryInt(r, "limit", 50, 1, 500);
        var normalized = r.Query.ToDictionary(p => p.Key, p => p.Value);
        normalized["q"] = q.ToLowerInvariant();
        foreach (var key in new[] { "types", "projectIds", "userIds", "departments", "contactIds", "lifecycle" })
            if (normalized.TryGetValue(key, out var value)) normalized[key] = string.Join(',', value.Split(',').Select(s => s.Trim()).Distinct().Order());
        r = r with { Query = normalized };
        var types = Values("types");
        var knownTypes = new[] { "task", "calendar_event", "project", "catalog_item", "file_location", "contact", "company", "interaction", "comment", "employee_profile" };
        if (types.Length > 10 || types.Any(type => !knownTypes.Contains(type))) throw Invalid("Unknown search type.");
        var states = Values("lifecycle");
        if (states.Any(state => state is not ("active" or "completed"))) throw Invalid("Search lifecycle must be active and/or completed.");
        foreach (var key in new[] { "projectIds", "userIds", "departments", "contactIds" })
            if (Values(key).Length > 100 || Values(key).Any(value => !Guid.TryParse(value, out var id) || id == Guid.Empty)) throw Invalid("Invalid UUID filter.");
        if (normalized.TryGetValue("hasFiles", out var hasFiles) && hasFiles is not ("true" or "false")) throw Invalid("hasFiles must be boolean.");
        foreach (var key in new[] { "from", "to" })
            if (normalized.TryGetValue(key, out var date) && (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) || !date.EndsWith('Z')))
                throw Invalid("Date filters require RFC3339 UTC timestamps.");
        if (normalized.TryGetValue("from", out var from) && normalized.TryGetValue("to", out var to) && DateTimeOffset.Parse(from) > DateTimeOffset.Parse(to)) throw Invalid("Invalid date range.");
        var scope = One(c, t, "SELECT jsonb_build_object('version',COALESCE((SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=@user),1));", r)!["version"]!.GetValue<long>();
        var filterHash = CursorScope(r);
        var offset = 0;
        Guid snapshotId;
        JsonArray candidates;
        if (normalized.TryGetValue("cursor", out var cursor))
        {
            var parts = cursor.Split('.');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out snapshotId) || !int.TryParse(parts[1], out offset) || offset < 0)
                throw Error(400, "SEARCH_CURSOR_INVALID", "Invalid search cursor.");
            var snapshot = One(c, t, "SELECT jsonb_build_object('results',results,'filterHash',filter_hash,'scopeVersion',scope_version,'expired',expires_at<=statement_timestamp()) " +
                "FROM core.product_search_snapshots WHERE organization_id=@org AND user_account_id=@user AND id=@snapshot;", r, ("snapshot", snapshotId));
            if (snapshot is null || snapshot["expired"]!.GetValue<bool>() || snapshot["scopeVersion"]!.GetValue<long>() != scope)
                throw Error(410, "SEARCH_CURSOR_EXPIRED", "Search snapshot or authorization scope has expired.");
            if (Text(snapshot, "filterHash") != filterHash) throw Error(400, "SEARCH_CURSOR_INVALID", "Cursor filters do not match.");
            candidates = snapshot["results"]!.AsArray();
            if (offset > candidates.Count) throw Error(400, "SEARCH_CURSOR_INVALID", "Invalid search position.");
        }
        else
        {
            snapshotId = Guid.NewGuid();
            var union = new List<string>();
            var args = new List<(string, object?)> { ("q", q), ("limit", 10001) };
            foreach (var key in new[] { "projectIds", "userIds", "departments", "contactIds" }) args.Add((key, Values(key)));
            args.Add(("from", normalized.GetValueOrDefault("from", ""))); args.Add(("to", normalized.GetValueOrDefault("to", "")));
            args.Add(("sensitive", r.Permissions.Contains("FileLocation.ReadSensitivePath"))); args.Add(("session", r.SessionId));
            args.Add(("fileRead", r.Permissions.Contains("FileCatalog.Read") && r.Permissions.Contains("FileReference.Open")));
            foreach (var resource in Resources.Values)
            {
                if (resource.Type is "notification" or "network_resource" || !r.Permissions.Contains(resource.ReadPermission) ||
                    (types.Length > 0 && !types.Contains(resource.Type))) continue;
                var compatible = resource.Type is "project" or "task" or "calendar_event" or "catalog_item" or "contact" or "company" or "interaction" or "employee_profile";
                if (!compatible) continue;
                if (hasFiles is not null && resource.Type is "interaction" or "calendar_event" or "employee_profile") continue;
                var title = resource.Type is "contact" or "employee_profile" ? "p.display_name" : resource.Type == "interaction" ? "p.subject" : resource.Type is "task" or "calendar_event" ? "p.title" : "p.name";
                var project = resource.Type == "project" ? "p.id::text" : "to_jsonb(p)->>'project_id'";
                var date = "COALESCE((to_jsonb(p)->>'start_at_utc')::timestamptz,(to_jsonb(p)->>'deadline_at')::timestamptz,(to_jsonb(p)->>'event_date')::timestamptz,o.updated_at)";
                var terminal = "COALESCE(to_jsonb(p)->>'status','') IN ('completed','cancelled','inactive')";
                var clauses = new List<string> { "o.organization_id=@org", "o.lifecycle_state='active'", Visibility(resource),
                    $"strpos(lower({SearchText(resource)}),lower(@q))>0",
                    $"(@from='' OR {date}>=nullif(@from,'')::timestamptz)", $"(@to='' OR {date}<=nullif(@to,'')::timestamptz)" };
                if (states.Length == 1) clauses.Add(states[0] == "completed" ? terminal : "NOT(" + terminal + ")");
                if (Values("projectIds").Length > 0) clauses.Add($"({project}=ANY(@projectIds) OR EXISTS(SELECT 1 FROM core.object_links l JOIN projects.projects pr ON pr.id=CASE WHEN l.source_object_id=p.id THEN l.target_object_id ELSE l.source_object_id END AND pr.organization_id=l.organization_id " +
                    "WHERE l.organization_id=@org AND (l.source_object_id=p.id OR l.target_object_id=p.id) AND pr.id::text=ANY(@projectIds) AND (@admin OR pr.owner_user_id=@user OR pr.manager_user_id=@user OR EXISTS(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=pr.id AND m.user_account_id=@user AND m.status='active'))))");
                if (Values("userIds").Length > 0) clauses.Add("(o.created_by::text=ANY(@userIds) OR to_jsonb(p)->>'owner_user_id'=ANY(@userIds) OR to_jsonb(p)->>'manager_user_id'=ANY(@userIds))");
                if (Values("departments").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM iam.user_accounts ua JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id WHERE ua.organization_id=@org AND ua.id=o.created_by AND to_jsonb(ep)->>'department_id'=ANY(@departments))");
                if (Values("contactIds").Length > 0)
                    clauses.Add("(" + (resource.Type switch
                    {
                        "contact" => "p.id::text=ANY(@contactIds)",
                        "company" => "EXISTS(SELECT 1 FROM crm.company_contacts cc WHERE cc.organization_id=@org AND cc.company_id=p.id AND cc.contact_id::text=ANY(@contactIds))",
                        "interaction" => "p.counterparty_object_id::text=ANY(@contactIds) OR p.participant_object_ids::text[] && @contactIds",
                        _ => "EXISTS(SELECT 1 FROM core.object_links l JOIN core.objects contact ON contact.id=CASE WHEN l.source_object_id=p.id THEN l.target_object_id ELSE l.source_object_id END AND contact.organization_id=l.organization_id WHERE l.organization_id=@org AND (l.source_object_id=p.id OR l.target_object_id=p.id) AND contact.object_type IN ('contact','company') AND contact.lifecycle_state='active' AND contact.id::text=ANY(@contactIds))",
                    }) + ")");
                if (hasFiles is not null)
                {
                    var fileIds = resource.Type == "catalog_item" ? "fl.catalog_item_id=p.id" : "EXISTS(SELECT 1 FROM core.object_links l WHERE l.organization_id=@org AND l.source_object_id=p.id AND l.target_object_id=fl.catalog_item_id AND l.link_type IN ('task_file','project_file','contact_file'))";
                    var accessible = "EXISTS(SELECT 1 FROM files.file_locations fl JOIN core.objects fo ON fo.organization_id=fl.organization_id AND fo.id=fl.catalog_item_id WHERE @fileRead AND fl.organization_id=@org AND " + fileIds + " AND fo.lifecycle_state='active' AND fl.is_enabled " +
                        "AND (@sensitive OR fl.owner_user_id=@user) AND (fl.location_type='unc_path' OR (fl.owner_user_id=@user AND fl.device_id=(SELECT device_id FROM iam.sessions WHERE id=@session AND user_account_id=@user AND organization_id=@org))))";
                    clauses.Add(hasFiles == "true" ? accessible : "NOT(" + accessible + ")");
                }
                union.Add($"SELECT o.id,o.object_type,o.updated_at,CASE WHEN lower({title})=lower(@q) THEN 2 ELSE 1 END relevance," +
                    $"jsonb_build_object('objectId',o.id,'objectType',o.object_type,'title',{title},'version',o.version,'updatedAt',o.updated_at,'lifecycleState',o.lifecycle_state) result " +
                    $"FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id WHERE {string.Join(" AND ", clauses)}");
            }
            if ((types.Length == 0 || types.Contains("file_location")) && r.Permissions.Contains("FileCatalog.Read") && r.Permissions.Contains("FileReference.Open") && hasFiles != "false" && !(states.Length == 1 && states[0] == "completed"))
            {
                var clauses = new List<string> { "fl.organization_id=@org", "o.lifecycle_state='active'", "fl.is_enabled",
                    "(@sensitive OR fl.owner_user_id=@user)",
                    "(fl.location_type='unc_path' OR (fl.owner_user_id=@user AND fl.device_id=(SELECT device_id FROM iam.sessions WHERE organization_id=@org AND user_account_id=@user AND id=@session)))",
                    "strpos(lower(concat_ws(' ',ci.name,ci.description,fl.raw_path)),lower(@q))>0",
                    "(@from='' OR o.updated_at>=nullif(@from,'')::timestamptz)", "(@to='' OR o.updated_at<=nullif(@to,'')::timestamptz)" };
                if (Values("projectIds").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM core.object_links l JOIN projects.projects pr ON pr.id=l.source_object_id AND pr.organization_id=l.organization_id WHERE l.organization_id=@org AND l.target_object_id=ci.id AND pr.id::text=ANY(@projectIds) AND (@admin OR pr.owner_user_id=@user OR pr.manager_user_id=@user OR EXISTS(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=pr.id AND m.user_account_id=@user AND m.status='active')))");
                if (Values("contactIds").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM core.object_links l JOIN core.objects contact ON contact.id=l.source_object_id AND contact.organization_id=l.organization_id WHERE l.organization_id=@org AND l.target_object_id=ci.id AND contact.object_type IN ('contact','company') AND contact.lifecycle_state='active' AND contact.id::text=ANY(@contactIds))");
                if (Values("userIds").Length > 0) clauses.Add("fl.owner_user_id::text=ANY(@userIds)");
                if (Values("departments").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM iam.user_accounts ua JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id WHERE ua.organization_id=@org AND ua.id=fl.owner_user_id AND to_jsonb(ep)->>'department_id'=ANY(@departments))");
                union.Add("SELECT fl.id,'file_location'::text object_type,o.updated_at,CASE WHEN lower(ci.name)=lower(@q) THEN 2 ELSE 1 END relevance," +
                    "jsonb_build_object('objectId',fl.id,'objectType','file_location','parentObjectId',ci.id,'title',ci.name,'version',fl.version,'updatedAt',o.updated_at,'lifecycleState',o.lifecycle_state) result " +
                    "FROM files.file_locations fl JOIN files.catalog_items ci ON ci.id=fl.catalog_item_id AND ci.organization_id=fl.organization_id JOIN core.objects o ON o.id=ci.id AND o.organization_id=ci.organization_id WHERE " + string.Join(" AND ", clauses));
            }
            if (hasFiles is not null && types.Length > 0 && !types.Any(type => type is "task" or "project" or "catalog_item" or "contact" or "company" or "file_location")) throw Invalid("hasFiles is incompatible with requested types.");
            var rows = union.Count == 0 ? [] : Many(c, t, "SELECT result FROM (" + string.Join(" UNION ALL ", union) + ") matches ORDER BY relevance DESC,updated_at DESC,object_type,id LIMIT @limit;", r, args.ToArray());
            if (rows.Count > 10000) throw Invalid("Search is too broad; refine the filters.");
            candidates = new JsonArray(rows.ToArray<JsonNode?>());
            // Bounded, expiring query cache only; no product object is changed by searching.
            Run(c, t, "DELETE FROM core.product_search_snapshots WHERE organization_id=@org AND expires_at<statement_timestamp();", r);
            Run(c, t, "INSERT INTO core.product_search_snapshots(id,organization_id,user_account_id,filter_hash,scope_version,results,expires_at) " +
                "VALUES(@snapshot,@org,@user,@hash,@scope,@results::jsonb,statement_timestamp()+interval '15 minutes');", r,
                ("snapshot", snapshotId), ("hash", filterHash), ("scope", scope), ("results", candidates.ToJsonString()));
        }
        var page = new JsonArray();
        while (offset < candidates.Count && page.Count < limit)
        {
            var candidate = candidates[offset++]!.AsObject();
            if (Text(candidate, "objectType") == "file_location")
            {
                if (!r.Permissions.Contains("FileCatalog.Read") || !r.Permissions.Contains("FileReference.Open")) continue;
                var allowed = One(c, t, "SELECT jsonb_build_object('id',fl.id) FROM files.file_locations fl JOIN core.objects o ON o.organization_id=fl.organization_id AND o.id=fl.catalog_item_id " +
                    "WHERE fl.organization_id=@org AND fl.id=@id AND fl.version=@version AND fl.is_enabled AND o.lifecycle_state='active' AND (@sensitive OR fl.owner_user_id=@user) " +
                    "AND (fl.location_type='unc_path' OR (fl.owner_user_id=@user AND fl.device_id=(SELECT device_id FROM iam.sessions WHERE organization_id=@org AND user_account_id=@user AND id=@session)));", r,
                    ("id", GuidValue(candidate, "objectId")), ("version", Version(candidate)), ("sensitive", r.Permissions.Contains("FileLocation.ReadSensitivePath")), ("session", r.SessionId));
                if (allowed is not null) page.Add(candidate.DeepClone());
                continue;
            }
            var resource = Resources.Values.First(resource => resource.Type == Text(candidate, "objectType"));
            // Recheck lifecycle and current membership before pagination, even inside a cached snapshot.
            var visible = One(c, t, $"SELECT jsonb_build_object('id',o.id) FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                $"WHERE o.organization_id=@org AND o.id=@id AND o.lifecycle_state='active' AND {Visibility(resource)};", r, ("id", GuidValue(candidate, "objectId")));
            if (visible is not null && r.Permissions.Contains(resource.ReadPermission)) page.Add(candidate.DeepClone());
        }
        return r.Route.Operation == "suggestions" ? new(page) : new(new JsonObject { ["items"] = page, ["nextCursor"] = offset < candidates.Count ? snapshotId.ToString("N") + "." + offset : null, ["tookMs"] = timer.ElapsedMilliseconds });

        string[] Values(string key) => normalized.TryGetValue(key, out var value) ? value.Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
    }
}
