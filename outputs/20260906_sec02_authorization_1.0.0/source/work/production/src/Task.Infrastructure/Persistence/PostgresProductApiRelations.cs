using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using Task.Application.Files;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse Related(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource, JsonObject current)
    {
        var operation = r.Route.Operation;
        if (operation == "history")
        {
            ValidateQuery(r, "limit page");
            var limit = QueryInt(r, "limit", 50, 1, 200);
            var rows = Many(c, t, "SELECT jsonb_build_object('id',id,'action',event_type,'version',aggregate_version,'actorUserId',actor_user_id,'occurredAt',occurred_at,'changedFields',changed_fields) " +
                "FROM governance.domain_events WHERE organization_id=@org AND aggregate_id=@id ORDER BY occurred_at DESC,id LIMIT @limit OFFSET @offset;", r,
                ("id", r.Id), ("limit", limit), ("offset", (QueryInt(r, "page", 1, 1, 10000) - 1) * limit));
            return new(new JsonObject { ["items"] = new JsonArray(rows.ToArray<JsonNode?>()) });
        }
        if (operation == "members")
        {
            ValidateQuery(r, "status limit page");
            var limit = QueryInt(r, "limit", 50, 1, 200);
            var rows = Many(c, t, "SELECT to_jsonb(m) FROM projects.members m WHERE organization_id=@org AND project_id=@id AND status=@status " +
                "ORDER BY user_account_id LIMIT @limit OFFSET @offset;", r, ("id", r.Id), ("status", r.Query.GetValueOrDefault("status", "active")),
                ("limit", limit), ("offset", (QueryInt(r, "page", 1, 1, 10000) - 1) * limit));
            return new(new JsonObject { ["items"] = new JsonArray(rows.ToArray<JsonNode?>()) }, Version: Version(current));
        }
        if (operation is "locations" or "resolve") return Locations(c, t, r, current);
        if (Text(current, "lifecycleState") != "active") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the object before editing its relationships.");
        JsonObject? result;
        if (operation == "participants")
        {
            ValidateFields(r.Body, "participantObjectIds");
            var body = (JsonObject)current.DeepClone(); body["participantObjectIds"] = r.Body["participantObjectIds"]?.DeepClone();
            if (body["participantObjectIds"] is not JsonArray) throw Invalid("Participants are required.");
            ValidateContent(c, t, r, resource, body, current);
            Update(c, t, r, resource.Table, r.Body, r.Id!.Value); result = null;
        }
        else if (operation.StartsWith("member-", StringComparison.Ordinal) || operation == "transfer") result = Members(c, t, r, current);
        else if (operation.StartsWith("location-", StringComparison.Ordinal)) result = ChangeLocation(c, t, r, current);
        else result = CrmRelation(c, t, r);
        var updated = Bump(c, t, r, resource, current);
        if (operation is "member-remove" or "channel-remove" or "contact-unlink" or "location-remove") return new(null, 204, Version(updated));
        if (operation == "member-overrides") return new(result, Version: Version(result!));
        return new(result ?? updated, operation.EndsWith("-add", StringComparison.Ordinal) || operation == "contact-link" ? 201 : 200, Version(updated));
    }

    private static void CheckProjectWrite(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject current)
    {
        if (IsAdmin(r) || GuidValue(current, "ownerUserId") == r.UserId || current["managerUserId"]?.ToString() == r.UserId.ToString()) return;
        var permission = r.Route.Permission.ToLowerInvariant();
        var member = One(c, t, "SELECT jsonb_build_object('allowed',true) FROM projects.members m WHERE m.organization_id=@org AND m.project_id=@id " +
            "AND m.user_account_id=@user AND m.status='active' " +
            "AND NOT (m.permission_overrides->'deny' ? @permission) " +
            "AND NOT EXISTS (SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=@permission AND rp.effect='deny') " +
            "AND ((m.permission_overrides->'allow' ? @permission) OR EXISTS (SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=@permission AND rp.effect='grant'));",
            r, ("id", r.Id), ("permission", permission));
        if (member is null) throw Error(403, "FORBIDDEN", "Project role does not permit this action.");
    }

    private static JsonObject? Members(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject current)
    {
        var body = (JsonObject)r.Body.DeepClone();
        var operation = r.Route.Operation;
        if (operation == "transfer")
        {
            ValidateFields(body, "newOwnerUserId expectedNewOwnerMembershipVersion");
            var user = GuidValue(body, "newOwnerUserId");
            RequireActiveUser(c, t, r, user);
            var membership = One(c, t, "SELECT to_jsonb(m) FROM projects.members m WHERE organization_id=@org AND project_id=@id AND user_account_id=@target AND status='active' FOR UPDATE;",
                r, ("id", r.Id), ("target", user)) ?? throw Invalid("The new owner must be an active project member.");
            if (body["expectedNewOwnerMembershipVersion"]?.GetValue<int>() != Version(membership)) throw Error(412, "VERSION_CONFLICT", "Membership has changed.");
            Run(c, t, "INSERT INTO projects.members(organization_id,project_id,user_account_id,project_role_id,status,joined_at) " +
                "VALUES(@org,@id,@old,@role,'active',statement_timestamp()) ON CONFLICT(organization_id,project_id,user_account_id) DO UPDATE SET status='active',removed_at=NULL,version=projects.members.version+1;",
                r, ("id", r.Id), ("old", GuidValue(current, "ownerUserId")), ("role", GuidValue(membership, "projectRoleId")));
            Run(c, t, "UPDATE projects.projects SET owner_user_id=@target WHERE organization_id=@org AND id=@id;", r, ("id", r.Id), ("target", user));
            BumpScope(c, t, r, user);
            BumpScope(c, t, r, GuidValue(current, "ownerUserId"));
            return null;
        }
        var memberId = r.ChildId ?? GuidValue(body, "userAccountId");
        if (memberId == GuidValue(current, "ownerUserId")) throw Invalid("Use transfer-ownership before modifying the owner membership.");
        if (operation == "member-overrides")
        {
            ValidateFields(body, "allow deny expectedMemberVersion");
            var membership = One(c, t, "SELECT to_jsonb(m) FROM projects.members m WHERE organization_id=@org AND project_id=@id AND user_account_id=@target AND status='active' FOR UPDATE;",
                r, ("id", r.Id), ("target", memberId)) ?? throw Error(404, "OBJECT_NOT_VISIBLE", "Membership is not visible.");
            if (body["expectedMemberVersion"]?.GetValue<int>() != Version(membership) || r.ExpectedVersion != Version(membership))
                throw Error(412, "VERSION_CONFLICT", "Membership version has changed.");
            body.Remove("expectedMemberVersion");
            foreach (var name in new[] { "allow", "deny" })
            {
                if (body[name] is not JsonArray codes || codes.Count > 200 || codes.Any(n => n is not JsonValue)) throw Invalid("Overrides require allow and deny arrays.");
                foreach (var code in codes)
                {
                    var normalized = code!.GetValue<string>().ToLowerInvariant();
                    if (One(c, t, "SELECT jsonb_build_object('code',code) FROM iam.permissions WHERE code=@code;", r, ("code", normalized)) is null)
                        throw Invalid("Unknown permission override.");
                    // Never allow a project manager to grant permissions they do not possess.
                    if (name == "allow" && !IsAdmin(r)) throw Error(403, "FORBIDDEN", "Only an organization administrator can grant explicit overrides.");
                }
                body[name] = new JsonArray(codes.Select(n => JsonValue.Create(n!.GetValue<string>().ToLowerInvariant())).ToArray<JsonNode?>());
            }
            if (Run(c, t, "UPDATE projects.members SET permission_overrides=@payload::jsonb,version=version+1 " +
                "WHERE organization_id=@org AND project_id=@id AND user_account_id=@target;", r,
                ("payload", body.ToJsonString()), ("id", r.Id), ("target", memberId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Membership is not visible.");
        }
        else if (operation == "member-remove")
        {
            ValidateFields(body, "");
            if (Run(c, t, "UPDATE projects.members SET status='removed',removed_at=statement_timestamp(),version=version+1 " +
                "WHERE organization_id=@org AND project_id=@id AND user_account_id=@target AND status<>'removed';", r,
                ("id", r.Id), ("target", memberId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Membership is not visible.");
        }
        else
        {
            ValidateFields(body, "projectId userAccountId projectRoleId status joinedAt removedAt");
            CheckParent(body, "projectId", r.Id!.Value);
            if (body.ContainsKey("userAccountId") && GuidValue(body, "userAccountId") != memberId) throw Invalid("Member identifier is immutable.");
            RequireActiveUser(c, t, r, memberId);
            if (body["projectRoleId"] is not null)
            {
                var roleId = GuidValue(body, "projectRoleId");
                if (One(c, t, "SELECT jsonb_build_object('id',id) FROM iam.roles WHERE organization_id=@org AND id=@role;", r, ("role", roleId)) is null)
                    throw Invalid("Role must belong to the organization.");
                if (!IsAdmin(r) && One(c, t, "SELECT jsonb_build_object('restricted',true) FROM iam.role_permissions WHERE role_id=@role AND effect='grant' AND permission_code IN " +
                    "('organization.manage','identity.role.manage','project.managemembers','project.transferownership') LIMIT 1;", r, ("role", roleId)) is not null)
                    throw Error(403, "FORBIDDEN", "Only an administrator can assign a privileged role.");
            }
            if (operation == "member-add")
            {
                body["organizationId"] = r.OrganizationId; body["projectId"] = r.Id; body["userAccountId"] = memberId;
                Insert(c, t, r, "projects.members", body);
            }
            else
            {
                body.Remove("projectId"); body.Remove("userAccountId");
                if (body.Count == 0) throw Invalid("At least one membership field is required.");
                var columns = body.Select(p => Snake(p.Key)).ToArray();
                if (Run(c, t, $"UPDATE projects.members SET ({string.Join(',', columns)})=(SELECT {string.Join(',', columns)} FROM jsonb_populate_record(NULL::projects.members,@payload::jsonb)),version=version+1 " +
                    "WHERE organization_id=@org AND project_id=@id AND user_account_id=@target;", r,
                    ("payload", ToDatabase(body).ToJsonString()), ("id", r.Id), ("target", memberId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Membership is not visible.");
            }
        }
        BumpScope(c, t, r, memberId);
        return One(c, t, "SELECT to_jsonb(m) FROM projects.members m WHERE organization_id=@org AND project_id=@id AND user_account_id=@target;", r, ("id", r.Id), ("target", memberId));
    }

    private static void BumpScope(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid user) =>
        Run(c, t, "INSERT INTO iam.authorization_scope_versions(user_account_id,version,updated_at) VALUES(@target,2,statement_timestamp()) " +
            "ON CONFLICT(user_account_id) DO UPDATE SET version=iam.authorization_scope_versions.version+1,updated_at=EXCLUDED.updated_at;", r, ("target", user));

    private static JsonObject? CrmRelation(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var body = (JsonObject)r.Body.DeepClone();
        var operation = r.Route.Operation;
        if (operation is "contact-link" or "contact-unlink")
        {
            if (operation == "contact-unlink")
            {
                ValidateFields(body, "");
                if (Run(c, t, "DELETE FROM crm.company_contacts WHERE organization_id=@org AND company_id=@id AND contact_id=@child;", r,
                    ("id", r.Id), ("child", r.ChildId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Relationship is not visible.");
                return null;
            }
            ValidateFields(body, "contactId companyId jobTitle departmentName isPrimary validFrom validTo");
            CheckParent(body, "companyId", r.Id!.Value);
            var contactId = GuidValue(body, "contactId");
            if (One(c, t, "SELECT jsonb_build_object('id',id) FROM core.objects WHERE organization_id=@org AND id=@contact AND object_type='contact' AND lifecycle_state='active' AND iam.object_allowed(@org,id,@user,'contact.read',@admin);", r,
                ("contact", contactId)) is null) throw Invalid("The contact must be active in this organization.");
            body["organizationId"] = r.OrganizationId; body["companyId"] = r.Id;
            Insert(c, t, r, "crm.company_contacts", body);
            return body;
        }
        var table = operation == "address-add" ? "crm.addresses" : "crm.communication_channels";
        if (operation == "channel-remove")
        {
            ValidateFields(body, "");
            if (Run(c, t, "DELETE FROM crm.communication_channels WHERE organization_id=@org AND owner_object_id=@id AND id=@child;", r,
                ("id", r.Id), ("child", r.ChildId)) != 1) throw Error(404, "OBJECT_NOT_VISIBLE", "Channel is not visible.");
            return null;
        }
        ValidateFields(body, operation == "address-add"
            ? "ownerObjectId addressType countryCode region city street postalCode formattedAddress isPrimary"
            : "ownerObjectId channelType label value isPrimary isVerified");
        CheckParent(body, "ownerObjectId", r.Id!.Value);
        if (body.ContainsKey("isVerified")) throw Invalid("Channel verification is server-managed.");
        var id = r.ChildId ?? Guid.NewGuid();
        if (operation == "channel-patch")
        {
            if (One(c, t, "SELECT to_jsonb(p) FROM crm.communication_channels p WHERE organization_id=@org AND owner_object_id=@id AND id=@child;", r,
                ("id", r.Id), ("child", id)) is null) throw Error(404, "OBJECT_NOT_VISIBLE", "Channel is not visible.");
            body.Remove("ownerObjectId");
            if (body.Count == 0) throw Invalid("At least one channel field is required.");
            Update(c, t, r, table, body, id);
        }
        else
        {
            body["organizationId"] = r.OrganizationId; body["ownerObjectId"] = r.Id; body["id"] = id;
            Insert(c, t, r, table, body);
        }
        return One(c, t, $"SELECT to_jsonb(p) FROM {table} p WHERE organization_id=@org AND id=@child;", r, ("child", id));
    }

    private ProductApiResponse Locations(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject current)
    {
        ValidateQuery(r, "deviceId");
        ValidateFields(r.Body, "deviceId action");
        var device = SessionDevice(c, t, r);
        if ((r.Query.TryGetValue("deviceId", out var queryDevice) && queryDevice != device.ToString()) ||
            (r.Body["deviceId"] is not null && GuidValue(r.Body, "deviceId") != device)) throw Error(403, "FORBIDDEN", "Device must match the authenticated session.");
        var locations = Many(c, t, "SELECT to_jsonb(p) FROM files.file_locations p WHERE organization_id=@org AND catalog_item_id=@id ORDER BY is_primary DESC,priority,id;", r, ("id", r.Id));
        foreach (var location in locations)
        {
            var local = Text(location, "locationType") != "unc_path";
            var owns = GuidValue(location, "ownerUserId") == r.UserId && (!local || location["deviceId"]?.ToString() == device.ToString());
            var canRead = owns || r.Permissions.Contains("FileLocation.ReadSensitivePath");
            location["canOpenOnDevice"] = canRead && location["isEnabled"]!.GetValue<bool>() && (!local || owns);
            if (!canRead) location.Remove("rawPath");
        }
        if (r.Route.Operation == "resolve")
        {
            if (Text(current, "lifecycleState") == "trashed") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the catalog item before resolving a path.");
            var location = locations.FirstOrDefault(l => l["canOpenOnDevice"]!.GetValue<bool>());
            return new(new JsonObject
            {
                ["catalogItemId"] = r.Id,
                ["location"] = location,
                ["status"] = location is null ? "unavailable_on_device" : "requires_client_check",
                ["physicalOperationPerformed"] = false
            }, Version: Version(current));
        }
        return new(new JsonArray(locations.ToArray<JsonNode?>()), Version: Version(current));
    }

    private JsonObject? ChangeLocation(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, JsonObject current)
    {
        if (Text(current, "itemType") is not ("file_reference" or "folder_reference")) throw Invalid("Only file or folder references have locations.");
        var operation = r.Route.Operation;
        var body = (JsonObject)r.Body.DeepClone();
        var device = SessionDevice(c, t, r);
        JsonObject? old = null;
        if (r.ChildId is not null)
        {
            old = One(c, t, "SELECT to_jsonb(p) FROM files.file_locations p WHERE organization_id=@org AND catalog_item_id=@id AND id=@child;", r,
                ("id", r.Id), ("child", r.ChildId)) ?? throw Error(404, "OBJECT_NOT_VISIBLE", "Location is not visible.");
            if (Text(old, "locationType") != "unc_path" && !IsAdmin(r) &&
                (GuidValue(old, "ownerUserId") != r.UserId || old["deviceId"]?.ToString() != device.ToString()))
                throw Error(403, "FORBIDDEN", "Only the owning device/user can change this local path.");
        }
        if (operation == "location-remove")
        {
            ValidateFields(body, "");
            Run(c, t, "DELETE FROM files.file_locations WHERE organization_id=@org AND catalog_item_id=@id AND id=@child;", r, ("id", r.Id), ("child", r.ChildId));
            return null;
        }
        ValidateFields(body, "catalogItemId locationType rawPath deviceId networkResourceId priority isEnabled isPrimary expectedCatalogItemVersion");
        CheckParent(body, "catalogItemId", r.Id!.Value);
        if (body["expectedCatalogItemVersion"] is not null && body["expectedCatalogItemVersion"]!.GetValue<int>() != r.ExpectedVersion) throw Invalid("Catalog version must match If-Match.");
        body.Remove("expectedCatalogItemVersion");
        var merged = old is null ? new JsonObject() : (JsonObject)old.DeepClone();
        foreach (var pair in body) merged[pair.Key] = pair.Value?.DeepClone();
        var path = Text(merged, "rawPath") ?? throw Invalid("rawPath is required.");
        var type = Text(merged, "locationType");
        if (type == "unc_path")
        {
            var resourceId = GuidValue(merged, "networkResourceId");
            var network = RequireObject(c, t, r with { Id = resourceId }, Resources["network-resources"]);
            if (Text(network, "status") != "active" || Text(network, "lifecycleState") != "active") throw Invalid("Network resource is disabled.");
            path = ValidateUnc(path, [Text(network, "rootUncPath")!]);
            body["deviceId"] = null;
        }
        else if (type is "local_path" or "mapped_drive")
        {
            if (merged["deviceId"] is not null && GuidValue(merged, "deviceId") != device) throw Error(403, "FORBIDDEN", "Local paths must belong to the current device.");
            if (!Regex.IsMatch(path, @"^[A-Za-z]:\\[^<>:""|?*\x00-\x1F]+$") || path.Split('\\').Any(part => part is "." or "..") || path.Length > 4096)
                throw Invalid("Invalid absolute local path.");
            var settings = One(c, t, "SELECT to_jsonb(s) FROM org.user_settings s WHERE organization_id=@org AND user_account_id=@user;", r);
            if (settings?["allowLocalPaths"]?.GetValue<bool>() == false) throw Error(403, "FORBIDDEN", "Local paths are disabled in your settings.");
            body["deviceId"] = device; body["networkResourceId"] = null;
        }
        else throw Invalid("Unknown location type.");
        body["rawPath"] = path;
        var id = r.ChildId ?? Guid.NewGuid();
        if (body["isPrimary"]?.GetValue<bool>() == true)
            Run(c, t, "UPDATE files.file_locations SET is_primary=false,version=version+1 WHERE organization_id=@org AND catalog_item_id=@id AND is_primary;", r, ("id", r.Id));
        if (old is null)
        {
            body["id"] = id; body["organizationId"] = r.OrganizationId; body["catalogItemId"] = r.Id; body["ownerUserId"] = r.UserId;
            Insert(c, t, r, "files.file_locations", body);
        }
        else
        {
            body.Remove("catalogItemId"); body["version"] = Version(old) + 1;
            Update(c, t, r, "files.file_locations", body, id);
        }
        var result = One(c, t, "SELECT to_jsonb(p)-'raw_path' FROM files.file_locations p WHERE organization_id=@org AND id=@child;", r, ("child", id));
        return result;
    }

    private static Guid SessionDevice(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        var session = One(c, t, "SELECT jsonb_build_object('deviceId',device_id) FROM iam.sessions WHERE organization_id=@org AND user_account_id=@user AND id=@session;", r, ("session", r.SessionId));
        return session is null ? throw Error(401, "UNAUTHENTICATED", "Session is no longer valid.") : GuidValue(session, "deviceId");
    }
    private static void CheckParent(JsonObject body, string field, Guid id)
    { if (body[field] is not null && GuidValue(body, field) != id) throw Invalid("Body parent does not match the route."); }
    private static string ValidateUnc(string path, IReadOnlyList<string> roots)
    {
        var verdict = FileLocationPolicy.ValidateUnc(path, roots);
        return verdict.IsValid ? verdict.NormalizedPath! : throw Invalid("Invalid or disallowed UNC path.");
    }
}
