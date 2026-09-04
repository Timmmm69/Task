using Task.Domain;
namespace Task.Tests;

public sealed class TaskCardContentTests
{
    [Fact]
    public void CardSurvivesEveryLifecycleCopyAndNoOpDoesNotAdvanceVersion()
    {
        var user = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var content = new TaskCardContent { Description = "Контекст", AssigneeIds = [user], WatcherIds = [Guid.NewGuid()], PlannedDurationMinutes = 45, ScheduledDate = new(2026, 9, 4) };
        var task = TaskAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), user, "Задача", now, content: content);
        Assert.Same(task, task.UpdateEditableFields(user, now, null, null, default, default, content.Apply("{\"description\":\"Контекст\"}")));
        task = task.Start(user, now.AddSeconds(1)).SubmitForReview(user, now.AddSeconds(2)).Complete(user, now.AddSeconds(3));
        Assert.Equal(content.ToJson(), task.Content.ToJson()); Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal(content.ToJson(), task.Archive(user, now.AddSeconds(4)).Content.ToJson());
    }
    [Fact]
    public void PatchPreservesOmittedFieldsAndClearsOnlyNullableValues()
    {
        var content = new TaskCardContent { Description = "До", ProjectId = Guid.NewGuid(), AssigneeIds = [Guid.NewGuid()] };
        var changed = content.Apply("{\"description\":null}");
        Assert.Null(changed.Description); Assert.Equal(content.ProjectId, changed.ProjectId); Assert.Equal(content.AssigneeIds, changed.AssigneeIds);
        Assert.Throws<ArgumentException>(() => content.Apply("{\"watcherIds\":null}").Validate(null));
        Assert.Throws<ArgumentException>(() => content.Apply("{\"plannedDurationMinutes\":10081}").Validate(null));
        Assert.Throws<ArgumentException>(() => content.Apply("{\"authorUserId\":null}"));
    }
    [Fact]
    public void ScheduleRequiresConsistentLocalAndUtcFields()
    {
        var card = new TaskCardContent { ScheduledDate = new(2026, 9, 4), StartTimeLocal = new(9, 30), ScheduleTimeZone = "Europe/Minsk" };
        card.Validate(new DateTimeOffset(2026, 9, 4, 6, 30, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => card.Validate(new DateTimeOffset(2026, 9, 4, 7, 30, 0, TimeSpan.Zero)));
        Assert.Throws<ArgumentException>(() => (card with { ScheduledDate = null }).Validate(null));
        Assert.Throws<ArgumentException>(() => (card with { ScheduleTimeZone = "Bad/Zone" }).Validate(null));
    }
}
