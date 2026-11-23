using System.Diagnostics;
using System.Text.Json.Nodes;
using Task.Application;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async global::System.Threading.Tasks.Task TodayProjection_FiltersParticipationAndKeepsCanonicalPlanning()
    {
        using var db = Database.Create()!;
        var store = db.ApiRuntime.CreateTaskReadStore();
        var from = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
        var day = new TaskTodayWindow(DateOnly.FromDateTime(from.UtcDateTime), from, from.AddDays(1));
        var own = new JsonObject { ["requesterUserId"] = db.User };
        var unrelated = int.TryParse(Environment.GetEnvironmentVariable("TASK_TODAY_AUDIT_ROWS"), out var requested)
            ? Math.Clamp(requested, 250, 10000) : 250;
        SeedMany(db.OtherUser, own, unrelated);
        var participating = new[]
        {
            Seed("Author", db.User, new()),
            Seed("Assignee", db.OtherUser, new() { ["assigneeIds"] = new JsonArray(JsonValue.Create(db.User.ToString("D"))) }),
            Seed("Watcher", db.OtherUser, new() { ["watcherIds"] = new JsonArray(JsonValue.Create(db.User.ToString("D"))) }, status: "review"),
        };
        var planned = new[]
        {
            Seed("Date only", db.OtherUser, new() { ["requesterUserId"] = db.User, ["scheduledDate"] = "2026-11-23" }),
            Seed("Cross midnight by planned duration", db.OtherUser,
                new() { ["requesterUserId"] = db.User, ["plannedDurationMinutes"] = 90 }, from.AddMinutes(-30)),
        };
        Seed("Legacy deadline point", db.OtherUser, own, deadline: from.AddHours(13));
        Seed("Terminal legacy row", db.OtherUser, own, from.AddHours(12), status: "cancelled");
        Seed("Completed participation outside the day", db.User, new(), status: "completed");
        Seed("Next day", db.OtherUser, own, day.ToUtc);
        Seed("Ends at midnight", db.OtherUser, new() { ["requesterUserId"] = db.User, ["plannedDurationMinutes"] = 60 }, from.AddHours(-1));
        Seed("Hidden even in the window", db.OtherUser, new(), from.AddHours(12));
        var archived = Seed("Archived", db.User, new());
        db.Sql("UPDATE core.objects SET lifecycle_state='archived',archived_at=updated_at WHERE id=$1;", archived);

        var otherOrg = Guid.NewGuid();
        db.Sql("INSERT INTO core.organizations(id,code,name,default_time_zone) VALUES($1,$2,'Other tenant','UTC');", otherOrg, otherOrg.ToString("N"));
        Seed("Foreign tenant", db.User, new(), from.AddHours(12), organization: otherOrg);

        var legacyRequest = new TaskReadPageRequest(db.Organization, db.User, 1);
        var timer = Stopwatch.StartNew();
        var legacy = await ReadAll(legacyRequest);
        var legacyMs = timer.Elapsed.TotalMilliseconds;
        var todayRequest = legacyRequest with { Today = day };
        timer.Restart();
        var projected = await ReadAll(todayRequest);
        var todayMs = timer.Elapsed.TotalMilliseconds;
        var wanted = participating.Concat(planned).ToHashSet();
        Assert.Equal(unrelated + 10, legacy.Items.Count);
        Assert.Equal(wanted.Count, projected.Items.Count);
        Assert.Equal(legacy.Items.Where(task => wanted.Contains(task.Id)).Select(task => task.Id), projected.Items.Select(task => task.Id));
        Assert.Equal(1, projected.Pages);
        output.WriteLine($"Visible tasks {legacy.Items.Count}; legacy {legacy.Pages} pages / {legacyMs:F1} ms; Today {projected.Pages} page / {todayMs:F1} ms / {projected.Items.Count} tasks.");

        SeedMany(db.User, new(), 201);
        var first = await store.GetPageAsync(todayRequest);
        Assert.Equal(PostgresTaskReadStore.TodayPageSize, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var next = await store.GetPageAsync(todayRequest with { Cursor = first.NextCursor });
        Assert.Equal(6, next.Items.Count);
        Assert.Empty(first.Items.Select(task => task.Id).Intersect(next.Items.Select(task => task.Id)));
        await Assert.ThrowsAsync<TaskReadCursorException>(() => store.GetPageAsync(legacyRequest with { Cursor = first.NextCursor }));
        await Assert.ThrowsAsync<TaskReadCursorException>(() => store.GetPageAsync(todayRequest with
        {
            Cursor = first.NextCursor,
            Today = new(day.Date.AddDays(1), day.FromUtc.AddDays(1), day.ToUtc.AddDays(1)),
        }));

        async global::System.Threading.Tasks.Task<(List<TaskReadProjection> Items, int Pages)> ReadAll(TaskReadPageRequest request)
        {
            var items = new List<TaskReadProjection>();
            var pages = 0;
            do
            {
                var page = await store.GetPageAsync(request);
                items.AddRange(page.Items);
                request = request with { Cursor = page.NextCursor };
                pages++;
            } while (request.Cursor is not null);
            return (items, pages);
        }

        void SeedMany(Guid author, JsonObject card, int count) => db.Sql("""
            WITH objects AS (
                INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by)
                SELECT gen_random_uuid(),$1,'task',statement_timestamp(),$2,statement_timestamp(),$2
                FROM generate_series(1,$3) RETURNING id,organization_id
            )
            INSERT INTO work.tasks(id,organization_id,title,card_content)
            SELECT id,organization_id,'Visible background task',$4::jsonb FROM objects;
            """, db.Organization, author, count, card.ToJsonString());

        Guid Seed(string title, Guid author, JsonObject card, DateTimeOffset? start = null, DateTimeOffset? deadline = null,
            string status = "new", Guid? organization = null)
        {
            var id = Guid.NewGuid();
            db.Sql("""
                WITH item AS (
                    INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by)
                    VALUES($1,$2,'task',statement_timestamp(),$3,statement_timestamp(),$3) RETURNING id,organization_id
                )
                INSERT INTO work.tasks(id,organization_id,title,card_content,start_at_utc,deadline_at,status,completed_at,completed_by)
                SELECT id,organization_id,$4,$5::jsonb,nullif($6,'')::timestamptz,nullif($7,'')::timestamptz,$8,
                    CASE WHEN $8='completed' THEN statement_timestamp() END, CASE WHEN $8='completed' THEN $3 END FROM item;
                """, id, organization ?? db.Organization, author, title, card.ToJsonString(),
                start?.ToString("O") ?? "", deadline?.ToString("O") ?? "", status);
            return id;
        }
    }
}
