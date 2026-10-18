using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Application.Security;
using Task.Infrastructure.Postgres;

namespace Task.Tests;

public sealed class RequiresPostgresTheoryAttribute : TheoryAttribute
{
    public RequiresPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TaskCreateCommandTests.ConnectionEnvironmentVariable)))
            Skip = $"Set {TaskCreateCommandTests.ConnectionEnvironmentVariable} to execute the real PostgreSQL role gate.";
    }
}

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresTheory]
    [InlineData("foreign-user", 404)]
    [InlineData("foreign-role", 422)]
    [InlineData("foreign-department", 422)]
    [InlineData("duplicate", 422)]
    [InlineData("administrative-department", 422)]
    [InlineData("administrative-expiry", 422)]
    [InlineData("unknown-field", 422)]
    [InlineData("expired", 422)]
    [InlineData("version-body", 422)]
    public void Administration_InvalidRoleReplacementRollsBackAllAssignments(string scenario, int status)
    {
        using var db = Database.Create(); Assert.NotNull(db); // Never silently pass without PostgreSQL.
        var administrator = SeedAdministrator(db);
        var employee = db.Role("system_employee");
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.OtherUser, employee);
        var assignment = new JsonObject { ["roleId"] = employee };
        var roles = new JsonArray(assignment);
        var target = db.OtherUser;
        var version = 1;
        switch (scenario)
        {
            case "foreign-user": target = Guid.NewGuid(); break;
            case "foreign-role": assignment["roleId"] = Guid.NewGuid(); break;
            case "foreign-department": assignment["departmentId"] = Guid.NewGuid(); break;
            case "duplicate": roles.Add(assignment.DeepClone()); break;
            case "administrative-department": assignment["roleId"] = administrator; assignment["departmentId"] = SeedDepartment(db); break;
            case "administrative-expiry": assignment["roleId"] = administrator; assignment["validUntil"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O"); break;
            case "unknown-field": assignment["permissions"] = new JsonArray("organization.manage"); break;
            case "expired": assignment["validUntil"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"); break;
            case "version-body": version = 2; break;
        }
        var body = new JsonObject { ["roles"] = roles, ["expectedUserVersion"] = version };
        Assert.Equal(status, Assert.Throws<ProductApiException>(() => db.Call("user-roles", "replace", body.ToJsonString(), target, 1)).Status);
        Assert.Equal(2, db.Count("iam.user_roles"));
        using var query = db.Source.CreateCommand("SELECT role_id FROM iam.user_roles WHERE user_account_id=$1;");
        query.Parameters.AddWithValue(db.OtherUser);
        Assert.Equal(employee, query.ExecuteScalar());
        Assert.Equal(0, db.Count("governance.audit_entries"));
    }

    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task Administration_RoleReceiptPreservesScopesAndInvalidatesEffectivePermissionsOnce()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        SeedAdministrator(db);
        var department = SeedDepartment(db);
        var until = DateTimeOffset.UtcNow.AddDays(1);
        var employee = db.Role("system_employee"); var observer = db.Role("system_observer");
        var body = new JsonObject
        {
            ["expectedUserVersion"] = 1,
            ["roles"] = new JsonArray(new JsonObject { ["roleId"] = employee, ["departmentId"] = department, ["validUntil"] = until.ToString("O") },
                new JsonObject { ["roleId"] = observer }),
        }.ToJsonString();
        var result = db.Call("user-roles", "replace", body, db.OtherUser, 1, "admin-role-scope-1");
        Assert.Equal(2, result.Version);
        var assignments = result.Body!.AsArray(); Assert.Equal(2, assignments.Count);
        var scoped = assignments.Single(node => node!["roleId"]!.GetValue<Guid>() == employee)!;
        Assert.Equal(department, scoped["departmentId"]!.GetValue<Guid>());
        Assert.Equal(until.ToUnixTimeMilliseconds(), DateTimeOffset.Parse(scoped["validUntil"]!.GetValue<string>()).ToUnixTimeMilliseconds());
        Assert.NotNull(scoped["grantedAt"]); Assert.NotNull(scoped["grantedBy"]);
        long scopeVersion;
        using (var query = db.Source.CreateCommand("SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=$1;"))
        { query.Parameters.AddWithValue(db.OtherUser); scopeVersion = Convert.ToInt64(query.ExecuteScalar()); Assert.True(scopeVersion > 1); }
        Assert.Equal(2, db.Call("user-roles", "replace", body, db.OtherUser, 1, "admin-role-scope-1").Version);
        using (var query = db.Source.CreateCommand("SELECT version FROM iam.authorization_scope_versions WHERE user_account_id=$1;"))
        { query.Parameters.AddWithValue(db.OtherUser); Assert.Equal(scopeVersion, Convert.ToInt64(query.ExecuteScalar())); }
        var decision = new PermissionDecisionService(new PostgresAuthorizationPolicyStore(db.Source));
        Assert.True((await decision.EvaluateAsync(db.Organization, db.OtherUser, "task.read")).Allowed);
        db.Call("user-roles", "replace", """{"roles":[],"expectedUserVersion":2}""", db.OtherUser, 2, "admin-role-scope-2");
        Assert.False((await decision.EvaluateAsync(db.Organization, db.OtherUser, "task.read")).Allowed);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("roles", "list", user: db.OtherUser,
            revalidatePermissions: true)).Status);
    }

    private static Guid SeedAdministrator(Database db)
    {
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name,is_system) VALUES($1,$2,'system_administrator','Administrator',true);", role, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code) SELECT $1,code FROM iam.permissions;", role);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.User, role);
        return role;
    }
    private static Guid SeedDepartment(Database db)
    {
        var id = Guid.NewGuid();
        db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_at,updated_at,created_by,updated_by) VALUES($1,$2,'department',statement_timestamp(),statement_timestamp(),$3,$3);", id, db.Organization, db.User);
        return id;
    }
}
