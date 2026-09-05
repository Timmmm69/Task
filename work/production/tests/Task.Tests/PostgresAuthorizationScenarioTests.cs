using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Application.Security;
using Task.Infrastructure.Postgres;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;
using System.Text.Json;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public void Authorization_DepartmentAndExpiryDoNotBecomeGlobalPermanentGrants()
    {
        using var db = Database.Create(); if (db is null) return;
        var admin = Guid.NewGuid(); var department = Guid.NewGuid(); var otherDepartment = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name,is_system) VALUES($1,$2,'system_administrator','Administrator',true);", admin, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code) SELECT $1,code FROM iam.permissions;", admin);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.User, admin);
        foreach (var id in new[] { department, otherDepartment })
            db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_at,updated_at,created_by,updated_by) VALUES($1,$2,'department',statement_timestamp(),statement_timestamp(),$3,$3);", id, db.Organization, db.User);
        db.Sql("UPDATE org.employee_profiles SET department_id=$1 WHERE id=(SELECT employee_profile_id FROM iam.user_accounts WHERE id=$2);", department, db.User);
        db.Sql("UPDATE org.employee_profiles SET department_id=$1 WHERE id=(SELECT employee_profile_id FROM iam.user_accounts WHERE id=$2);", otherDepartment, db.OtherUser);
        var privateContact = db.Call("contacts", "create", """{"firstName":"Own","displayName":"Other department"}""", user: db.OtherUser, admin: false);
        var project = db.Call("projects", "create", $$"""{"name":"Department A","ownerUserId":"{{db.User}}"}""");
        db.Call("projects", "member-add", $$"""{"userAccountId":"{{db.OtherUser}}","projectRoleId":"{{db.Role("system_manager")}}"}""", Id(project), 1);
        var until = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var body = $$"""{"roles":[{"roleId":"{{db.Role("system_manager")}}","departmentId":"{{department}}","validUntil":"{{until}}"}],"expectedUserVersion":1}""";
        var result = db.Call("user-roles", "replace", body, db.OtherUser, 1);
        Assert.Equal(department.ToString(), result.Body![0]!["departmentId"]!.ToString());
        Assert.NotNull(result.Body[0]!["validUntil"]);
        Assert.Equal(200, db.Call("projects", "get", id: Id(project), user: db.OtherUser, admin: false, revalidatePermissions: true).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("contacts", "get", id: Id(privateContact), user: db.OtherUser, admin: false, revalidatePermissions: true)).Status);
        db.Sql("UPDATE iam.user_roles SET valid_until=statement_timestamp()-interval '1 second' WHERE user_account_id=$1;", db.OtherUser);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("projects", "get", id: Id(project), user: db.OtherUser, admin: false, revalidatePermissions: true)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("user-roles", "replace", $$"""{"roles":[{"roleId":"{{admin}}","validUntil":"{{until}}"}],"expectedUserVersion":2}""", db.OtherUser, 2)).Status);
    }

    [Fact]
    public void Authorization_RevokedCapabilityCannotUseAnOldRequestSnapshot()
    {
        using var db = Database.Create(); if (db is null) return;
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.OtherUser, db.Role("system_observer"));
        var item = db.Call("catalog-items", "create", """{"name":"Owned","itemType":"file_reference"}""", user: db.OtherUser, admin: false);
        Assert.Equal(200, db.Call("catalog-items", "get", id: Id(item), user: db.OtherUser, revalidatePermissions: true).Status);
        db.Sql("DELETE FROM iam.user_roles WHERE user_account_id=$1;", db.OtherUser);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "get", id: Id(item), user: db.OtherUser, revalidatePermissions: true)).Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task Authorization_PersonalTasksCalendarAndRecurrenceArePrivate()
    {
        using var db = Database.Create(); if (db is null) return;
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.User, db.Role("system_employee"));
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.OtherUser, db.Role("system_employee"));
        var now = DateTimeOffset.UtcNow;
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Personal", now,
            schedule: TaskSchedule.Create(now.AddMinutes(1), now.AddHours(1)));
        db.Runtime.CreateTaskStore().Add(task);
        Assert.Null(await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, task.Metadata.Id, db.OtherUser));
        var events = db.Runtime.CreateCalendarEventStore();
        var item = CalendarEvent.Create(Guid.NewGuid(), db.Organization, db.User, null, "Personal calendar", null,
            CalendarEventTiming.CreateTimed(DateOnly.FromDateTime(now.UtcDateTime), now.AddMinutes(1), now.AddHours(1), "UTC"), now, [], []);
        events.AddForUser(item);
        Assert.Null(events.GetForUser(item.Metadata.Id, db.Organization, db.OtherUser));
        Assert.NotNull(events.GetForUser(item.Metadata.Id, db.Organization, db.User));
        Assert.Throws<KeyNotFoundException>(() => events.SaveForUser(item.Cancel(db.OtherUser, now.AddMinutes(2)), 1));
        var schedule = db.Runtime.CreateScheduleStore();
        Assert.Empty(schedule.QuerySchedule(db.Organization, now, now.AddDays(1), null, null, null, db.OtherUser));
        Assert.Equal(2, schedule.QuerySchedule(db.Organization, now, now.AddDays(1), null, null, null, db.User).Count);
        var recurrence = new RecurrenceService(db.Runtime.CreateRecurrenceStore());
        var definition = new RecurrenceDefinition { Status = "active", Frequency = "daily", Interval = 1,
            OccurrenceStartDate = DateOnly.FromDateTime(now.UtcDateTime), TimeZone = "UTC",
            Template = new() { Title = "Private series", AuthorUserId = db.User, Priority = "normal", AssigneeIds = [db.OtherUser] } };
        recurrence.Create(db.Organization, db.User, "private-series-1", JsonSerializer.Serialize(definition, RecurrenceService.JsonOptions));
        var series = Assert.Single(recurrence.List(db.Organization, db.User));
        recurrence.Generate(db.Organization, db.User, series.Id, series.Version, "generate-private", definition.OccurrenceStartDate);
        var occurrences = recurrence.GetOccurrences(db.Organization, series.Id, db.User);
        Assert.NotEmpty(occurrences);
        var occurrence = occurrences[0];
        Assert.NotNull(await db.Runtime.CreateTaskReadStore().GetVisibleByIdAsync(db.Organization, occurrence.TaskId, db.OtherUser));
        Assert.Empty(recurrence.List(db.Organization, db.OtherUser));
        Assert.Equal(404, Assert.Throws<RecurrenceRequestException>(() => recurrence.Get(db.Organization, series.Id, db.OtherUser)).Status);
        Assert.Equal(404, Assert.Throws<RecurrenceRequestException>(() => recurrence.SetStatus(db.Organization, db.OtherUser, series.Id, 1, "other-cancel", "cancelled")).Status);
    }

    [Fact]
    public void Authorization_PrivateObjectsStayHiddenAcrossEveryProjection()
    {
        using var db = Database.Create(); if (db is null) return;
        var contact = db.Call("contacts", "create", """{"firstName":"Secret","displayName":"Secret contact"}""");
        var file = db.Call("catalog-items", "create", """{"name":"Secret file","itemType":"file_reference"}""");
        var interaction = db.Call("interactions", "create", $$"""{"counterpartyObjectId":"{{Id(contact)}}","interactionType":"call","occurredAt":"2026-09-05T09:00:00Z","subject":"Secret interaction"}""");
        foreach (var (resource, item) in new[] { ("contacts", contact), ("catalog-items", file), ("interactions", interaction) })
        {
            Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call(resource, "get", id: Id(item), user: db.OtherUser, admin: false)).Status);
            Assert.Empty(db.Call(resource, "list", user: db.OtherUser, admin: false).Body!["items"]!.AsArray());
        }
        Assert.Empty(db.Call("search", "search", user: db.OtherUser, admin: false, query: new() { ["q"] = "Secret" }).Body!["items"]!.AsArray());
        var options = db.Call("tasks", "task-options", user: db.OtherUser, admin: false).Body!;
        Assert.Empty(options["files"]!.AsArray()); Assert.Empty(options["counterparties"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "locations", id: Id(file), user: db.OtherUser, admin: false)).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "resolve", id: Id(file), user: db.OtherUser, admin: false)).Status);
        db.Call("contacts", "archive", id: Id(contact), version: 1);
        Assert.Empty(db.Call("archive", "list", user: db.OtherUser, admin: false).Body!["items"]!.AsArray());
    }

    [Fact]
    public void Authorization_ProjectSharingRevocationAndDenyWinForOwnerAndAdmin()
    {
        using var db = Database.Create(); if (db is null) return;
        var project = db.Call("projects", "create", $$"""{"name":"Shared","ownerUserId":"{{db.User}}"}""");
        var role = db.Role("system_observer");
        db.Call("projects", "member-add", $$"""{"userAccountId":"{{db.OtherUser}}","projectRoleId":"{{role}}"}""", Id(project), 1);
        var folder = db.Call("catalog-items", "create", """{"name":"Shared folder","itemType":"virtual_folder"}""");
        var child = db.Call("catalog-items", "create", $$"""{"name":"Shared child","itemType":"text_note","noteContent":"Private","parentItemId":"{{Id(folder)}}"}""");
        db.Call("objects", "link-add", $$"""{"sourceObjectId":"{{Id(project)}}","targetObjectId":"{{Id(folder)}}","linkType":"project_file"}""", Id(project), 2);
        Assert.Equal(200, db.Call("catalog-items", "get", id: Id(child), user: db.OtherUser, admin: false).Status);
        Assert.Single(db.Call("catalog-items", "tree", user: db.OtherUser, admin: false, query: new() { ["depth"] = "2" }).Body!["items"]![0]!["children"]!.AsArray());
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "patch", """{"name":"Forbidden"}""", Id(child), 1, user: db.OtherUser, admin: false)).Status);
        db.Call("projects", "member-remove", id: Id(project), version: 3, child: db.OtherUser);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "get", id: Id(child), user: db.OtherUser, admin: false)).Status);
        Assert.Empty(db.Call("catalog-items", "tree", user: db.OtherUser, admin: false).Body!["items"]!.AsArray());
        db.Sql("INSERT INTO projects.members(organization_id,project_id,user_account_id,project_role_id,permission_overrides) VALUES($1,$2,$3,$4,'{\"allow\":[],\"deny\":[\"project.update\",\"filecatalog.read\"]}');", db.Organization, Id(project), db.User, role);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("projects", "patch", """{"name":"Denied owner"}""", Id(project), 4)).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "get", id: Id(child))).Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task Authorization_SystemRolesHaveExplicitMatrixAndDenyWins()
    {
        using var db = Database.Create(); if (db is null) return;
        var decision = new PermissionDecisionService(new PostgresAuthorizationPolicyStore(db.Source));
        foreach (var (role, allowed, denied) in new[] {
            ("system_observer", new[] {"task.read", "project.read", "filecatalog.read"}, new[] {"task.create", "task.update", "comment.create", "organization.manage", "user.manageroles"}),
            ("system_employee", new[] {"task.create", "task.changestatus", "comment.create", "contact.read"}, new[] {"project.create", "task.assign", "organization.manage", "user.manageroles"}),
            ("system_manager", new[] {"project.create", "task.update", "task.assign", "contact.update"}, new[] {"organization.manage", "user.manageroles", "identity.account.manage"}) })
        {
            db.Sql("DELETE FROM iam.user_roles WHERE user_account_id=$1;", db.OtherUser);
            db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.OtherUser, db.Role(role));
            foreach (var permission in allowed) Assert.True((await decision.EvaluateAsync(db.Organization, db.OtherUser, permission)).Allowed, role + ":" + permission);
            foreach (var permission in denied) Assert.False((await decision.EvaluateAsync(db.Organization, db.OtherUser, permission)).Allowed, role + ":" + permission);
        }
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code,effect) VALUES($1,'task.update','deny');", db.Role("system_observer"));
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.OtherUser, db.Role("system_observer"));
        Assert.Equal(AuthorizationDenyReason.ExplicitDeny, (await decision.EvaluateAsync(db.Organization, db.OtherUser, "task.update")).Reason);
        db.Sql("UPDATE iam.user_accounts SET account_status='blocked' WHERE id=$1;", db.OtherUser);
        Assert.False((await decision.EvaluateAsync(db.Organization, db.OtherUser, "task.read")).Allowed);
    }

    [Fact]
    public async System.Threading.Tasks.Task Authorization_RoleAssignmentIsVersionedAuditedAndCannotRemoveLastAdmin()
    {
        using var db = Database.Create(); if (db is null) return;
        var adminRole = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name,is_system) VALUES($1,$2,'system_administrator','Administrator',true);", adminRole, db.Organization);
        db.Sql("INSERT INTO iam.role_permissions(role_id,permission_code) SELECT $1,code FROM iam.permissions;", adminRole);
        db.Sql("INSERT INTO iam.user_roles(user_account_id,role_id) VALUES($1,$2);", db.User, adminRole);
        var payload = $$"""{"roles":[{"roleId":"{{db.Role("system_employee")}}"}],"expectedUserVersion":1}""";
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("user-roles", "replace", payload, db.OtherUser, 1, admin: false)).Status);
        var result = db.Call("user-roles", "replace", payload, db.OtherUser, 1, key: "role-assignment-1");
        Assert.Equal(2, result.Version); Assert.Single(result.Body!.AsArray());
        Assert.Equal(2, db.Call("user-roles", "replace", payload, db.OtherUser, 1, key: "role-assignment-1").Version);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("user-roles", "replace", payload, db.OtherUser, 1)).Status);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => db.Call("user-roles", "replace", """{"roles":[],"expectedUserVersion":1}""", db.User, 1)).Status);
        Assert.Equal(2, db.Count("iam.user_roles"));
        Assert.Equal(1, db.Count("governance.audit_entries")); Assert.Equal(1, db.Count("governance.outbox_messages"));
        var context = new IdentityCommandContext(db.Organization, db.User, null, Guid.NewGuid(), "user.block", "last-admin-block", System.Security.Cryptography.SHA256.HashData("last-admin"u8));
        Assert.Equal(IdentityCommandDisposition.InvalidStateTransition, (await db.Runtime.CreateUserAccountCommandStore()
            .TransitionAsync(context, db.User, 1, UserAccountTransition.Block, "test")).Disposition);
    }
}
