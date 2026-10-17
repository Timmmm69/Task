using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public void LinkTargetsCoverRelatedTypesWithoutSearchUseAndRecheckOwnership()
    {
        using var db = Database.Create()!;
        var source = Id(db.Call("contacts", "create", """{"firstName":"Source","displayName":"Source"}"""));
        var network = Id(db.Call("network-resources", "create", """{"name":"Picker network","rootUncPath":"\\\\server\\picker"}"""));
        var inbox = Id(db.Call("inbox-items", "create", """{"itemType":"note","title":"Picker inbox","content":"Note"}"""));
        db.Call("inbox-items", "create", """{"itemType":"note","title":"Picker foreign","content":"Private"}""", user: db.OtherUser);
        var now = DateTimeOffset.UtcNow;
        NotificationSnapshot Notice(Guid recipient) => new(SyncableEntityMetadata.Create(Guid.NewGuid(), db.Organization, db.User, now),
            recipient, "task_assigned", null, "Picker notification", "Private body", NotificationSeverity.Info,
            NotificationStatus.Pending, now, null, null, null, null, null, "{}");
        var own = Notice(db.User); db.Runtime.CreateNotificationStore().Add(own);
        db.Runtime.CreateNotificationStore().Add(Notice(db.OtherUser));
        var permissions = new[] { "ObjectLink.Create", "FileCatalog.Read", "Inbox.ReadOwn", "Notification.ReadOwn" };
        var query = new Dictionary<string, string> { ["q"] = "Picker", ["types"] = "network_resource,notification,inbox_item", ["limit"] = "1" };
        ProductApiResponse Targets(Dictionary<string, string> filters) => db.Call("search", "link-targets", query: filters, permissions: permissions);
        var ids = new List<Guid>(); var page = Targets(query);
        while (true)
        {
            var row = Assert.Single(page.Body!["items"]!.AsArray())!;
            ids.Add(Guid.Parse(row["objectId"]!.ToString()));
            Assert.Null(row["body"]); Assert.Null(row["content"]); Assert.Null(row["rootUncPath"]);
            Assert.Equal("active", row["lifecycleState"]!.ToString());
            if (page.Body["nextCursor"] is not { } cursor) break;
            query["cursor"] = cursor.ToString(); page = Targets(query);
        }
        Assert.Equal(3, ids.Count); Assert.Equal(3, ids.Distinct().Count());
        Assert.Contains(network, ids); Assert.Contains(inbox, ids); Assert.Contains(own.Metadata.Id, ids);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("search", "link-targets", query: query,
            permissions: ["FileCatalog.Read"])).Status);
        Assert.Empty(db.Call("search", "link-targets", query: new() { ["q"] = "Picker", ["types"] = "inbox_item,notification" },
            permissions: ["ObjectLink.Create"]).Body!["items"]!.AsArray());
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("search", "search", query:
            new() { ["q"] = "Picker", ["types"] = "network_resource" })).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("search", "link-targets", query:
            new() { ["q"] = "Picker", ["types"] = "file_location" })).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("search", "link-targets", query:
            new() { ["q"] = "Picker", ["types"] = "inbox_item", ["lifecycle"] = "completed" })).Status);
        foreach (var target in ids)
            Assert.Equal(201, db.Call("objects", "link-add", new JsonObject
            { ["sourceObjectId"] = source, ["targetObjectId"] = target, ["linkType"] = "related" }.ToJsonString(),
                source, ids.IndexOf(target) + 1).Status);

        // A cached page must recheck the current owner even without a scope-version bump.
        var cachedQuery = new Dictionary<string, string> { ["q"] = "Picker", ["types"] = "network_resource,inbox_item", ["limit"] = "1" };
        var cached = Targets(cachedQuery);
        var firstId = Guid.Parse(cached.Body!["items"]![0]!["objectId"]!.ToString());
        if (firstId == inbox)
            db.Sql("UPDATE core.objects SET lifecycle_state='archived',archived_at=updated_at WHERE id=$1;", network);
        else
            db.Sql("UPDATE work.inbox_items SET owner_user_id=$1 WHERE id=$2;", db.OtherUser, inbox);
        cachedQuery["cursor"] = cached.Body["nextCursor"]!.ToString();
        Assert.Empty(Targets(cachedQuery).Body!["items"]!.AsArray());
    }

    [RequiresPostgresFact]
    public void ExistingLinksPageThroughAllFourHundredFiftyOneRows()
    {
        using var db = Database.Create()!;
        var source = Id(db.Call("contacts", "create", """{"firstName":"Source","displayName":"Source"}"""));
        db.Sql("WITH objects AS (INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by) " +
            "SELECT gen_random_uuid(),$1,'contact',statement_timestamp(),$2,statement_timestamp(),$2 FROM generate_series(1,451) RETURNING id,organization_id) " +
            "INSERT INTO crm.contacts(id,organization_id,first_name,display_name) SELECT id,organization_id,'Target','Target' FROM objects;", db.Organization, db.User);
        db.Sql("INSERT INTO core.object_links(id,organization_id,source_object_id,target_object_id,link_type,created_by) " +
            "SELECT gen_random_uuid(),$1,$2,id,'related',$3 FROM crm.contacts WHERE organization_id=$1 AND id<>$2;", db.Organization, source, db.User);
        var query = new Dictionary<string, string> { ["limit"] = "200" };
        var ids = new HashSet<string>(); var pages = 0;
        while (true)
        {
            var page = db.Call("objects", "links", id: source, query: query); pages++;
            Assert.Equal(1, page.Version);
            foreach (var row in page.Body!["items"]!.AsArray()) Assert.True(ids.Add(row!["id"]!.ToString()));
            if (!page.Body["hasMore"]!.GetValue<bool>()) break;
            query["cursor"] = page.Body["nextCursor"]!.ToString();
        }
        Assert.Equal(3, pages); Assert.Equal(451, ids.Count);
    }

    [RequiresPostgresFact]
    public void LinkTargetsAndGlobalSearchCannotShareCursors()
    {
        using var db = Database.Create()!;
        for (var i = 0; i < 2; i++) db.Call("contacts", "create", $$"""{"firstName":"Picker","displayName":"Picker {{i}}"}""");
        var query = new Dictionary<string, string> { ["q"] = "Picker", ["types"] = "contact", ["limit"] = "1" };
        var first = db.Call("search", "link-targets", query: query);
        query["cursor"] = first.Body!["nextCursor"]!.ToString();
        Assert.Equal("SEARCH_CURSOR_INVALID", Assert.Throws<ProductApiException>(() => db.Call("search", "search", query: query)).Code);
    }
}
