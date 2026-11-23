using System.Text;
using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Domain;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests(ITestOutputHelper output)
{
    [RequiresPostgresFact]
    public void NotificationPollReturnsChangesAndRechecksLiveVisibility()
    {
        using var db = Database.Create()!;
        var store = db.ApiRuntime.CreateProductApiStore();
        var source = Id(db.Call("contacts", "create", """{"firstName":"Poll","displayName":"Poll source"}"""));
        var now = DateTimeOffset.UtcNow;
        var old = Add(db.User, source);
        var expires = Add(db.User, null);
        var future = Add(db.User, null, now.AddDays(1));
        var foreign = Add(db.OtherUser, null);
        var first = Poll(db, store);
        Assert.True(Flag(first, "reset"));
        Assert.Equal(new[] { old, expires }.Order(), PollIds(first).Order());
        Assert.True(Flag(first, "autostartEnabled"));
        Assert.True(Flag(first["preferences"]!, "soundEnabled"));
        var revision = PollRevision(first);

        var idle = Poll(db, store, revision);
        Assert.False(Flag(idle, "reset"));
        Assert.Equal(revision, PollRevision(idle));
        Assert.Empty(PollIds(idle));

        db.Call("notifications", "read", id: old);
        var next = Add(db.User, null);
        db.Call("preferences", "patch", """{"soundEnabled":false}""", version: 1);
        db.Call("user-settings", "patch", """{"autostartEnabled":false}""", version: 1);
        var changed = Poll(db, store, revision);
        Assert.False(Flag(changed, "reset"));
        Assert.Equal(new[] { old, next }.Order(), PollIds(changed).Order());
        Assert.Equal("read", changed["items"]!.AsArray().Single(item => item!["id"]!.ToString() == old.ToString())!["status"]!.GetValue<string>());
        Assert.False(Flag(changed, "autostartEnabled"));
        Assert.False(Flag(changed["preferences"]!, "soundEnabled"));

        db.Call("contacts", "trash", id: source, version: 1);
        db.Sql("UPDATE notify.notifications SET expires_at=statement_timestamp() WHERE id=$1", expires);
        db.Sql("UPDATE notify.notifications SET not_before=statement_timestamp() WHERE id=$1", future);
        db.Sql("UPDATE notify.notifications SET status='dismissed',dismissed_at=statement_timestamp() WHERE id=$1", next);
        var removed = Poll(db, store, PollRevision(changed));
        Assert.Equal(new[] { old, expires }.Order(), removed["removedIds"]!.AsArray().Select(id => Guid.Parse(id!.ToString())).Order());
        Assert.Equal(new[] { next, future }.Order(), PollIds(removed).Order());
        Assert.Equal("dismissed", removed["items"]!.AsArray().Single(item => item!["id"]!.ToString() == next.ToString())!["status"]!.GetValue<string>());
        Assert.DoesNotContain(foreign, PollIds(removed));

        var other = Poll(db, store, PollRevision(removed), user: db.OtherUser, session: db.OtherSession);
        Assert.True(Flag(other, "reset"));
        Assert.Equal(foreign, Assert.Single(PollIds(other)));
        var gap = Poll(db, db.ApiRuntime.CreateProductApiStore(), PollRevision(removed));
        Assert.True(Flag(gap, "reset"));
        Assert.Equal(new[] { next, future }.Order(), PollIds(gap).Order());

        Guid Add(Guid recipient, Guid? target, DateTimeOffset? due = null)
        {
            var notification = new NotificationSnapshot(SyncableEntityMetadata.Create(Guid.NewGuid(), db.Organization, db.User, now),
                recipient, "task_assigned", target, "Poll title", "Poll body", NotificationSeverity.Info, NotificationStatus.Pending,
                due ?? now.AddMinutes(-5), null, null, null, null, null, "{}");
            db.Runtime.CreateNotificationStore().Add(notification);
            return notification.Metadata.Id;
        }
    }

    [RequiresPostgresFact]
    public void NotificationPollKeepsSettingsAndNotificationsIndependentlyAuthorized()
    {
        using var db = Database.Create()!;
        var store = db.ApiRuntime.CreateProductApiStore();
        var settings = Poll(db, store, permissions: ["Settings.ReadOwn"]);
        Assert.False(Flag(settings, "notificationsIncluded"));
        Assert.Null(settings["revision"]);
        Assert.True(Flag(settings, "autostartEnabled"));
        Assert.NotNull(settings["preferences"]);

        var notifications = Poll(db, store, permissions: ["Notification.ReadOwn"]);
        Assert.True(Flag(notifications, "notificationsIncluded"));
        Assert.NotNull(notifications["revision"]);
        Assert.Null(notifications["preferences"]);
        Assert.Null(notifications["autostartEnabled"]);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => Poll(db, store, permissions: [])).Status);
    }

    [RequiresPostgresFact]
    public void NotificationPollMatchesPagedHistoryAndAvoidsReturningUnchangedPayloads()
    {
        using var db = Database.Create()!;
        var store = db.ApiRuntime.CreateProductApiStore();
        var empty = Poll(db, store);
        var emptySteady = Poll(db, store, PollRevision(empty));
        var legacyEmptyBytes = Bytes(db.Call("notifications", "list").Body!) + Bytes(db.Call("preferences", "get").Body!) +
            Bytes(db.Call("user-settings", "get").Body!);
        output.WriteLine($"0 notifications, JSON payload per steady pass: {legacyEmptyBytes} bytes in 3 responses; {Bytes(emptySteady)} bytes in 1 response.");
        Seed(1000);
        var expected = new List<JsonNode?>();
        var query = new Dictionary<string, string> { ["limit"] = "200" };
        var calls = 2;
        var legacyBytes = Bytes(db.Call("preferences", "get").Body!) + Bytes(db.Call("user-settings", "get").Body!);
        while (true)
        {
            var history = db.Call("notifications", "list", query: query).Body!;
            calls++;
            legacyBytes += Bytes(history);
            expected.AddRange(history["items"]!.AsArray().Select(item => item!.DeepClone()));
            if (history["nextCursor"] is not { } cursor) break;
            query["cursor"] = cursor.ToString();
        }
        Assert.Equal(7, calls);
        var first = Poll(db, store);
        Assert.True(JsonNode.DeepEquals(new JsonArray(expected.ToArray()), first["items"]));
        var unchanged = Poll(db, store, PollRevision(first));
        Assert.Empty(PollIds(unchanged));
        Assert.True(Bytes(unchanged) < legacyBytes / 100);
        output.WriteLine($"1000 notifications, JSON payload per steady pass: {legacyBytes} bytes in {calls} responses; {Bytes(unchanged)} bytes in 1 response.");

        Seed(1001);
        var large = Poll(db, store, PollRevision(first));
        Assert.True(Flag(large, "historyRequired"));
        Assert.Null(large["revision"]);
        Assert.Empty(PollIds(large));

        static int Bytes(JsonNode body) => Encoding.UTF8.GetByteCount(body.ToJsonString());
        void Seed(int count) => db.Sql("""
            WITH objects AS (
                INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by)
                SELECT gen_random_uuid(),$1,'notification',statement_timestamp(),$2,statement_timestamp(),$2
                FROM generate_series(1,$3) RETURNING id,organization_id
            )
            INSERT INTO notify.notifications(id,organization_id,recipient_user_id,notification_type,title,body,not_before)
            SELECT id,organization_id,$2,'task_assigned','Synthetic notice','Synthetic body',statement_timestamp()-interval '1 hour' FROM objects;
            """, db.Organization, db.User, count);
    }

    private static JsonObject Poll(Database db, IProductApiStore store, string? revision = null, Guid? user = null,
        Guid? session = null, string[]? permissions = null)
    {
        var route = ProductApiRoutes.All.Single(route => route.Resource == "notifications" && route.Operation == "poll");
        var query = new Dictionary<string, string>();
        if (revision is not null) query["revision"] = revision;
        return store.Execute(new(db.Organization, user ?? db.User, session ?? db.Session, Guid.NewGuid(), route,
            null, null, new JsonObject(), query, null, null, "",
            (permissions ?? ["Notification.ReadOwn", "Settings.ReadOwn", "organization.manage"]).ToHashSet())).Body!.AsObject();
    }

    private static string PollRevision(JsonObject poll) => poll["revision"]!.GetValue<string>();
    private static IEnumerable<Guid> PollIds(JsonObject poll) => poll["items"]!.AsArray().Select(item => Guid.Parse(item!["id"]!.ToString()));
    private static bool Flag(JsonNode node, string key) => node[key]!.GetValue<bool>();
}
