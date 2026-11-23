using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
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
