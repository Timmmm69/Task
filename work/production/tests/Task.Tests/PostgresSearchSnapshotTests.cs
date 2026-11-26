using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public void SearchTaskPreviewUsesCardDescriptionAndKeepsLiteralTitleMatch()
    {
        using var db = Database.Create()!;
        var description = "Смета [1] со скидкой. " + new string('я', 280);
        var task = Domain.TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Смета [1]",
            DateTimeOffset.UtcNow, content: new() { Description = description });
        new PostgresTaskAggregateStore(db.Source).Add(task);
        var result = db.Call("search", "search", query: new Dictionary<string, string>
        { ["q"] = "[1]", ["types"] = "task" });
        var item = Assert.Single(result.Body!["items"]!.AsArray());
        Assert.Equal(task.Metadata.Id.ToString(), item!["objectId"]!.GetValue<string>());
        Assert.Equal(description[..240], item["description"]!.GetValue<string>());
    }

    [RequiresPostgresFact]
    public void SearchSuggestionsAndSinglePageDoNotWriteSnapshots()
    {
        using var db = Database.Create()!;
        var older = Id(db.Call("contacts", "create", """{"firstName":"Snapshot","displayName":"Snapshot older"}"""));
        var exact = Id(db.Call("contacts", "create", """{"firstName":"Snapshot","displayName":"Snapshot"}"""));
        db.Sql("UPDATE core.objects SET updated_at=created_at+interval '1 minute' WHERE id=$1", older);

        var query = new Dictionary<string, string> { ["q"] = "Snapshot", ["types"] = "contact", ["limit"] = "1" };
        var suggestions = db.Call("search", "suggestions", query: query).Body!.AsArray();
        Assert.Equal(exact.ToString(), Assert.Single(suggestions)!["objectId"]!.ToString());
        Assert.Equal(0, db.Count("core.product_search_snapshots"));

        query["limit"] = "2";
        var page = db.Call("search", "search", query: query).Body!;
        Assert.Equal(new[] { exact.ToString(), older.ToString() }, SearchIds(page["items"]!.AsArray()));
        Assert.Null(page["nextCursor"]);
        Assert.Equal(0, db.Count("core.product_search_snapshots"));

        var targets = db.Call("search", "link-targets", query: query).Body!;
        Assert.Equal(SearchIds(page["items"]!.AsArray()), SearchIds(targets["items"]!.AsArray()));
        Assert.Null(targets["nextCursor"]);
        Assert.Equal(0, db.Count("core.product_search_snapshots"));

        db.Sql("""
            INSERT INTO core.product_search_snapshots(id,organization_id,user_account_id,filter_hash,scope_version,results,expires_at)
            VALUES(gen_random_uuid(),$1,$2,'expired',1,'[]',statement_timestamp()-interval '1 minute');
            """, db.Organization, db.User);
        query["q"] = "Nothing matches this query";
        var empty = db.Call("search", "search", query: query).Body!;
        Assert.Empty(empty["items"]!.AsArray());
        Assert.Null(empty["nextCursor"]);
        Assert.Equal(1, db.Count("core.product_search_snapshots"));
    }

    [RequiresPostgresFact]
    public void SearchContinuesInOrderAndRechecksVisibilityAndCursorScope()
    {
        using var db = Database.Create()!;
        var exact = Id(db.Call("contacts", "create", """{"firstName":"Page","displayName":"Page"}"""));
        var older = Id(db.Call("contacts", "create", """{"firstName":"Page","displayName":"Page older"}"""));
        var newer = Id(db.Call("contacts", "create", """{"firstName":"Page","displayName":"Page newer"}"""));
        db.Sql("UPDATE core.objects SET updated_at=created_at+interval '1 minute' WHERE id=$1", newer);
        var query = new Dictionary<string, string> { ["q"] = "Page", ["types"] = "contact", ["limit"] = "1" };
        ProductApiResponse Search() => db.Call("search", "search", query: query);

        var first = Search().Body!;
        Assert.Equal(exact.ToString(), Assert.Single(SearchIds(first["items"]!.AsArray())));
        var cursor = first["nextCursor"]!.GetValue<string>();
        Assert.Matches("^[0-9a-f]{32}\\.1$", cursor);
        Assert.Equal(1, db.Count("core.product_search_snapshots"));

        query["cursor"] = cursor;
        var second = Search().Body!;
        Assert.Equal(newer.ToString(), Assert.Single(SearchIds(second["items"]!.AsArray())));
        query["cursor"] = second["nextCursor"]!.GetValue<string>();
        Assert.Equal(cursor[..^1] + "2", query["cursor"]);
        var third = Search().Body!;
        Assert.Equal(older.ToString(), Assert.Single(SearchIds(third["items"]!.AsArray())));
        Assert.Null(third["nextCursor"]);
        Assert.Equal(1, db.Count("core.product_search_snapshots"));

        db.Sql("UPDATE core.objects SET lifecycle_state='archived',archived_at=updated_at WHERE id=$1;", newer);
        query["cursor"] = cursor;
        var afterArchive = Search().Body!;
        Assert.Equal(older.ToString(), Assert.Single(SearchIds(afterArchive["items"]!.AsArray())));
        Assert.Null(afterArchive["nextCursor"]);
        AssertSearchError(410, "SEARCH_CURSOR_EXPIRED", () => db.Call("search", "search",
            query: query, user: db.OtherUser, session: db.OtherSession));

        query["q"] = "Changed";
        AssertSearchError(400, "SEARCH_CURSOR_INVALID", Search);
        query["q"] = "Page";
        query["cursor"] = "invalid";
        AssertSearchError(400, "SEARCH_CURSOR_INVALID", Search);
        query["cursor"] = cursor;
        db.Sql("INSERT INTO iam.authorization_scope_versions(user_account_id,version) VALUES($1,2) " +
            "ON CONFLICT(user_account_id) DO UPDATE SET version=iam.authorization_scope_versions.version+1;", db.User);
        AssertSearchError(410, "SEARCH_CURSOR_EXPIRED", Search);
    }

    [RequiresPostgresFact]
    public void SearchTooBroadReturnsValidationErrorWithoutWritingSnapshots()
    {
        using var db = Database.Create()!;
        var catalogItem = Id(db.Call("catalog-items", "create", """{"name":"Broad","itemType":"file_reference"}"""));
        // Many locations on one visible object exercise the candidate cap with real authorization.
        db.Sql("""
            INSERT INTO files.file_locations(id,organization_id,catalog_item_id,location_type,raw_path,owner_user_id)
            SELECT gen_random_uuid(),$1,$3,'unc_path',concat($4,n,'.txt'),$2
            FROM generate_series(1,10001) n;
            """, db.Organization, db.User, catalogItem, @"\\server\share\broad-");
        db.Sql("ANALYZE files.file_locations; ANALYZE files.catalog_items; ANALYZE core.objects;");
        var query = new Dictionary<string, string> { ["q"] = "Broad", ["types"] = "file_location", ["limit"] = "1" };
        foreach (var operation in new[] { "suggestions", "search" })
        {
            var error = AssertSearchError(422, "VALIDATION_FAILED", () => db.Call("search", operation, query: query,
                permissions: ["Search.Use", "FileCatalog.Read", "FileReference.Open"]));
            Assert.Equal("Search is too broad; refine the filters.", error.Message);
        }
        Assert.Equal(0, db.Count("core.product_search_snapshots"));
    }

    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task SearchSnapshotCleanupBatchesExpiredRowsAndKeepsLiveCursors()
    {
        using var db = Database.Create()!;
        db.Call("contacts", "create", """{"firstName":"Live","displayName":"Live"}""");
        var last = Id(db.Call("contacts", "create", """{"firstName":"Live","displayName":"Live last"}"""));
        var query = new Dictionary<string, string> { ["q"] = "Live", ["types"] = "contact", ["limit"] = "1" };
        ProductApiResponse Search() => db.Call("search", "search", query: query);
        var expired = Search().Body!["nextCursor"]!.GetValue<string>();
        var live = Search().Body!["nextCursor"]!.GetValue<string>();

        using var expiration = db.Source.CreateCommand("SELECT expires_at FROM core.product_search_snapshots WHERE id=$1");
        expiration.Parameters.AddWithValue(Guid.ParseExact(live.Split('.')[0], "N"));
        var expiresAt = (DateTime)expiration.ExecuteScalar()!;
        Assert.InRange(expiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(14.5), TimeSpan.FromMinutes(15));

        db.Sql("UPDATE core.product_search_snapshots SET expires_at=statement_timestamp()-interval '2 minutes' WHERE id=$1;",
            Guid.ParseExact(expired.Split('.')[0], "N"));
        db.Sql("""
            INSERT INTO core.product_search_snapshots(id,organization_id,user_account_id,filter_hash,scope_version,results,expires_at)
            SELECT gen_random_uuid(),$1,$2,'test',1,'[]',statement_timestamp()-interval '1 minute'
            FROM generate_series(1,$3);
            """, db.Organization, db.OtherUser, TaskPersistenceRuntime.SearchSnapshotCleanupBatchSize);
        query["cursor"] = expired;
        AssertSearchError(410, "SEARCH_CURSOR_EXPIRED", Search);

        Assert.Equal(TaskPersistenceRuntime.SearchSnapshotCleanupBatchSize, await db.ApiRuntime.PurgeExpiredSearchSnapshotsAsync());
        Assert.Equal(2, db.Count("core.product_search_snapshots"));
        Assert.Equal(1, await db.ApiRuntime.PurgeExpiredSearchSnapshotsAsync());
        Assert.Equal(0, await db.ApiRuntime.PurgeExpiredSearchSnapshotsAsync());
        Assert.Equal(1, db.Count("core.product_search_snapshots"));
        AssertSearchError(410, "SEARCH_CURSOR_EXPIRED", Search);
        query["cursor"] = live;
        Assert.Equal(last.ToString(), Assert.Single(SearchIds(Search().Body!["items"]!.AsArray())));
    }

    [RequiresPostgresFact]
    public void TaskSearchPreservesLiteralAndShortSubstringsAcrossMigration()
    {
        using var db = Database.Create(schemaVersion: 19)!;
        Guid AddTask(string title)
        {
            var id = Guid.NewGuid();
            db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES ($1,$2,'task',$3,$3,statement_timestamp(),statement_timestamp());",
                id, db.Organization, db.User);
            db.Sql("INSERT INTO work.tasks(id,organization_id,title) VALUES ($1,$2,$3);", id, db.Organization, title);
            return id;
        }

        var exact = AddTask("Ключ%_!");
        var partial = AddTask("Старый ключ%_! в заголовке");
        AddTask("КЛЮЧАБ!");
        var contact = Id(db.Call("contacts", "create", """{"firstName":"Cursor","displayName":"Cursor"}"""));
        var olderContact = Id(db.Call("contacts", "create", """{"firstName":"Cursor","displayName":"Cursor last"}"""));
        var oldQuery = new Dictionary<string, string> { ["q"] = "Cursor", ["types"] = "contact", ["limit"] = "1" };
        var oldPage = db.Call("search", "search", query: oldQuery).Body!;
        Assert.Equal(contact.ToString(), Assert.Single(SearchIds(oldPage["items"]!.AsArray())));
        oldQuery["cursor"] = oldPage["nextCursor"]!.GetValue<string>();

        db.Runtime.CreateMigrator().ApplyPending();

        var continuation = db.Call("search", "search", query: oldQuery).Body!;
        Assert.Equal(olderContact.ToString(), Assert.Single(SearchIds(continuation["items"]!.AsArray())));
        Assert.Null(continuation["nextCursor"]);

        var query = new Dictionary<string, string>
        {
            ["q"] = "КЛЮЧ%_!",
            ["types"] = "task",
            ["limit"] = "10",
            ["lifecycle"] = "active",
            ["userIds"] = db.User.ToString(),
        };
        JsonArray Search() => db.Call("search", "search", query: query).Body!["items"]!.AsArray();
        Assert.Equal(new[] { exact.ToString(), partial.ToString() }, SearchIds(Search()));

        query["q"] = "%_";
        Assert.Equal(new[] { exact.ToString(), partial.ToString() }.Order(), SearchIds(Search()).Order());
        query["q"] = "_";
        var suggestions = db.Call("search", "suggestions", query: query).Body!.AsArray();
        Assert.Equal(new[] { exact.ToString(), partial.ToString() }.Order(), SearchIds(suggestions).Order());
    }

    [RequiresPostgresFact]
    public void SearchFillsPageAcrossWindowsAndKeepsTheOriginalCursorPosition()
    {
        using var db = Database.Create()!;
        var exact = Id(db.Call("contacts", "create", """{"firstName":"Window","displayName":"Window"}"""));
        var tail = Id(db.Call("contacts", "create", """{"firstName":"Window","displayName":"Window tail"}"""));
        var last = Id(db.Call("contacts", "create", """{"firstName":"Window","displayName":"Window last"}"""));
        db.Sql("UPDATE core.objects SET updated_at=created_at+interval '1 minute' WHERE id=$1;", tail);
        db.Sql("""
            WITH filler AS (
                INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at)
                SELECT gen_random_uuid(),$1,'contact',$2,$2,statement_timestamp(),statement_timestamp()+interval '2 minutes'
                FROM generate_series(1,70) RETURNING id,organization_id
            )
            INSERT INTO crm.contacts(id,organization_id,first_name,display_name)
            SELECT id,organization_id,'Window','Window filler' FROM filler;
            """, db.Organization, db.User);

        var query = new Dictionary<string, string> { ["q"] = "Window", ["types"] = "contact", ["limit"] = "1" };
        JsonNode Search() => db.Call("search", "search", query: query).Body!;
        var first = Search();
        Assert.Equal(exact.ToString(), Assert.Single(SearchIds(first["items"]!.AsArray())));
        var cursor = first["nextCursor"]!.GetValue<string>();
        db.Sql("""
            UPDATE core.objects o SET lifecycle_state='archived',archived_at=updated_at
            FROM crm.contacts c WHERE o.organization_id=c.organization_id AND o.id=c.id AND c.display_name='Window filler';
            """);

        query["cursor"] = cursor;
        var second = Search();
        Assert.Equal(tail.ToString(), Assert.Single(SearchIds(second["items"]!.AsArray())));
        query["cursor"] = second["nextCursor"]!.GetValue<string>();
        Assert.Equal(cursor[..^1] + "72", query["cursor"]);
        var third = Search();
        Assert.Equal(last.ToString(), Assert.Single(SearchIds(third["items"]!.AsArray())));
        Assert.Null(third["nextCursor"]);

        query["cursor"] = cursor[..^1] + "73";
        Assert.Empty(Search()["items"]!.AsArray());
        query["cursor"] = cursor[..^1] + int.MaxValue;
        AssertSearchError(400, "SEARCH_CURSOR_INVALID", () => db.Call("search", "search", query: query));
    }

    [RequiresPostgresFact]
    public void SearchWindowRechecksFileDeviceAndVersionAndOmitsPaths()
    {
        using var db = Database.Create()!;
        var firstItem = Id(db.Call("catalog-items", "create", """{"name":"Window file","itemType":"file_reference"}"""));
        var localItem = Id(db.Call("catalog-items", "create", """{"name":"Window file local","itemType":"file_reference"}"""));
        var sharedItem = Id(db.Call("catalog-items", "create", """{"name":"Window file shared","itemType":"file_reference"}"""));
        Guid AddUncLocation(Guid catalogItem, string rawPath)
        {
            var id = Guid.NewGuid();
            db.Sql("""
                INSERT INTO files.file_locations(id,organization_id,catalog_item_id,location_type,raw_path,owner_user_id)
                VALUES ($1,$2,$3,'unc_path',$4,$5);
                """, id, db.Organization, catalogItem, rawPath, db.User);
            return id;
        }

        var first = AddUncLocation(firstItem, @"\\server\share\first.txt");
        var local = Id(db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\local.txt"}""", localItem, 1));
        var shared = AddUncLocation(sharedItem, @"\\server\share\shared.txt");
        db.Sql("UPDATE core.objects SET updated_at=created_at+interval '1 minute' WHERE id=$1;", localItem);
        var otherDevice = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        db.Sql("INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) VALUES ($1,$2,'device',$3,$3,statement_timestamp(),statement_timestamp());",
            otherDevice, db.Organization, db.User);
        db.Sql("INSERT INTO iam.devices(id,organization_id,user_account_id,device_fingerprint_hash) VALUES ($1,$2,$3,$4);",
            otherDevice, db.Organization, db.User, otherDevice.ToString("N"));
        db.Sql("""
            INSERT INTO iam.sessions(id,organization_id,user_account_id,device_id,credential_version,authorization_scope_version,idle_expires_at,absolute_expires_at)
            VALUES ($1,$2,$3,$4,1,1,statement_timestamp()+interval '1 day',statement_timestamp()+interval '2 days');
            """, otherSession, db.Organization, db.User, otherDevice);

        var query = new Dictionary<string, string> { ["q"] = "Window file", ["types"] = "file_location", ["limit"] = "1" };
        var permissions = new[] { "Search.Use", "FileCatalog.Read", "FileReference.Open" };
        JsonNode Search(Guid session) => db.Call("search", "search", query: query, permissions: permissions, session: session).Body!;
        void AssertHit(Guid id, JsonNode page)
        {
            var row = Assert.Single(page["items"]!.AsArray())!;
            Assert.Equal(id.ToString(), row["objectId"]!.ToString());
            Assert.Null(row["rawPath"]);
        }

        var initial = Search(db.Session);
        AssertHit(first, initial);
        query["cursor"] = initial["nextCursor"]!.GetValue<string>();
        AssertHit(local, Search(db.Session));
        var fromOtherDevice = Search(otherSession);
        AssertHit(shared, fromOtherDevice);
        Assert.Null(fromOtherDevice["nextCursor"]);

        db.Sql("UPDATE files.file_locations SET version=version+1 WHERE id=$1;", local);
        var afterChange = Search(db.Session);
        AssertHit(shared, afterChange);
        Assert.Null(afterChange["nextCursor"]);
    }

    private static string[] SearchIds(JsonArray items) =>
        items.Select(item => item!["objectId"]!.GetValue<string>()).ToArray();

    private static ProductApiException AssertSearchError(int status, string code, Func<ProductApiResponse> call)
    {
        var error = Assert.Throws<ProductApiException>(() => call());
        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
        return error;
    }
}
