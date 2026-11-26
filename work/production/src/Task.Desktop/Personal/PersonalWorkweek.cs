using Task.Application;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    internal WorkweekResults ReadWeek(DateTimeOffset from, DateTimeOffset through) => Locked(() =>
    {
        using var command = Command("""
            SELECT title, completed_at, count(*) OVER () FROM tasks
            WHERE trashed_at IS NULL AND completed_at IS NOT NULL AND status=$completed
              AND julianday(completed_at) BETWEEN julianday($from) AND julianday($through)
            ORDER BY julianday(completed_at) DESC, id LIMIT 2
            """, null, ("$from", Instant(from)), ("$through", Instant(through)), ("$completed", (int)TaskApi.DesktopTaskStatus.Completed));
        var items = new List<WeeklyCompletion>();
        var total = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read()) { items.Add(new(reader.GetString(0), ReadInstant(reader, 1)!.Value)); total = reader.GetInt32(2); }
        return new WorkweekResults(total, items);
    });
}
