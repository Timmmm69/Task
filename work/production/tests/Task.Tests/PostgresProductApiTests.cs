using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using Npgsql;
using Task.Application.ProductData;
using Task.Domain;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public void ProductApi_ProjectsCrmCatalogLifecycleAndIdempotency()
    {
        using var db = Database.Create(); if (db is null) return;
        var project = db.Call("projects", "create", $$"""{"name":"Alpha","ownerUserId":"{{db.User}}"}""", key: "project-create-001");
        var projectId = Id(project);
        Assert.Equal(201, project.Status); Assert.Equal(1, project.Version);
        Assert.True(JsonNode.DeepEquals(project.Body, db.Call("projects", "create", $$"""{"name":"Alpha","ownerUserId":"{{db.User}}"}""", key: "project-create-001").Body));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", Assert.Throws<ProductApiException>(() => db.Call("projects", "create", $$"""{"name":"Other","ownerUserId":"{{db.User}}"}""", key: "project-create-001")).Code);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("projects", "get", id: projectId, user: db.OtherUser, admin: false)).Status);
        Assert.Empty(db.Call("projects", "list", user: db.OtherUser, admin: false).Body!["items"]!.AsArray());
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("projects", "patch", "{\"organizationId\":null}", projectId, 1)).Status);
        var updated = db.Call("projects", "patch", "{\"name\":\"Beta\"}", projectId, 1);
        Assert.Equal(2, updated.Version);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("projects", "patch", "{\"name\":\"stale\"}", projectId, 1)).Status);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => db.Call("projects", "archive", "{}", projectId, 2)).Status);
        db.Call("projects", "patch", "{\"status\":\"completed\",\"actualEndAt\":\"2026-09-04T10:00:00Z\"}", projectId, 2);
        db.Call("projects", "archive", "{}", projectId, 3);
        Assert.Single(db.Call("archive", "list").Body!["items"]!.AsArray());
        db.Call("archive", "unarchive", "{}", projectId, 4);
        db.Call("projects", "trash", "{}", projectId, 5);
        Assert.Empty(db.Call("projects", "list").Body!["items"]!.AsArray());
        Assert.Single(db.Call("trash", "list").Body!["items"]!.AsArray());
        db.Call("trash", "restore", "{}", projectId, 6);

        var contact = db.Call("contacts", "create", "{\"firstName\":\"Anna\",\"displayName\":\"Anna Smith\"}");
        var contactId = Id(contact);
        var channel = db.Call("contacts", "channel-add", "{\"channelType\":\"email\",\"value\":\"anna@example.test\"}", contactId, 1);
        Assert.Equal(201, channel.Status);
        Assert.Single(db.Call("contacts", "get", id: contactId).Body!["channels"]!.AsArray());
        db.Call("contacts", "channel-patch", "{\"value\":\"new@example.test\"}", contactId, 2, child: Id(channel));
        db.Call("contacts", "channel-remove", "{}", contactId, 3, child: Id(channel));
        db.Call("contacts", "address-add", "{\"addressType\":\"work\",\"formattedAddress\":\"Minsk\"}", contactId, 4);
        var company = db.Call("companies", "create", "{\"name\":\"Acme\"}");
        db.Call("companies", "contact-link", $$"""{"companyId":"{{Id(company)}}","contactId":"{{contactId}}"}""", Id(company), 1);
        Assert.Single(db.Call("companies", "get", id: Id(company)).Body!["contacts"]!.AsArray());
        db.Call("companies", "contact-unlink", "{}", Id(company), 2, child: contactId);
        var folder = db.Call("catalog-items", "create", "{\"name\":\"Root\",\"itemType\":\"virtual_folder\"}");
        var nested = db.Call("catalog-items", "create", $$"""{"name":"Nested","itemType":"virtual_folder","parentItemId":"{{Id(folder)}}"}""");
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "move", $$"""{"parentItemId":"{{Id(nested)}}"}""", Id(folder), 1)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "create", "{\"name\":\"Unsafe\",\"itemType\":\"web_link\",\"webUrl\":\"javascript:alert(1)\"}")).Status);
        var tree = db.Call("catalog-items", "tree", query: new() { ["depth"] = "2" });
        Assert.Single(tree.Body!["items"]![0]!["children"]!.AsArray());
        Assert.True(db.Count("governance.domain_events") > 10);
        Assert.Equal(db.Count("governance.domain_events"), db.Count("governance.outbox_messages"));
    }

    [Fact]
    public void ProductApi_SettingsNotificationsAndDiscovery()
    {
        using var db = Database.Create(); if (db is null) return;
        var settings = db.Call("user-settings", "get"); Assert.Equal(1, settings.Version);
        Assert.Equal("ru-RU", settings.Body!["language"]!.GetValue<string>());
        var updated = db.Call("user-settings", "patch", "{\"language\":\"en-US\",\"allowLocalPaths\":false}", version: 1, key: "settings-change-001");
        Assert.Equal(2, updated.Version);
        Assert.False(db.Call("user-settings", "get").Body!["allowLocalPaths"]!.GetValue<bool>());
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("user-settings", "patch", "{\"language\":\"ru-RU\"}", version: 1)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("user-settings", "patch", "{\"weekendDays\":[6,6]}", version: 2)).Status);
        var prefs = db.Call("preferences", "patch", "{\"enabled\":true,\"desktopEnabled\":true,\"soundEnabled\":false,\"defaultSnoozeMinutes\":15}", version: 2);
        Assert.Equal(3, prefs.Version); Assert.False(prefs.Body!["soundEnabled"]!.GetValue<bool>());
        db.Call("organization-settings", "patch", "{\"trashRetentionDays\":45}", version: 1);
        Assert.Equal(45, db.Call("organization-settings", "get").Body!["trashRetentionDays"]!.GetValue<int>());
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var notification = new NotificationSnapshot(SyncableEntityMetadata.Create(Guid.NewGuid(), db.Organization, db.User, now), db.User,
            "task_assigned", null, "Notice", "Body", NotificationSeverity.Info, NotificationStatus.Pending, now, null, null, null, null, null, "{}");
        db.Runtime.CreateNotificationStore().Add(notification);
        var id = notification.Metadata.Id;
        Assert.Single(db.Call("notifications", "list").Body!["items"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("notifications", "get", id: id, user: db.OtherUser)).Status);
        Assert.Equal(2, db.Call("notifications", "read", "{}", id, 1).Version);
        Assert.Equal(3, db.Call("notifications", "action", "{\"action\":\"dismiss\",\"expectedVersion\":2}", id, 2).Version);
        Assert.Equal(0, db.Call("notifications", "read-all", new JsonObject { ["notificationIds"] = new JsonArray(id.ToString()) }.ToJsonString()).Body!["updatedCount"]!.GetValue<int>());
        db.Call("contacts", "create", "{\"firstName\":\"Search\",\"displayName\":\"Find me\"}");
        var search = db.Call("search", "search", query: new() { ["q"] = "Find" });
        Assert.Single(search.Body!["items"]!.AsArray());
        Assert.Empty(db.Call("search", "search", query: new() { ["q"] = "Find" }, permissions: ["Search.Use"]).Body!["items"]!.AsArray());
        for (var i = 0; i < 4; i++) db.Call("contacts", "create", $$"""{"firstName":"Page","displayName":"Page {{i}}"}""");
        var page = db.Call("contacts", "list", query: new() { ["limit"] = "2" });
        var cursor = page.Body!["nextCursor"]!.GetValue<string>();
        var next = db.Call("contacts", "list", query: new() { ["limit"] = "2", ["cursor"] = cursor });
        Assert.Equal(2, next.Body!["items"]!.AsArray().Count);
        Assert.NotEqual(page.Body!["items"]![0]!["id"]!.ToString(), next.Body!["items"]![0]!["id"]!.ToString());
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("contacts", "list", query: new() { ["limit"] = "3", ["cursor"] = cursor })).Status);
    }

    [Fact]
    public void ProductApi_MembershipAndFilePathSafety()
    {
        using var db = Database.Create(); if (db is null) return;
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name) VALUES($1,$2,'member','Member');", role, db.Organization);
        var project = db.Call("projects", "create", $$"""{"name":"Private","ownerUserId":"{{db.User}}"}""");
        var member = db.Call("projects", "member-add", $$"""{"userAccountId":"{{db.OtherUser}}","projectRoleId":"{{role}}"}""", Id(project), 1);
        Assert.Equal(201, member.Status);
        Assert.Equal(200, db.Call("projects", "get", id: Id(project), user: db.OtherUser, admin: false).Status);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("projects", "patch", "{\"name\":\"Denied\"}", Id(project), 2, user: db.OtherUser, admin: false)).Status);
        var overrides = db.Call("projects", "member-overrides", """{"allow":[],"deny":[],"expectedMemberVersion":1}""", Id(project), 1, child: db.OtherUser);
        Assert.Equal(2, overrides.Version);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("projects", "member-overrides", """{"allow":[],"deny":[],"expectedMemberVersion":1}""", Id(project), 1, child: db.OtherUser)).Status);
        db.Call("projects", "member-remove", "{}", Id(project), 3, child: db.OtherUser);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("projects", "get", id: Id(project), user: db.OtherUser, admin: false)).Status);
        var item = db.Call("catalog-items", "create", "{\"name\":\"Contract\",\"itemType\":\"file_reference\"}");
        var local = db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\contract.docx"}""", Id(item), 1);
        Assert.Null(local.Body!["rawPath"]);
        Assert.NotNull(db.Call("catalog-items", "locations", id: Id(item)).Body![0]!["rawPath"]);
        var hidden = db.Call("catalog-items", "locations", id: Id(item), user: db.OtherUser, admin: false, session: db.OtherSession, permissions: ["FileReference.Open"]);
        Assert.Null(hidden.Body![0]!["rawPath"]);
        Assert.False(hidden.Body![0]!["canOpenOnDevice"]!.GetValue<bool>());
        var resolved = db.Call("catalog-items", "resolve", "{}", Id(item));
        Assert.False(resolved.Body!["physicalOperationPerformed"]!.GetValue<bool>());
        var network = db.Call("network-resources", "create", """{"name":"Docs","rootUncPath":"\\\\server\\docs"}""");
        var unc = db.Call("catalog-items", "location-add", $$"""{"locationType":"unc_path","rawPath":"\\\\server\\docs\\file.pdf","networkResourceId":"{{Id(network)}}"}""", Id(item), 2);
        Assert.Equal(201, unc.Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-add", $$"""{"locationType":"unc_path","rawPath":"\\\\evil\\other\\secret.pdf","networkResourceId":"{{Id(network)}}"}""", Id(item), 3)).Status);
        db.Call("catalog-items", "location-remove", "{}", Id(item), 3, child: Id(local));
        Assert.Single(db.Call("catalog-items", "locations", id: Id(item)).Body!.AsArray());
    }

    private static Guid Id(ProductApiResponse response) => response.Body!["id"]!.Deserialize<Guid>();

    [Fact]
    public async System.Threading.Tasks.Task ProductApi_ConcurrentWritesAndSearchSnapshotsAreSafe()
    {
        using var db = Database.Create(); if (db is null) return;
        var creates = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 6).Select(_ => System.Threading.Tasks.Task.Run(() =>
            db.Call("contacts", "create", """{"firstName":"Concurrent","displayName":"Concurrent one"}""", key: "parallel-create-001"))));
        Assert.Single(creates.Select(Id).Distinct()); Assert.Equal(1, db.Count("crm.contacts"));
        var id = Id(creates[0]);
        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 4).Select(i => System.Threading.Tasks.Task.Run(() =>
        {
            try { db.Call("contacts", "patch", new JsonObject { ["displayName"] = "Concurrent " + i }.ToJsonString(), id, 1); return 200; }
            catch (ProductApiException error) { return error.Status; }
        })));
        Assert.Single(results, status => status == 200); Assert.Equal(3, results.Count(status => status == 412));
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("contacts", "create", """{"firstName":true,"displayName":"Wrong"}""")).Status);
        Assert.Equal(1, db.Count("crm.contacts"));
        for (var i = 0; i < 3; i++) db.Call("contacts", "create", new JsonObject { ["firstName"] = "Concurrent", ["displayName"] = "Concurrent item " + i }.ToJsonString());
        var first = db.Call("search", "search", query: new() { ["q"] = "Concurrent", ["limit"] = "2" }, permissions: ["Search.Use", "Contact.Read", "Task.Read", "Calendar.Read"]);
        var cursor = first.Body!["nextCursor"]!.GetValue<string>();
        db.Call("contacts", "create", """{"firstName":"Concurrent","displayName":"Concurrent new after snapshot"}""");
        var next = db.Call("search", "search", query: new() { ["q"] = "Concurrent", ["limit"] = "2", ["cursor"] = cursor }, permissions: ["Search.Use", "Contact.Read", "Task.Read", "Calendar.Read"]);
        Assert.Equal(2, next.Body!["items"]!.AsArray().Count); Assert.Null(next.Body!["nextCursor"]);
        Assert.Equal(400, Assert.Throws<ProductApiException>(() => db.Call("search", "search", query: new() { ["q"] = "Changed", ["limit"] = "2", ["cursor"] = cursor })).Status);
        using var foreign = Database.Create()!;
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => foreign.Call("contacts", "get", id: id)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("projects", "create", new JsonObject { ["name"] = "Cross tenant", ["ownerUserId"] = foreign.User }.ToJsonString())).Status);
    }

    [Fact]
    public void ProductApi_LinksInteractionsAndFileChecksRoundTrip()
    {
        using var db = Database.Create(); if (db is null) return;
        var contact = db.Call("contacts", "create", """{"firstName":"Anna","displayName":"Anna"}""");
        var file = db.Call("catalog-items", "create", """{"name":"Anna contract","itemType":"file_reference"}""");
        var location = db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\contract.docx"}""", Id(file), 1);
        var linkBody = new JsonObject { ["sourceObjectId"] = Id(contact), ["targetObjectId"] = Id(file), ["linkType"] = "contact_file" }.ToJsonString();
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("objects", "link-add", linkBody, Id(contact), 1,
            admin: false, permissions: ["Contact.Read", "FileCatalog.Read", "ObjectLink.Create"])).Status);
        var link = db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = Id(contact), ["targetObjectId"] = Id(file), ["linkType"] = "contact_file" }.ToJsonString(), Id(contact), 1);
        Assert.Single(db.Call("objects", "links", id: Id(contact)).Body!["items"]!.AsArray());
        Assert.Empty(db.Call("objects", "links", id: Id(contact), permissions: ["Contact.Read", "ObjectLink.Read"]).Body!["items"]!.AsArray());
        Assert.Single(db.Call("search", "search", query: new() { ["q"] = "Anna", ["types"] = "file_location" }).Body!["items"]!.AsArray());
        Assert.Empty(db.Call("search", "search", user: db.OtherUser, session: db.OtherSession, admin: false,
            permissions: ["Search.Use", "FileCatalog.Read", "FileReference.Open"], query: new() { ["q"] = "Anna", ["types"] = "file_location" }).Body!["items"]!.AsArray());
        Assert.Equal(2, db.Call("search", "search", permissions: ["Search.Use", "Employee.Read"], query: new() { ["q"] = "API", ["types"] = "employee_profile" }).Body!["items"]!.AsArray().Count);
        var search = db.Call("search", "search", query: new() { ["q"] = "Anna", ["types"] = "contact", ["hasFiles"] = "true" });
        Assert.Single(search.Body!["items"]!.AsArray());
        var interaction = db.Call("interactions", "create", new JsonObject
        {
            ["counterpartyObjectId"] = Id(contact),
            ["interactionType"] = "call",
            ["occurredAt"] = DateTimeOffset.UtcNow,
            ["subject"] = "Anna meeting",
            ["participantObjectIds"] = new JsonArray(Id(contact).ToString())
        }.ToJsonString());
        Assert.Equal(201, interaction.Status);
        Assert.Single(db.Call("search", "search", query: new() { ["q"] = "Anna", ["types"] = "interaction", ["contactIds"] = Id(contact).ToString() }).Body!["items"]!.AsArray());
        db.Call("interactions", "participants", """{"participantObjectIds":[]}""", Id(interaction), 1);
        var device = db.DeviceFor(db.Session);
        var check = db.Call("file-locations", "check", new JsonObject { ["deviceId"] = device, ["status"] = "available", ["checkedAt"] = DateTimeOffset.UtcNow, ["expectedLocationVersion"] = 1 }.ToJsonString(), Id(location));
        Assert.Equal(204, check.Status); Assert.Null(check.Body); Assert.Equal(1, db.Count("files.location_checks"));
        db.Call("objects", "link-remove", "{}", Id(contact), 2, child: Id(link));
        Assert.Empty(db.Call("objects", "links", id: Id(contact)).Body!["items"]!.AsArray());
        for (var i = 0; i < 3; i++)
        {
            var related = db.Call("contacts", "create", new JsonObject { ["firstName"] = "Related", ["displayName"] = "Related " + i }.ToJsonString());
            db.Call("objects", "link-add", new JsonObject { ["sourceObjectId"] = Id(contact), ["targetObjectId"] = Id(related), ["linkType"] = "related" }.ToJsonString(), Id(contact), 3 + i);
        }
        var page = db.Call("objects", "links", id: Id(contact), query: new() { ["limit"] = "2" });
        Assert.Equal(2, page.Body!["items"]!.AsArray().Count);
        var next = db.Call("objects", "links", id: Id(contact), query: new() { ["limit"] = "2", ["cursor"] = page.Body!["nextCursor"]!.GetValue<string>() });
        Assert.Single(next.Body!["items"]!.AsArray()); Assert.False(next.Body!["hasMore"]!.GetValue<bool>());
    }

    private sealed class Database : IDisposable
    {
        private readonly NpgsqlDataSource admin;
        private readonly NpgsqlDataSource source;
        private readonly string name;
        private readonly TaskPersistenceRuntime apiRuntime;
        public Guid Organization { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid OtherUser { get; } = Guid.NewGuid();
        public Guid Session { get; } = Guid.NewGuid();
        public Guid OtherSession { get; } = Guid.NewGuid();
        public TaskPersistenceRuntime Runtime { get; }
        public static Database? Create() => Environment.GetEnvironmentVariable("TASK_POSTGRES_TEST_ADMIN_CONNECTION") is { Length: > 0 } connection ? new(connection) : null;
        private Database(string connection)
        {
            name = "task_api04_" + Guid.NewGuid().ToString("N"); admin = NpgsqlDataSource.Create(connection);
            using (var command = admin.CreateCommand($"CREATE DATABASE {name};")) command.ExecuteNonQuery();
            var runtimeConnection = new NpgsqlConnectionStringBuilder(connection) { Database = name }.ConnectionString;
            source = NpgsqlDataSource.Create(runtimeConnection); Runtime = new(runtimeConnection);
            Runtime.CreateMigrator().ApplyPending();
            Sql("INSERT INTO core.organizations(id,code,name,default_time_zone) VALUES($1,$2,'API04 tests','UTC');", Organization, Organization.ToString("N"));
            SeedUser(User, Session); SeedUser(OtherUser, OtherSession);
            Sql($"CREATE ROLE {name}_role LOGIN PASSWORD 'api04-test-only' NOSUPERUSER NOCREATEDB NOCREATEROLE;");
            var assembly = typeof(PostgresProductApiTests).Assembly;
            using var grants = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("grant-runtime.sql")))!;
            using var reader = new StreamReader(grants);
            Sql(string.Join('\n', reader.ReadToEnd().Split('\n').Where(line => !line.TrimStart().StartsWith('\\'))).Replace("task_runtime", name + "_role"));
            apiRuntime = new(new NpgsqlConnectionStringBuilder(runtimeConnection) { Username = name + "_role", Password = "api04-test-only" }.ConnectionString);
        }
        private void SeedUser(Guid user, Guid session)
        {
            var profile = Guid.NewGuid(); var device = Guid.NewGuid();
            Sql("INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by) VALUES " +
                "($1,$4,'employee_profile',clock_timestamp(),$2,clock_timestamp(),$2),($2,$4,'user_account',clock_timestamp(),$2,clock_timestamp(),$2),($3,$4,'device',clock_timestamp(),$2,clock_timestamp(),$2);", profile, user, device, Organization);
            Sql("INSERT INTO org.employee_profiles(id,organization_id,first_name,last_name,display_name,preferred_time_zone) VALUES($1,$2,'API','User','API User','UTC');", profile, Organization);
            Sql("INSERT INTO iam.user_accounts(id,organization_id,employee_profile_id,login,password_hash,password_parameters) VALUES($1,$2,$3,$4,$5,'{}');", user, Organization, profile, user.ToString("N"), new string('h', 64));
            Sql("INSERT INTO iam.devices(id,organization_id,user_account_id,device_fingerprint_hash) VALUES($1,$2,$3,$4);", device, Organization, user, device.ToString("N"));
            Sql("INSERT INTO iam.sessions(id,organization_id,user_account_id,device_id,credential_version,authorization_scope_version,idle_expires_at,absolute_expires_at) VALUES($1,$2,$3,$4,1,1,clock_timestamp()+interval '1 day',clock_timestamp()+interval '2 days');", session, Organization, user, device);
        }
        public ProductApiResponse Call(string resource, string operation, string body = "{}", Guid? id = null, int? version = null,
            string? key = null, Guid? child = null, Guid? user = null, bool admin = true, Dictionary<string, string>? query = null,
            string[]? permissions = null, Guid? session = null)
        {
            var route = ProductApiRoutes.All.First(route => route.Resource == resource && route.Operation == operation);
            var granted = (permissions ?? ProductApiRoutes.All.Select(route => route.Permission).ToArray()).ToHashSet();
            if (admin) granted.Add("organization.manage");
            return apiRuntime.CreateProductApiStore().Execute(new(Organization, user ?? User, session ?? Session, Guid.NewGuid(),
                route, id, child, JsonNode.Parse(body)!.AsObject(), query ?? new(), version, key,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body + version))), granted));
        }
        public void Sql(string sql, params object[] args)
        { using var command = source.CreateCommand(sql); foreach (var arg in args) command.Parameters.Add(new NpgsqlParameter { Value = arg }); command.ExecuteNonQuery(); }
        public long Count(string table)
        { using var command = source.CreateCommand($"SELECT count(*) FROM {table};"); return (long)command.ExecuteScalar()!; }
        public Guid DeviceFor(Guid session)
        { using var command = source.CreateCommand("SELECT device_id FROM iam.sessions WHERE id=$1"); command.Parameters.Add(new NpgsqlParameter { Value = session }); return (Guid)command.ExecuteScalar()!; }
        public void Dispose()
        {
            apiRuntime.Dispose(); Runtime.Dispose(); source.Dispose();
            using (var command = admin.CreateCommand($"DROP DATABASE {name} WITH (FORCE);")) command.ExecuteNonQuery();
            using (var command = admin.CreateCommand($"DROP ROLE {name}_role;")) command.ExecuteNonQuery();
            admin.Dispose();
        }
    }
}
