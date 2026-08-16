using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceOccurrenceTests
{
    private static readonly Guid OccurrenceId = Guid.Parse("5a3c81d7-2e4b-4f6a-9a2d-1c4e6f8a0b2d");
    private static readonly Guid SeriesId = Guid.Parse("b64fbeec-f0f4-4f5f-9967-ea2ce57be461");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");
    private static readonly Guid ActorId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid TaskId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly OccurrenceKey Key = OccurrenceKey.FromLocalDate(new DateOnly(2026, 8, 3));

    [Fact]
    public void Create_OpensAsPlannedWithDeterministicKey()
    {
        var occurrence = Occurrence();

        Assert.Equal(RecurrenceOccurrenceStatus.Planned, occurrence.Status);
        Assert.Equal(Key, occurrence.OccurrenceKey);
        Assert.Equal(Key.LocalDate, occurrence.LocalDate);
        Assert.Null(occurrence.TaskId);
        Assert.Equal(SeriesId, occurrence.SeriesId);
        Assert.Equal(1, occurrence.Metadata.Version);
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiersAndKeys()
    {
        Assert.Throws<ArgumentException>(() => Occurrence(seriesId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Occurrence(occurrenceId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Occurrence(createdBy: Guid.Empty));
        Assert.Throws<ArgumentNullException>(() =>
            RecurrenceOccurrence.Create(OccurrenceId, OrganizationId, SeriesId, ActorId, null!, CreatedAt));
    }

    [Fact]
    public void Reconstitute_EnforcesKeyDateAndGeneratedTaskInvariants()
    {
        var metadata = SyncableEntityMetadata.Create(OccurrenceId, OrganizationId, ActorId, CreatedAt);

        var generated = RecurrenceOccurrence.Reconstitute(
            metadata,
            SeriesId,
            Key,
            Key.LocalDate,
            RecurrenceOccurrenceStatus.Generated,
            TaskId);
        Assert.Equal(RecurrenceOccurrenceStatus.Generated, generated.Status);
        Assert.Equal(TaskId, generated.TaskId);

        Assert.Throws<ArgumentException>(() =>
            RecurrenceOccurrence.Reconstitute(metadata, SeriesId, Key, Key.LocalDate.AddDays(1), RecurrenceOccurrenceStatus.Planned, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceOccurrence.Reconstitute(metadata, SeriesId, Key, Key.LocalDate, RecurrenceOccurrenceStatus.Generated, null));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceOccurrence.Reconstitute(metadata, SeriesId, Key, Key.LocalDate, RecurrenceOccurrenceStatus.Planned, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceOccurrence.Reconstitute(metadata, SeriesId, Key, Key.LocalDate, (RecurrenceOccurrenceStatus)99, null));
    }

    [Fact]
    public void MarkGenerated_TurnsPlannedIntoGeneratedWithTask()
    {
        var generated = Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(1), TaskId);

        Assert.Equal(RecurrenceOccurrenceStatus.Generated, generated.Status);
        Assert.Equal(TaskId, generated.TaskId);
        Assert.Equal(2, generated.Metadata.Version);
        Assert.Equal(ActorId, generated.Metadata.UpdatedBy);
    }

    [Fact]
    public void MarkGenerated_RejectsNonPlannedOrEmptyTask()
    {
        var generated = Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(1), TaskId);

        Assert.Throws<InvalidOperationException>(() => generated.MarkGenerated(ActorId, CreatedAt.AddMinutes(2), TaskId));
        Assert.Throws<InvalidOperationException>(() => Occurrence().Skip(ActorId, CreatedAt.AddMinutes(1))
            .MarkGenerated(ActorId, CreatedAt.AddMinutes(2), TaskId));
        Assert.Throws<ArgumentException>(() => Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(1), Guid.Empty));
    }

    [Fact]
    public void Skip_AcceptsPlannedAndGeneratedOccurrences()
    {
        var plannedSkipped = Occurrence().Skip(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(RecurrenceOccurrenceStatus.Skipped, plannedSkipped.Status);
        Assert.Null(plannedSkipped.TaskId);

        var generated = Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(1), TaskId);
        var generatedSkipped = generated.Skip(ActorId, CreatedAt.AddMinutes(2));
        Assert.Equal(RecurrenceOccurrenceStatus.Skipped, generatedSkipped.Status);
        Assert.Equal(TaskId, generatedSkipped.TaskId);
    }

    [Fact]
    public void Skip_RejectsAlreadySkippedOrCancelled()
    {
        var skipped = Occurrence().Skip(ActorId, CreatedAt.AddMinutes(1));
        var cancelled = Occurrence().Cancel(ActorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => skipped.Skip(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Skip(ActorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Cancel_AcceptsPlannedAndGeneratedOccurrences()
    {
        var plannedCancelled = Occurrence().Cancel(ActorId, CreatedAt.AddMinutes(1));
        Assert.Equal(RecurrenceOccurrenceStatus.Cancelled, plannedCancelled.Status);

        var generated = Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(1), TaskId);
        var generatedCancelled = generated.Cancel(ActorId, CreatedAt.AddMinutes(2));
        Assert.Equal(RecurrenceOccurrenceStatus.Cancelled, generatedCancelled.Status);
        Assert.Equal(TaskId, generatedCancelled.TaskId);
        Assert.Equal(EntityLifecycleState.Active, generatedCancelled.Metadata.LifecycleState);
    }

    [Fact]
    public void Cancel_RejectsSkippedOrCancelledOccurrences()
    {
        var skipped = Occurrence().Skip(ActorId, CreatedAt.AddMinutes(1));
        var cancelled = Occurrence().Cancel(ActorId, CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => skipped.Cancel(ActorId, CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(ActorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Operations_RejectBackdatedAndNonUtcTimestamps()
    {
        var occurrence = Occurrence().MarkGenerated(ActorId, CreatedAt.AddMinutes(2), TaskId);
        var nonUtcAt = new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.FromHours(5));

        Assert.Throws<ArgumentOutOfRangeException>(() => occurrence.Skip(ActorId, CreatedAt.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => Occurrence().Skip(ActorId, nonUtcAt));
    }

    private static RecurrenceOccurrence Occurrence(
        Guid? occurrenceId = null,
        Guid? seriesId = null,
        Guid? createdBy = null,
        OccurrenceKey? occurrenceKey = null) =>
        RecurrenceOccurrence.Create(
            occurrenceId ?? OccurrenceId,
            OrganizationId,
            seriesId ?? SeriesId,
            createdBy ?? ActorId,
            occurrenceKey ?? Key,
            CreatedAt);
}