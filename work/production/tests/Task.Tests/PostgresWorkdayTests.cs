using Task.Application;
using Task.Domain;
using Task.Infrastructure.Persistence;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public async System.Threading.Tasks.Task WorkdayAndWeek_UseCutoffActualCompletionsAndCurrentVisibility()
    {
        using var db = Database.Create();
        Assert.NotNull(db);
        var date = new DateOnly(2026, 11, 27);
        var start = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        var day = new TaskTodayWindow(date, start, start.AddDays(1));
        var end = start.AddHours(18);
        var writes = new PostgresTaskAggregateStore(db.Source);
        TaskAggregate Add(string title, Guid creator, TaskSchedule? schedule = null, TaskCardContent? card = null)
        {
            var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, creator, title, start.AddDays(-1), schedule: schedule, content: card);
            writes.Add(task);
            return task;
        }
        var done = Add("Проверить поставку", db.User, card: new() { ScheduledDate = date });
        writes.Save(done.Complete(db.User, end.AddMinutes(-1)), 1);
        Add("Только срок", db.User, TaskSchedule.Create(null, end));
        Add("Ночная работа", db.User, TaskSchedule.Create(start.AddMinutes(-15), null),
            new() { ScheduledDate = date.AddDays(-1), StartTimeLocal = new(23, 45), ScheduleTimeZone = "UTC", PlannedDurationMinutes = 30 });
        var other = Add("Чужая задача", db.OtherUser, card: new() { ScheduledDate = date });
        writes.Save(other.Complete(db.OtherUser, end.AddMinutes(-10)), 1);
        var store = db.ApiRuntime.CreateTaskReadStore();
        var days = Assert.IsAssignableFrom<IWorkdayReadStore>(store);
        var weeks = Assert.IsAssignableFrom<IWorkweekReadStore>(store);
        Assert.Equal(new WorkdayResult(1, 2), await days.ReadDayAsync(db.Organization, db.User, day, end, default));
        var week = await weeks.ReadWeekAsync(db.Organization, db.User, start.AddDays(-4), end, default);
        Assert.Equal(1, week.Total);
        Assert.Equal("Проверить поставку", Assert.Single(week.Items).Title);

        // A later move must never be reported as if it were the plan at 18:00.
        var moved = Add("Перенос", db.User, card: new() { ScheduledDate = date });
        db.Sql("UPDATE core.objects SET updated_at=$2,version=2 WHERE id=$1", moved.Metadata.Id, end.AddHours(1));
        db.Sql("UPDATE work.tasks SET card_content=jsonb_set(card_content,'{scheduledDate}',to_jsonb('2026-11-30'::text)) WHERE id=$1", moved.Metadata.Id);
        Assert.Null(await days.ReadDayAsync(db.Organization, db.User, day, end, default));
        db.Sql("""
            INSERT INTO iam.idempotency_records (id,organization_id,user_account_id,operation_id,idempotency_key,request_hash,state,
              resource_id,response_status,response_headers,response_body,completed_at,retention_expires_at)
            VALUES ($1,$2,$3,'task.create','workday-test-001',decode(repeat('00',32),'hex'),'completed',$4,201,'{}',
              jsonb_build_object('version',1,'status','new','updatedAt',$5::timestamptz,'scheduledDate','2026-11-27'),
              $5,$5::timestamptz+interval '1 day')
            """, Guid.NewGuid(), db.Organization, db.User, moved.Metadata.Id, start);
        Assert.Equal(new WorkdayResult(1, 3), await days.ReadDayAsync(db.Organization, db.User, day, end, default));
        Assert.Equal("UTC", db.Call("user-settings", "get").Body!["timeZone"]!.GetValue<string>());
    }
}
