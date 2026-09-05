using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse Roles(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        if (r.Route.Resource == "roles")
        {
            ValidateQuery(r, "limit page");
            var limit = QueryInt(r, "limit", 50, 1, 200);
            var rows = Many(c, t, "SELECT to_jsonb(role)||jsonb_build_object('permissions',COALESCE((SELECT jsonb_agg(jsonb_build_object('code',permission_code,'effect',effect) ORDER BY permission_code) FROM iam.role_permissions WHERE role_id=role.id),'[]'::jsonb)) FROM iam.roles role WHERE organization_id=@org ORDER BY code LIMIT @limit OFFSET @offset;",
                r, ("limit", limit + 1), ("offset", (QueryInt(r, "page", 1, 1, 10000) - 1) * limit));
            return new(new JsonObject { ["items"] = new JsonArray(rows.Take(limit).ToArray<JsonNode?>()), ["hasMore"] = rows.Count > limit });
        }
        // A delegated User.ManageRoles grant must never become a privilege-escalation path.
        if (!IsAdmin(r)) throw Error(403, "FORBIDDEN", "Organization administration is required to assign roles.");
        if (r.Body.Any(p => p.Key is not ("roles" or "expectedUserVersion")) || r.Body["roles"] is not JsonArray roles || roles.Count > 100)
            throw Invalid("Specify roles and expectedUserVersion.");
        if (r.Body["expectedUserVersion"] is not JsonValue expected || !expected.TryGetValue<int>(out var expectedVersion) || expectedVersion != r.ExpectedVersion) throw Invalid("expectedUserVersion must match If-Match.");
        var target = One(c, t, "SELECT to_jsonb(o) FROM iam.user_accounts u JOIN core.objects o ON o.organization_id=u.organization_id AND o.id=u.id WHERE u.organization_id=@org AND u.id=@id FOR UPDATE OF o,u;", r, ("id", r.Id))
            ?? throw Error(404, "OBJECT_NOT_VISIBLE", "User is not visible.");
        if (Version(target) != r.ExpectedVersion) throw Error(412, "VERSION_CONFLICT", "User version has changed.");
        var ids = new List<Guid>();
        var assignments = new List<(Guid Role, Guid? Department, DateTimeOffset? Until)>();
        foreach (var node in roles)
        {
            if (node is not JsonObject role || role.Any(p => p.Key is not ("roleId" or "departmentId" or "validUntil"))) throw Invalid("Invalid role assignment.");
            var id = GuidValue(role, "roleId");
            if (ids.Contains(id)) throw Invalid("Duplicate role.");
            if (One(c, t, "SELECT jsonb_build_object('id',id) FROM iam.roles WHERE organization_id=@org AND id=@role;", r, ("role", id)) is null)
                throw Invalid("Role must belong to the organization.");
            ids.Add(id);
            var department = role["departmentId"] is null ? (Guid?)null : GuidValue(role, "departmentId");
            DateTimeOffset? until = null;
            if (role["validUntil"] is not null)
            {
                if (role["validUntil"] is not JsonValue value || !value.TryGetValue<string>(out var raw) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(raw, @"(?:Z|[+-]\d{2}:\d{2})$") ||
                    !DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var instant) || instant <= DateTimeOffset.UtcNow)
                    throw Invalid("validUntil must be a future instant with a UTC offset.");
                until = instant.ToUniversalTime();
            }
            if (department is not null && One(c, t, "SELECT jsonb_build_object('id',id) FROM core.objects WHERE organization_id=@org AND id=@department AND object_type='department' AND lifecycle_state='active';", r, ("department", department)) is null)
                throw Invalid("Department must be active in this organization.");
            if ((department is not null || until is not null) && One(c, t, "SELECT jsonb_build_object('restricted',true) FROM iam.role_permissions WHERE role_id=@role AND effect='grant' AND permission_code IN ('organization.manage','identity.account.manage','identity.role.manage','user.manageroles') LIMIT 1;", r, ("role", id)) is not null)
                throw Invalid("Administrative roles require a permanent organization assignment.");
            assignments.Add((id, department, until));
        }
        Run(c, t, "DELETE FROM iam.user_roles WHERE user_account_id=@id;", r, ("id", r.Id));
        foreach (var assignment in assignments)
            Run(c, t, "INSERT INTO iam.user_roles(user_account_id,role_id,granted_by,department_id,valid_until) VALUES(@id,@role,@user,@department::uuid,@until::timestamptz);", r,
                ("id", r.Id), ("role", assignment.Role), ("department", assignment.Department), ("until", assignment.Until));
        if (One(c, t, "SELECT jsonb_build_object('id',u.id) FROM iam.user_accounts u WHERE u.organization_id=@org AND u.account_status='active' AND iam.permission_granted(@org,u.id,'organization.manage') LIMIT 1;", r) is null)
            throw Error(409, "LAST_ADMINISTRATOR", "At least one active administrator must remain.");
        Run(c, t, "UPDATE core.objects SET version=version+1,updated_at=statement_timestamp(),updated_by=@user WHERE organization_id=@org AND id=@id;", r, ("id", r.Id));
        BumpScope(c, t, r, r.Id!.Value);
        var changed = new JsonObject { ["id"] = r.Id, ["version"] = Version(target) + 1 };
        Record(c, t, r, "user_account", r.Id.Value, Version(target) + 1, target, changed);
        return new(new JsonArray(Many(c, t, "SELECT jsonb_build_object('userAccountId',user_account_id,'roleId',role_id,'departmentId',department_id,'validUntil',valid_until,'grantedAt',granted_at,'grantedBy',granted_by) FROM iam.user_roles WHERE user_account_id=@id ORDER BY role_id;", r, ("id", r.Id)).ToArray<JsonNode?>()), Version: Version(target) + 1);
    }
}
