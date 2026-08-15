using Task.Domain;

namespace Task.Tests;

public sealed class TaskAggregateTests
{
    private static readonly Guid TaskId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid CreatorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid EditorId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsImmutableIdentityAuditFieldsAndNewStatus()
    {
        var task = TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "  Implement login screen  ", CreatedAt);

        Assert.Equal(TaskId, task.Metadata.Id);
        Assert.Equal(OrganizationId, task.Metadata.OrganizationId);
        Assert.Equal(CreatorId, task.Metadata.CreatedBy);
        Assert.Equal(CreatorId, task.Metadata.UpdatedBy);
        Assert.Equal(CreatedAt, task.Metadata.CreatedAtUtc);
        Assert.Equal(CreatedAt, task.Metadata.UpdatedAtUtc);
        Assert.Equal(1, task.Metadata.Version);
        Assert.Equal(EntityLifecycleState.Active, task.Metadata.LifecycleState);
        Assert.Equal("Implement login screen", task.Title);
        Assert.Equal(TaskWorkStatus.New, task.WorkStatus);
        Assert.Null(task.CompletedAtUtc);
        Assert.Null(task.CompletedBy);
    }

    [Fact]
    public void Create_RejectsEmptyOrWhitespaceTitle()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "", CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "   ", CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "\t", CreatedAt));
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiersAndNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(Guid.Empty, OrganizationId, CreatorId, "Title", CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(TaskId, Guid.Empty, CreatorId, "Title", CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Title",
                new DateTimeOffset(2026, 8, 15, 11, 30, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void Rename_UpdatesTitleAndIncrementsVersion()
    {
        var renamed = NewTask().Rename(EditorId, "  New title  ", CreatedAt.AddMinutes(5));

        Assert.Equal("New title", renamed.Title);
        Assert.Equal(2, renamed.Metadata.Version);
        Assert.Equal(EditorId, renamed.Metadata.UpdatedBy);
        Assert.Equal(CreatedAt.AddMinutes(5), renamed.Metadata.UpdatedAtUtc);
        Assert.Equal(TaskWorkStatus.New, renamed.WorkStatus);
        Assert.Equal(EntityLifecycleState.Active, renamed.Metadata.LifecycleState);
    }

    [Fact]
    public void Rename_RejectsEmptyOrWhitespaceTitle()
    {
        var task = NewTask();

        Assert.Throws<ArgumentException>(() => task.Rename(EditorId, "", CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => task.Rename(EditorId, "  ", CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Rename_IsAllowedWhileNonTerminalWorkInProgress()
    {
        var inProgress = NewTask().Start(CreatorId, CreatedAt.AddMinutes(1));
        var renamed = inProgress.Rename(EditorId, "Renamed in progress", CreatedAt.AddMinutes(2));

        Assert.Equal("Renamed in progress", renamed.Title);
        Assert.Equal(TaskWorkStatus.InProgress, renamed.WorkStatus);
        Assert.Equal(3, renamed.Metadata.Version);
    }

    [Fact]
    public void Rename_RejectedForTerminalWorkStatus()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => completed.Rename(EditorId, "New", CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Rename(EditorId, "New", CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Start_MovesNewToInProgress()
    {
        var started = NewTask().Start(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Equal(TaskWorkStatus.InProgress, started.WorkStatus);
        Assert.Equal(TaskWorkStatus.New, NewTask().WorkStatus);
        Assert.Equal(2, started.Metadata.Version);
    }

    [Fact]
    public void Start_ThrowsFromAnyNonNewStatus()
    {
        var inProgress = NewTask().Start(CreatorId, CreatedAt.AddMinutes(1));
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => inProgress.Start(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => completed.Start(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Start(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void SubmitForReview_MovesInProgressToReview()
    {
        var reviewed = NewTask()
            .Start(CreatorId, CreatedAt.AddMinutes(1))
            .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2));

        Assert.Equal(TaskWorkStatus.Review, reviewed.WorkStatus);
        Assert.Equal(3, reviewed.Metadata.Version);
    }

    [Fact]
    public void SubmitForReview_ThrowsFromAnyNonInProgressStatus()
    {
        var task = NewTask();
        var reviewed = task
            .Start(CreatorId, CreatedAt.AddMinutes(1))
            .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() => task.SubmitForReview(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => reviewed.SubmitForReview(EditorId, CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void Complete_FromAnyActiveNonTerminalStatusRecordsCompletionMetadata()
    {
        foreach (var task in new[]
                 {
                     NewTask(),
                     NewTask().Start(CreatorId, CreatedAt.AddMinutes(1)),
                     NewTask().Start(CreatorId, CreatedAt.AddMinutes(1))
                         .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2)),
                 })
        {
            var completedAt = CreatedAt.AddMinutes(3);
            var completed = task.Complete(EditorId, completedAt);

            Assert.Equal(TaskWorkStatus.Completed, completed.WorkStatus);
            Assert.Equal(completedAt, completed.CompletedAtUtc);
            Assert.Equal(EditorId, completed.CompletedBy);
            Assert.True(completed.Metadata.Version >= 2);
        }
    }

    [Fact]
    public void Complete_ThrowsForTerminalWorkStatus()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => completed.Complete(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Complete(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Cancel_FromAnyActiveNonTerminalStatusLeavesCompletionMetadataEmpty()
    {
        var cancelled = NewTask()
            .Start(CreatorId, CreatedAt.AddMinutes(1))
            .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2))
            .Cancel(EditorId, CreatedAt.AddMinutes(3));

        Assert.Equal(TaskWorkStatus.Cancelled, cancelled.WorkStatus);
        Assert.Null(cancelled.CompletedAtUtc);
        Assert.Null(cancelled.CompletedBy);
        Assert.Equal(4, cancelled.Metadata.Version);
    }

    [Fact]
    public void Cancel_ThrowsForTerminalWorkStatus()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => completed.Cancel(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Archive_RejectsNonTerminalWorkStatus()
    {
        var task = NewTask();
        var inProgress = task.Start(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => task.Archive(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => inProgress.Archive(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => NewTask().Rename(EditorId, "X", CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Archive_WorksForCompletedAndCancelled()
    {
        var archivedCompleted = NewTask()
            .Complete(CreatorId, CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2));
        var archivedCancelled = NewTask()
            .Cancel(CreatorId, CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2));

        Assert.Equal(EntityLifecycleState.Archived, archivedCompleted.Metadata.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, archivedCompleted.WorkStatus);
        Assert.Equal(CreatedAt.AddMinutes(2), archivedCompleted.Metadata.ArchivedAtUtc);
        Assert.Equal(EntityLifecycleState.Archived, archivedCancelled.Metadata.LifecycleState);
        Assert.Equal(TaskWorkStatus.Cancelled, archivedCancelled.WorkStatus);
        Assert.Equal(3, archivedCompleted.Metadata.Version);
    }

    [Fact]
    public void RestoreFromArchive_ReturnsToActiveKeepingWorkStatus()
    {
        var restored = NewTask()
            .Complete(CreatorId, CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2))
            .RestoreFromArchive(EditorId, CreatedAt.AddMinutes(3));

        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, restored.WorkStatus);
        Assert.Equal(CreatedAt.AddMinutes(1), restored.CompletedAtUtc);
        Assert.Equal(CreatorId, restored.CompletedBy);
        Assert.Null(restored.Metadata.ArchivedAtUtc);
    }

    [Fact]
    public void RestoreFromArchive_ThrowsUnlessArchived()
    {
        var task = NewTask();
        var cancelled = task.Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => task.RestoreFromArchive(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.RestoreFromArchive(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void MoveToTrash_RejectsNonTerminalWorkStatus()
    {
        Assert.Throws<InvalidOperationException>(() => NewTask().MoveToTrash(EditorId, CreatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            NewTask().Start(CreatorId, CreatedAt.AddMinutes(1)).MoveToTrash(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void TrashAndRestore_PreservesWorkStatusAndCompletionMetadata()
    {
        var trashed = NewTask()
            .Complete(CreatorId, CreatedAt.AddMinutes(1))
            .MoveToTrash(EditorId, CreatedAt.AddMinutes(2));
        var restored = trashed.RestoreFromTrash(EditorId, CreatedAt.AddMinutes(3));

        Assert.Equal(EntityLifecycleState.Trashed, trashed.Metadata.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, trashed.WorkStatus);
        Assert.Equal(CreatedAt.AddMinutes(2), trashed.Metadata.DeletedAtUtc);
        Assert.Equal(EditorId, trashed.Metadata.DeletedBy);
        Assert.Equal(EntityLifecycleState.Active, restored.Metadata.LifecycleState);
        Assert.Equal(TaskWorkStatus.Completed, restored.WorkStatus);
        Assert.Equal(CreatedAt.AddMinutes(1), restored.CompletedAtUtc);
        Assert.Null(restored.Metadata.DeletedAtUtc);
    }

    [Fact]
    public void RestoreFromTrash_ThrowsUnlessTrashed()
    {
        var task = NewTask();
        var completed = task.Complete(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => task.RestoreFromTrash(EditorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => completed.RestoreFromTrash(EditorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void ChangesAreForbiddenWhileArchivedOrTrashed()
    {
        var archived = NewTask()
            .Complete(CreatorId, CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2));
        var trashed = NewTask()
            .Cancel(CreatorId, CreatedAt.AddMinutes(1))
            .MoveToTrash(EditorId, CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() => archived.Rename(EditorId, "X", CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => archived.Complete(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => archived.Cancel(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => archived.Archive(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => archived.MoveToTrash(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => trashed.Rename(EditorId, "X", CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => trashed.Complete(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => trashed.Archive(EditorId, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => trashed.RestoreFromArchive(EditorId, CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void BackdatedTimestamp_IsRejectedByOperations()
    {
        var task = NewTask().Start(CreatorId, CreatedAt.AddMinutes(5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.Rename(EditorId, "Backdated", CreatedAt.AddMinutes(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.Complete(EditorId, CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void NonUtcTimestamp_IsRejectedByOperations()
    {
        var task = NewTask();
        var nonUtcAt = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.FromHours(5));

        Assert.Throws<ArgumentException>(() => task.Rename(EditorId, "X", nonUtcAt));
        Assert.Throws<ArgumentException>(() => task.Complete(EditorId, nonUtcAt));
    }

    [Fact]
    public void Version_IncrementsMonotonicallyAcrossFullLifecycleChain()
    {
        var task = NewTask()
            .Start(CreatorId, CreatedAt.AddMinutes(1))
            .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2))
            .Complete(EditorId, CreatedAt.AddMinutes(3))
            .Archive(EditorId, CreatedAt.AddMinutes(4))
            .RestoreFromArchive(EditorId, CreatedAt.AddMinutes(5))
            .MoveToTrash(EditorId, CreatedAt.AddMinutes(6))
            .RestoreFromTrash(EditorId, CreatedAt.AddMinutes(7));

        Assert.Equal(8, task.Metadata.Version);
        Assert.Equal(TaskWorkStatus.Completed, task.WorkStatus);
        Assert.Equal(EntityLifecycleState.Active, task.Metadata.LifecycleState);
    }

    [Fact]
    public void Create_SetsNormalPriorityAndEmptySchedule()
    {
        var task = NewTask();

        Assert.Equal(TaskPriority.Normal, task.Priority);
        Assert.NotNull(task.Schedule);
        Assert.Null(task.Schedule.StartsAtUtc);
        Assert.Null(task.Schedule.DeadlineUtc);
    }

    [Fact]
    public void ChangePriority_UpdatesPriorityAndIncrementsVersion()
    {
        var reprioritized = NewTask().ChangePriority(EditorId, TaskPriority.Critical, CreatedAt.AddMinutes(1));

        Assert.Equal(TaskPriority.Critical, reprioritized.Priority);
        Assert.Equal(2, reprioritized.Metadata.Version);
        Assert.Equal(EditorId, reprioritized.Metadata.UpdatedBy);
        Assert.Equal(CreatedAt.AddMinutes(1), reprioritized.Metadata.UpdatedAtUtc);
        Assert.Equal(TaskPriority.Normal, NewTask().Priority);
    }

    [Fact]
    public void ChangePriority_AllowsTransitionToAnyDefinedValue()
    {
        var low = NewTask().ChangePriority(EditorId, TaskPriority.Low, CreatedAt.AddMinutes(1));
        var high = NewTask().ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(1));

        Assert.Equal(TaskPriority.Low, low.Priority);
        Assert.Equal(TaskPriority.High, high.Priority);
    }

    [Fact]
    public void ChangePriority_RejectsUndefinedValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NewTask().ChangePriority(EditorId, (TaskPriority)99, CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NewTask().ChangePriority(EditorId, (TaskPriority)(-1), CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void ChangePriority_IsNoOpWhenPriorityIsUnchanged()
    {
        var task = NewTask();

        var result = task.ChangePriority(EditorId, TaskPriority.Normal, CreatedAt.AddMinutes(1));

        Assert.Same(task, result);
        Assert.Equal(1, result.Metadata.Version);
    }

    [Fact]
    public void ChangePriority_AllowedFromAnyNonTerminalWorkStatus()
    {
        foreach (var task in new[]
                 {
                     NewTask(),
                     NewTask().Start(CreatorId, CreatedAt.AddMinutes(1)),
                     NewTask().Start(CreatorId, CreatedAt.AddMinutes(1))
                         .SubmitForReview(CreatorId, CreatedAt.AddMinutes(2)),
                 })
        {
            var reprioritized = task.ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(3));

            Assert.Equal(TaskPriority.High, reprioritized.Priority);
            Assert.Equal(task.Metadata.Version + 1, reprioritized.Metadata.Version);
        }
    }

    [Fact]
    public void ChangePriority_RejectedForTerminalWorkStatus()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            completed.ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Reschedule_SetsPlanningWindowAndIncrementsVersion()
    {
        var startsAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var deadline = new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.Zero);

        var rescheduled = NewTask().Reschedule(EditorId, TaskSchedule.Create(startsAt, deadline), CreatedAt.AddMinutes(1));

        Assert.Equal(startsAt, rescheduled.Schedule.StartsAtUtc);
        Assert.Equal(deadline, rescheduled.Schedule.DeadlineUtc);
        Assert.Equal(2, rescheduled.Metadata.Version);
        Assert.Equal(EditorId, rescheduled.Metadata.UpdatedBy);
        Assert.Equal(CreatedAt.AddMinutes(1), rescheduled.Metadata.UpdatedAtUtc);
        Assert.Null(NewTask().Schedule.DeadlineUtc);
    }

    [Fact]
    public void Reschedule_IsNoOpWhenScheduleIsUnchanged()
    {
        var schedule = TaskSchedule.Create(
            new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.Zero));
        var task = NewTask().Reschedule(EditorId, schedule, CreatedAt.AddMinutes(1));

        var result = task.Reschedule(
            EditorId,
            TaskSchedule.Create(schedule.StartsAtUtc, schedule.DeadlineUtc),
            CreatedAt.AddMinutes(2));

        Assert.Same(task, result);
        Assert.Equal(2, result.Metadata.Version);
    }

    [Fact]
    public void Reschedule_RejectsNullSchedule()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewTask().Reschedule(EditorId, null!, CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Reschedule_RejectedForTerminalWorkStatus()
    {
        var completed = NewTask().Complete(CreatorId, CreatedAt.AddMinutes(1));
        var cancelled = NewTask().Cancel(CreatorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            completed.Reschedule(EditorId, DeadlineOnlySchedule(), CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() =>
            cancelled.Reschedule(EditorId, DeadlineOnlySchedule(), CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void PlanningChanges_ForbiddenWhileArchivedOrTrashed()
    {
        var archived = NewTask()
            .Complete(CreatorId, CreatedAt.AddMinutes(1))
            .Archive(EditorId, CreatedAt.AddMinutes(2));
        var trashed = NewTask()
            .Cancel(CreatorId, CreatedAt.AddMinutes(1))
            .MoveToTrash(EditorId, CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            archived.ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() =>
            archived.Reschedule(EditorId, DeadlineOnlySchedule(), CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() =>
            trashed.ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() =>
            trashed.Reschedule(EditorId, DeadlineOnlySchedule(), CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void PlanningChanges_RejectBackdatedAndNonUtcTimestamps()
    {
        var task = NewTask().ChangePriority(EditorId, TaskPriority.High, CreatedAt.AddMinutes(5));
        var nonUtcAt = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.FromHours(5));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.ChangePriority(EditorId, TaskPriority.Critical, CreatedAt.AddMinutes(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            task.Reschedule(EditorId, DeadlineOnlySchedule(), CreatedAt.AddMinutes(3)));
        Assert.Throws<ArgumentException>(() =>
            NewTask().ChangePriority(EditorId, TaskPriority.High, nonUtcAt));
        Assert.Throws<ArgumentException>(() =>
            NewTask().Reschedule(EditorId, DeadlineOnlySchedule(), nonUtcAt));
    }

    [Fact]
    public void IsOverdue_TrueWhenDeadlinePassedAndNonTerminal()
    {
        var overdue = NewTask().Reschedule(
            EditorId,
            TaskSchedule.Create(null, CreatedAt.AddMinutes(30)),
            CreatedAt.AddMinutes(1));

        Assert.True(overdue.IsOverdue(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void IsOverdue_FalseWithoutDeadline()
    {
        Assert.False(NewTask().IsOverdue(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void IsOverdue_FalseWhileDeadlineIsInTheFuture()
    {
        var scheduled = NewTask().Reschedule(
            EditorId,
            TaskSchedule.Create(null, CreatedAt.AddHours(3)),
            CreatedAt.AddMinutes(1));

        Assert.False(scheduled.IsOverdue(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void IsOverdue_FalseForTerminalWorkStatus()
    {
        var completed = NewTask()
            .Reschedule(
                EditorId,
                TaskSchedule.Create(null, CreatedAt.AddMinutes(30)),
                CreatedAt.AddMinutes(1))
            .Complete(CreatorId, CreatedAt.AddMinutes(2));
        var cancelled = NewTask()
            .Reschedule(
                EditorId,
                TaskSchedule.Create(null, CreatedAt.AddMinutes(30)),
                CreatedAt.AddMinutes(1))
            .Cancel(CreatorId, CreatedAt.AddMinutes(2));

        Assert.False(completed.IsOverdue(CreatedAt.AddHours(1)));
        Assert.False(cancelled.IsOverdue(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void IsOverdue_FalseWhenDeadlineEqualsNow()
    {
        var deadline = CreatedAt.AddHours(1);
        var scheduled = NewTask().Reschedule(
            EditorId,
            TaskSchedule.Create(null, deadline),
            CreatedAt.AddMinutes(1));

        Assert.False(scheduled.IsOverdue(deadline));
    }

    [Fact]
    public void IsOverdue_DoesNotChangeVersion()
    {
        var scheduled = NewTask().Reschedule(
            EditorId,
            TaskSchedule.Create(null, CreatedAt.AddMinutes(30)),
            CreatedAt.AddMinutes(1));

        _ = scheduled.IsOverdue(CreatedAt.AddHours(1));

        Assert.Equal(2, scheduled.Metadata.Version);
    }

    [Fact]
    public void PlanningFields_SurviveFullLifecycleChainIncludingArchiveAndTrash()
    {
        var schedule = TaskSchedule.Create(
            new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.Zero));

        var task = NewTask()
            .ChangePriority(EditorId, TaskPriority.Critical, CreatedAt.AddMinutes(1))
            .Reschedule(EditorId, schedule, CreatedAt.AddMinutes(2))
            .Start(CreatorId, CreatedAt.AddMinutes(3))
            .SubmitForReview(CreatorId, CreatedAt.AddMinutes(4))
            .Complete(EditorId, CreatedAt.AddMinutes(5))
            .Archive(EditorId, CreatedAt.AddMinutes(6))
            .RestoreFromArchive(EditorId, CreatedAt.AddMinutes(7))
            .MoveToTrash(EditorId, CreatedAt.AddMinutes(8))
            .RestoreFromTrash(EditorId, CreatedAt.AddMinutes(9));

        Assert.Equal(TaskPriority.Critical, task.Priority);
        Assert.Equal(schedule, task.Schedule);
    }

    private static TaskSchedule DeadlineOnlySchedule() =>
        TaskSchedule.Create(null, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));

    private static TaskAggregate NewTask() =>
        TaskAggregate.Create(TaskId, OrganizationId, CreatorId, "Implement login screen", CreatedAt);
}