using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private const int SearchCandidateLimit = 10000;
    private const int MinimumSearchWindow = 64;
    private const string SearchOrder = "relevance DESC,updated_at DESC,object_type,id";
    private const string SearchFileLocationSource = """
        FROM files.file_locations fl
        JOIN files.catalog_items ci ON ci.organization_id=fl.organization_id AND ci.id=fl.catalog_item_id
        JOIN core.objects o ON o.organization_id=ci.organization_id AND o.id=ci.id
        WHERE fl.organization_id=@org AND o.lifecycle_state='active' AND fl.is_enabled
            AND iam.object_allowed(@org,o.id,@user,'filecatalog.read',@admin)
            AND (@sensitive OR fl.owner_user_id=@user)
            AND (fl.location_type='unc_path' OR (fl.owner_user_id=@user AND fl.device_id=(
                SELECT device_id FROM iam.sessions WHERE organization_id=@org AND user_account_id=@user AND id=@session)))
        """;

    private ProductApiResponse Search(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var linkTargets = r.Route.Operation == "link-targets";
        if (!r.Permissions.Contains(r.Route.Permission)) throw Error(403, "FORBIDDEN", "Permission is required.");
        ValidateQuery(r, linkTargets ? "q types cursor limit" : "q types projectIds userIds departments contactIds hasFiles lifecycle from to cursor limit");
        var timer = Stopwatch.StartNew();
        var q = r.Query.GetValueOrDefault("q", "").Trim();
        if (q.Length < (r.Route.Operation == "suggestions" ? 1 : 2) || q.Length > 200) throw Invalid("Invalid search text length.");
        var limit = QueryInt(r, "limit", 50, 1, 500);
        var normalized = r.Query.ToDictionary(p => p.Key, p => p.Value);
        normalized["q"] = q.ToLowerInvariant();
        foreach (var key in new[] { "types", "projectIds", "userIds", "departments", "contactIds", "lifecycle" })
            if (normalized.TryGetValue(key, out var value)) normalized[key] = string.Join(',', value.Split(',').Select(s => s.Trim()).Distinct().Order());
        r = r with { Query = normalized };
        var types = SearchFilterValues(r, "types");
        var knownTypes = linkTargets
            ? Resources.Values.Select(resource => resource.Type).ToArray()
            : new[] { "task", "calendar_event", "project", "catalog_item", "file_location", "contact", "company", "interaction", "comment", "employee_profile" };
        if (types.Length > knownTypes.Length || types.Any(type => !knownTypes.Contains(type)) || linkTargets && types.Length == 0)
            throw Invalid("Unknown search type.");
        var states = SearchFilterValues(r, "lifecycle");
        if (states.Any(state => state is not ("active" or "completed"))) throw Invalid("Search lifecycle must be active and/or completed.");
        foreach (var key in new[] { "projectIds", "userIds", "departments", "contactIds" })
            if (SearchFilterValues(r, key).Length > 100 || SearchFilterValues(r, key).Any(value => !Guid.TryParse(value, out var id) || id == Guid.Empty)) throw Invalid("Invalid UUID filter.");
        if (normalized.TryGetValue("hasFiles", out var hasFiles) && hasFiles is not ("true" or "false")) throw Invalid("hasFiles must be boolean.");
        foreach (var key in new[] { "from", "to" })
            if (normalized.TryGetValue(key, out var date) && (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) || !date.EndsWith('Z')))
                throw Invalid("Date filters require RFC3339 UTC timestamps.");
        if (normalized.TryGetValue("from", out var from) && normalized.TryGetValue("to", out var to) && DateTimeOffset.Parse(from) > DateTimeOffset.Parse(to)) throw Invalid("Invalid date range.");
        var scope = One(c, t, "SELECT jsonb_build_object('version',COALESCE((SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=@user),1));", r)!["version"]!.GetValue<long>();
        var filterHash = CursorScope(r);
        return normalized.TryGetValue("cursor", out var cursor)
            ? ContinueSearch(c, t, r, cursor, scope, filterHash, limit, timer)
            : StartSearch(c, t, r, q, types, states, hasFiles, scope, filterHash, limit, linkTargets, timer);
    }

    private static ProductApiResponse StartSearch(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r,
        string q, string[] types, string[] states, string? hasFiles, long scope, string filterHash, int limit,
        bool linkTargets, Stopwatch timer)
    {
        var normalized = r.Query;
        var union = new List<string>();
        var args = new List<(string, object?)> { ("q", q), ("candidateLimit", SearchCandidateLimit + 1), ("pageLimit", limit) };
        foreach (var key in new[] { "projectIds", "userIds", "departments", "contactIds" }) args.Add((key, SearchFilterValues(r, key)));
        args.Add(("from", normalized.GetValueOrDefault("from", ""))); args.Add(("to", normalized.GetValueOrDefault("to", "")));
        args.Add(("sensitive", r.Permissions.Contains("FileLocation.ReadSensitivePath"))); args.Add(("session", r.SessionId));
        args.Add(("fileRead", CanSearchFileLocations(r)));
        foreach (var resource in Resources.Values)
        {
            if ((!linkTargets && resource.Type is "notification" or "network_resource") || !r.Permissions.Contains(resource.ReadPermission) ||
                (types.Length > 0 && !types.Contains(resource.Type))) continue;
            var compatible = resource.Type is "project" or "task" or "calendar_event" or "catalog_item" or "contact" or "company" or "interaction" or "employee_profile";
            if (!linkTargets && !compatible) continue;
            if (hasFiles is not null && resource.Type is "interaction" or "calendar_event" or "employee_profile") continue;
            var title = resource.Type is "contact" or "employee_profile" ? "p.display_name" : resource.Type == "interaction" ? "p.subject" : resource.Type is "task" or "calendar_event" or "notification" or "inbox_item" ? "p.title" : "p.name";
            var project = resource.Type == "project" ? "p.id::text" : "to_jsonb(p)->>'project_id'";
            var date = "COALESCE((to_jsonb(p)->>'start_at_utc')::timestamptz,(to_jsonb(p)->>'deadline_at')::timestamptz,(to_jsonb(p)->>'event_date')::timestamptz,o.updated_at)";
            var terminal = "COALESCE(to_jsonb(p)->>'status','') IN ('completed','cancelled','inactive')";
            var clauses = new List<string> { SearchTextMatch(resource, q),
                $"(@from='' OR {date}>=nullif(@from,'')::timestamptz)", $"(@to='' OR {date}<=nullif(@to,'')::timestamptz)" };
            if (states.Length == 1) clauses.Add(states[0] == "completed" ? terminal : "NOT(" + terminal + ")");
            if (SearchFilterValues(r, "projectIds").Length > 0) clauses.Add($"({project}=ANY(@projectIds) OR EXISTS(SELECT 1 FROM core.object_links l JOIN projects.projects pr ON pr.id=CASE WHEN l.source_object_id=p.id THEN l.target_object_id ELSE l.source_object_id END AND pr.organization_id=l.organization_id " +
                "WHERE l.organization_id=@org AND (l.source_object_id=p.id OR l.target_object_id=p.id) AND pr.id::text=ANY(@projectIds) AND (@admin OR pr.owner_user_id=@user OR pr.manager_user_id=@user OR EXISTS(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=pr.id AND m.user_account_id=@user AND m.status='active'))))");
            if (SearchFilterValues(r, "userIds").Length > 0) clauses.Add("(o.created_by::text=ANY(@userIds) OR to_jsonb(p)->>'owner_user_id'=ANY(@userIds) OR to_jsonb(p)->>'manager_user_id'=ANY(@userIds))");
            if (SearchFilterValues(r, "departments").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM iam.user_accounts ua JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id WHERE ua.organization_id=@org AND ua.id=o.created_by AND to_jsonb(ep)->>'department_id'=ANY(@departments))");
            if (SearchFilterValues(r, "contactIds").Length > 0)
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
                var accessible = "EXISTS(SELECT 1 FROM files.file_locations fl JOIN core.objects fo ON fo.organization_id=fl.organization_id AND fo.id=fl.catalog_item_id WHERE @fileRead AND fl.organization_id=@org AND " + fileIds + " AND fo.lifecycle_state='active' AND iam.object_allowed(@org,fo.id,@user,'filecatalog.read',@admin) AND fl.is_enabled " +
                    "AND (@sensitive OR fl.owner_user_id=@user) AND (fl.location_type='unc_path' OR (fl.owner_user_id=@user AND fl.device_id=(SELECT device_id FROM iam.sessions WHERE id=@session AND user_account_id=@user AND organization_id=@org))))";
                clauses.Add(hasFiles == "true" ? accessible : "NOT(" + accessible + ")");
            }
            union.Add($"SELECT o.id,o.object_type,o.updated_at,CASE WHEN lower({title})=lower(@q) THEN 2 ELSE 1 END relevance," +
                $"jsonb_build_object('objectId',o.id,'objectType',o.object_type,'title',{title},'version',o.version,'updatedAt',o.updated_at,'lifecycleState',o.lifecycle_state) result " +
                $"{SearchObjectSource(resource)} AND {string.Join(" AND ", clauses)}");
        }
        if ((types.Length == 0 || types.Contains("file_location")) && CanSearchFileLocations(r) && hasFiles != "false" && !(states.Length == 1 && states[0] == "completed"))
        {
            var clauses = new List<string>
            {
                "strpos(lower(concat_ws(' ',ci.name,ci.description,fl.raw_path)),lower(@q))>0",
                "(@from='' OR o.updated_at>=nullif(@from,'')::timestamptz)", "(@to='' OR o.updated_at<=nullif(@to,'')::timestamptz)",
            };
            if (SearchFilterValues(r, "projectIds").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM core.object_links l JOIN projects.projects pr ON pr.id=l.source_object_id AND pr.organization_id=l.organization_id WHERE l.organization_id=@org AND l.target_object_id=ci.id AND pr.id::text=ANY(@projectIds) AND (@admin OR pr.owner_user_id=@user OR pr.manager_user_id=@user OR EXISTS(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=pr.id AND m.user_account_id=@user AND m.status='active')))");
            if (SearchFilterValues(r, "contactIds").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM core.object_links l JOIN core.objects contact ON contact.id=l.source_object_id AND contact.organization_id=l.organization_id WHERE l.organization_id=@org AND l.target_object_id=ci.id AND contact.object_type IN ('contact','company') AND contact.lifecycle_state='active' AND contact.id::text=ANY(@contactIds))");
            if (SearchFilterValues(r, "userIds").Length > 0) clauses.Add("fl.owner_user_id::text=ANY(@userIds)");
            if (SearchFilterValues(r, "departments").Length > 0) clauses.Add("EXISTS(SELECT 1 FROM iam.user_accounts ua JOIN org.employee_profiles ep ON ep.id=ua.employee_profile_id AND ep.organization_id=ua.organization_id WHERE ua.organization_id=@org AND ua.id=fl.owner_user_id AND to_jsonb(ep)->>'department_id'=ANY(@departments))");
            union.Add("SELECT fl.id,'file_location'::text object_type,o.updated_at,CASE WHEN lower(ci.name)=lower(@q) THEN 2 ELSE 1 END relevance," +
                "jsonb_build_object('objectId',fl.id,'objectType','file_location','parentObjectId',ci.id,'title',ci.name,'version',fl.version,'updatedAt',o.updated_at,'lifecycleState',o.lifecycle_state) result " +
                $"{SearchFileLocationSource} AND {string.Join(" AND ", clauses)}");
        }
        if (hasFiles is not null && types.Length > 0 && !types.Any(type => type is "task" or "project" or "catalog_item" or "contact" or "company" or "file_location")) throw Invalid("hasFiles is incompatible with requested types.");

        if (union.Count == 0) return SearchPage(r, [], null, timer);

        // Count after visibility and filters so the same 10,000-candidate cap applies to all callers.
        // Store paginated results in PostgreSQL and return only the visible first page to the API.
        var suggestions = r.Route.Operation == "suggestions";
        var snapshotId = Guid.NewGuid();
        var saveSnapshot = suggestions ? "" : $$"""
            , saved AS (
                INSERT INTO core.product_search_snapshots(id,organization_id,user_account_id,filter_hash,scope_version,results,expires_at)
                SELECT @snapshot,@org,@user,@hash,@scope,
                    (SELECT jsonb_agg(result ORDER BY {{SearchOrder}}) FROM matches),
                    statement_timestamp()+interval '15 minutes'
                FROM statistics WHERE total>@pageLimit AND total<@candidateLimit
                RETURNING id
            )
            """;
        args.Add(("snapshot", snapshotId));
        args.Add(("scope", scope));
        args.Add(("hash", filterHash));
        var result = One(c, t, $$"""
            WITH matches AS MATERIALIZED (
                SELECT * FROM ({{string.Join(" UNION ALL ", union)}}) candidates
                ORDER BY {{SearchOrder}} LIMIT @candidateLimit
            ), statistics AS (SELECT count(*) AS total FROM matches)
            {{saveSnapshot}}
            SELECT jsonb_build_object(
                'total',total,
                'saved',{{(suggestions ? "false" : "EXISTS(SELECT 1 FROM saved)")}},
                'items',COALESCE((
                    SELECT jsonb_agg(result ORDER BY {{SearchOrder}})
                    FROM (SELECT * FROM matches ORDER BY {{SearchOrder}} LIMIT @pageLimit) page
                ),'[]'::jsonb))
            FROM statistics;
            """, r, args.ToArray())!;
        if (result["total"]!.GetValue<int>() > SearchCandidateLimit) throw Invalid("Search is too broad; refine the filters.");
        var nextCursor = result["saved"]!.GetValue<bool>() ? $"{snapshotId:N}.{limit}" : null;
        var items = result["items"]!.AsArray();
        result.Remove("items");
        return SearchPage(r, items, nextCursor, timer);
    }

    private static ProductApiResponse ContinueSearch(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r,
        string cursor, long scope, string filterHash, int limit, Stopwatch timer)
    {
        var parts = cursor.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var snapshotId) || !int.TryParse(parts[1], out var offset) || offset < 0)
            throw Error(400, "SEARCH_CURSOR_INVALID", "Invalid search cursor.");

        var cases = Resources.Values.Where(resource => r.Permissions.Contains(resource.ReadPermission))
            .Select(resource => $"WHEN '{resource.Type}' THEN EXISTS(SELECT 1 {SearchObjectSource(resource)} AND o.id=(candidate->>'objectId')::uuid)")
            .ToList();
        if (CanSearchFileLocations(r))
        {
            cases.Add($"WHEN 'file_location' THEN EXISTS(SELECT 1 {SearchFileLocationSource} " +
                "AND fl.id=(candidate->>'objectId')::uuid AND fl.version=(candidate->>'version')::int)");
        }
        var visible = cases.Count == 0 ? "false" : $"CASE candidate->>'objectType' {string.Join(" ", cases)} ELSE false END";
        var windowSize = limit;
        var page = new JsonArray();
        int count;
        do
        {
            // The path selects an ordinal range from old and new JSONB snapshots. Only visible rows
            // within that range are sent to the API, with their original positions for the cursor.
            var snapshot = One(c, t, $$"""
                WITH snapshot AS MATERIALIZED (
                    SELECT results, jsonb_array_length(results) AS total, filter_hash, scope_version,
                        expires_at<=statement_timestamp() AS expired
                    FROM core.product_search_snapshots
                    WHERE organization_id=@org AND user_account_id=@user AND id=@snapshot
                ), candidates AS (
                    SELECT candidate,@offset+ordinal AS position
                    FROM snapshot s
                    CROSS JOIN LATERAL jsonb_path_query(s.results, '$[$start to $stop]',
                        jsonb_build_object('start',@offset,'stop',least(@offset::bigint+@window-1,s.total-1)))
                        WITH ORDINALITY AS slice(candidate,ordinal)
                    WHERE NOT s.expired AND s.scope_version=@scope AND s.filter_hash=@hash AND @offset<s.total
                )
                SELECT jsonb_build_object('total',total,'filterHash',filter_hash,'scopeVersion',scope_version,'expired',expired,
                    'items',COALESCE((
                        SELECT jsonb_agg(jsonb_build_object('position',position,'candidate',candidate) ORDER BY position)
                        FROM (SELECT * FROM candidates WHERE {{visible}} ORDER BY position LIMIT @remaining) page
                    ),'[]'::jsonb))
                FROM snapshot;
                """, r, ("snapshot", snapshotId), ("offset", offset), ("window", windowSize), ("remaining", limit - page.Count),
                ("scope", scope), ("hash", filterHash), ("sensitive", r.Permissions.Contains("FileLocation.ReadSensitivePath")), ("session", r.SessionId));
            if (snapshot is null || snapshot["expired"]!.GetValue<bool>() || snapshot["scopeVersion"]!.GetValue<long>() != scope)
                throw Error(410, "SEARCH_CURSOR_EXPIRED", "Search snapshot or authorization scope has expired.");
            if (Text(snapshot, "filterHash") != filterHash) throw Error(400, "SEARCH_CURSOR_INVALID", "Cursor filters do not match.");
            count = snapshot["total"]!.GetValue<int>();
            if (offset > count) throw Error(400, "SEARCH_CURSOR_INVALID", "Invalid search position.");

            var rows = snapshot["items"]!.AsArray();
            foreach (var row in rows)
                page.Add(row!["candidate"]!.DeepClone());
            // Once the page is full, consume only through its last hit. Otherwise the whole window was examined.
            offset = page.Count == limit
                ? rows[^1]!["position"]!.GetValue<int>()
                : Math.Min(count, offset + windowSize);
            // Expand only when hidden rows prevented a full page.
            windowSize = Math.Max(limit, MinimumSearchWindow);
        } while (offset < count && page.Count < limit);

        return SearchPage(r, page, offset < count ? $"{snapshotId:N}.{offset}" : null, timer);
    }

    private static ProductApiResponse SearchPage(ProductApiRequest r, JsonArray page, string? nextCursor, Stopwatch timer) =>
        r.Route.Operation == "suggestions" ? new(page) :
            new(new JsonObject { ["items"] = page, ["nextCursor"] = nextCursor, ["tookMs"] = timer.ElapsedMilliseconds });

    private static bool CanSearchFileLocations(ProductApiRequest r) =>
        r.Permissions.Contains("FileCatalog.Read") && r.Permissions.Contains("FileReference.Open");

    private static string SearchObjectSource(Resource resource) =>
        $"FROM {resource.Table} p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
        $"WHERE o.organization_id=@org AND o.lifecycle_state='active' AND {Visibility(resource)}";

    private static string SearchTextMatch(Resource resource, string query)
    {
        if (resource.Type != "task") return $"strpos(lower({SearchText(resource)}),lower(@q))>0";

        const string taskText = "lower(p.title)";
        var exact = $"strpos({taskText},lower(@q))>0";
        if (query.Length < 3) return exact;
        // LIKE supplies an index candidate set. Preserve the original literal substring check.
        const string pattern = "'%' || replace(replace(replace(lower(@q),'!','!!'),'%','!%'),'_','!_') || '%'";
        return $"({taskText} LIKE ({pattern}) ESCAPE '!' AND {exact})";
    }

    private static string[] SearchFilterValues(ProductApiRequest r, string key) =>
        r.Query.TryGetValue(key, out var value) ? value.Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
}
