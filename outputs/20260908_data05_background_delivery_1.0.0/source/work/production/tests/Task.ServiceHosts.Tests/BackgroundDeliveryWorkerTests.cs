using Task.Application.Background;
using Task.Worker;

namespace Task.ServiceHosts.Tests;

public sealed class BackgroundDeliveryWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void WorkerCadenceLeaseAndBatch_AreContractValues()
    {
        Assert.Equal(1, OutboxPublisherWorker.PeriodSeconds);
        Assert.Equal(5, ReminderDeliveryWorker.PeriodSeconds);
        Assert.Equal(5, ReminderDeliveryWorker.MaterializationPeriodMinutes);
        Assert.Equal(30, OutboxPublisherWorker.LeaseSeconds);
        Assert.Equal(30, ReminderDeliveryWorker.LeaseSeconds);
        Assert.Equal(100, OutboxPublisherWorker.BatchSize);
        Assert.Equal(100, ReminderDeliveryWorker.BatchSize);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OutboxWorker_DeliversBatch_AndPersistsFailureForRetry()
    {
        var logger = new RecordingLogger<OutboxPublisherWorker>();
        var first = Outbox(Guid.NewGuid());
        var second = Outbox(Guid.NewGuid());
        var store = new FakeDeliveryStore(outbox: [first, second]) { FailingOutboxId = first.Id };
        using var worker = new OutboxPublisherWorker(logger, store, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);
        await logger.WaitUntilAsync(
            messages => messages.Any(message => message.Contains("processed 2 messages")), Timeout);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Contains(second.Id, store.DeliveredOutbox);
        Assert.Contains(first.Id, store.FailedOutbox);
        Assert.DoesNotContain(first.Id, store.DeliveredOutbox);
        Assert.Equal([first.Id, second.Id], store.RenewedOutbox);
        Assert.All(store.OutboxClaims, claim =>
        {
            Assert.Equal(OutboxPublisherWorker.WorkerName, claim.Worker);
            Assert.NotEqual(Guid.Empty, claim.Token);
            Assert.Equal(TimeSpan.FromSeconds(30), claim.Lease);
            Assert.Equal(100, claim.Batch);
        });
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ReminderWorker_MaterializesAndDelivers_WhileIsolatingFailure()
    {
        var logger = new RecordingLogger<ReminderDeliveryWorker>();
        var first = Reminder(Guid.NewGuid());
        var second = Reminder(Guid.NewGuid());
        var store = new FakeDeliveryStore(reminders: [first, second])
        {
            MaterializedCount = 2,
            FailingReminderId = first.OccurrenceId,
        };
        using var worker = new ReminderDeliveryWorker(logger, store, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);
        await logger.WaitUntilAsync(
            messages => messages.Any(message => message.Contains("processed 2 occurrences")), Timeout);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.True(store.MaterializeCalls >= 1);
        Assert.Contains(second.OccurrenceId, store.DeliveredReminders);
        Assert.Contains(first.OccurrenceId, store.FailedReminders);
        Assert.DoesNotContain(first.OccurrenceId, store.DeliveredReminders);
        Assert.Equal([first.OccurrenceId, second.OccurrenceId], store.RenewedReminders);
        Assert.All(store.ReminderClaims, claim =>
        {
            Assert.Equal(ReminderDeliveryWorker.WorkerName, claim.Worker);
            Assert.NotEqual(Guid.Empty, claim.Token);
            Assert.Equal(TimeSpan.FromSeconds(30), claim.Lease);
            Assert.Equal(100, claim.Batch);
        });
    }

    [Fact]
    public async global::System.Threading.Tasks.Task UnconfiguredWorkers_LogWarning_AndStopCleanly()
    {
        var outboxLogger = new RecordingLogger<OutboxPublisherWorker>();
        var reminderLogger = new RecordingLogger<ReminderDeliveryWorker>();
        using var outbox = new OutboxPublisherWorker(outboxLogger, runPeriod: ShortPeriod);
        using var reminders = new ReminderDeliveryWorker(reminderLogger, runPeriod: ShortPeriod);

        await outbox.StartAsync(CancellationToken.None);
        await reminders.StartAsync(CancellationToken.None);
        await outboxLogger.WaitUntilAsync(
            messages => messages.Any(message => message.Contains("not registered")), Timeout);
        await reminderLogger.WaitUntilAsync(
            messages => messages.Any(message => message.Contains("not registered")), Timeout);
        await outbox.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        await reminders.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Contains(outboxLogger.Messages, message => message.Contains("hosting loop stopped"));
        Assert.Contains(reminderLogger.Messages, message => message.Contains("hosting loop stopped"));
    }

    private static OutboxDelivery Outbox(Guid id) =>
        new(id, Guid.NewGuid(), "sync", "TaskChanged", "{}", 1, Guid.NewGuid());

    private static ReminderDelivery Reminder(Guid id) =>
        new(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, $"reminder-{id:N}", 1, Guid.NewGuid());

    private sealed class FakeDeliveryStore(
        IReadOnlyList<OutboxDelivery>? outbox = null,
        IReadOnlyList<ReminderDelivery>? reminders = null) : IBackgroundDeliveryStore
    {
        private int _outboxClaimed;
        private int _remindersClaimed;

        public Guid? FailingOutboxId { get; init; }
        public Guid? FailingReminderId { get; init; }
        public int MaterializedCount { get; init; }
        public int MaterializeCalls { get; private set; }
        public List<Guid> DeliveredOutbox { get; } = [];
        public List<Guid> RenewedOutbox { get; } = [];
        public List<Guid> FailedOutbox { get; } = [];
        public List<Guid> DeliveredReminders { get; } = [];
        public List<Guid> RenewedReminders { get; } = [];
        public List<Guid> FailedReminders { get; } = [];
        public List<(string Worker, Guid Token, TimeSpan Lease, int Batch)> OutboxClaims { get; } = [];
        public List<(string Worker, Guid Token, TimeSpan Lease, int Batch)> ReminderClaims { get; } = [];

        public global::System.Threading.Tasks.Task<IReadOnlyList<OutboxDelivery>> ClaimOutboxAsync(
            string workerName, Guid leaseToken, TimeSpan leaseDuration, int batchSize,
            CancellationToken cancellationToken = default)
        {
            OutboxClaims.Add((workerName, leaseToken, leaseDuration, batchSize));
            IReadOnlyList<OutboxDelivery> result = Interlocked.Exchange(ref _outboxClaimed, 1) == 0
                ? (outbox ?? []).Select(item => item with { LeaseToken = leaseToken }).ToArray()
                : [];
            return global::System.Threading.Tasks.Task.FromResult(result);
        }

        public global::System.Threading.Tasks.Task DeliverOutboxAsync(
            OutboxDelivery delivery, CancellationToken cancellationToken = default)
        {
            if (delivery.Id == FailingOutboxId)
                throw new InvalidOperationException("simulated failure");
            DeliveredOutbox.Add(delivery.Id);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task RenewOutboxLeaseAsync(
            OutboxDelivery delivery, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TimeSpan.FromSeconds(OutboxPublisherWorker.LeaseSeconds), leaseDuration);
            RenewedOutbox.Add(delivery.Id);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task FailOutboxAsync(
            OutboxDelivery delivery, string errorCode, string errorDetail,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("OUTBOX_DELIVERY_FAILED", errorCode);
            FailedOutbox.Add(delivery.Id);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task<int> MaterializeDueRemindersAsync(
            int batchSize, CancellationToken cancellationToken = default)
        {
            MaterializeCalls++;
            return global::System.Threading.Tasks.Task.FromResult(MaterializedCount);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<ReminderDelivery>> ClaimReminderDeliveriesAsync(
            string workerName, Guid leaseToken, TimeSpan leaseDuration, int batchSize,
            CancellationToken cancellationToken = default)
        {
            ReminderClaims.Add((workerName, leaseToken, leaseDuration, batchSize));
            IReadOnlyList<ReminderDelivery> result = Interlocked.Exchange(ref _remindersClaimed, 1) == 0
                ? (reminders ?? []).Select(item => item with { LeaseToken = leaseToken }).ToArray()
                : [];
            return global::System.Threading.Tasks.Task.FromResult(result);
        }

        public global::System.Threading.Tasks.Task DeliverReminderAsync(
            ReminderDelivery delivery, CancellationToken cancellationToken = default)
        {
            if (delivery.OccurrenceId == FailingReminderId)
                throw new InvalidOperationException("simulated failure");
            DeliveredReminders.Add(delivery.OccurrenceId);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task RenewReminderLeaseAsync(
            ReminderDelivery delivery, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TimeSpan.FromSeconds(ReminderDeliveryWorker.LeaseSeconds), leaseDuration);
            RenewedReminders.Add(delivery.OccurrenceId);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        public global::System.Threading.Tasks.Task FailReminderAsync(
            ReminderDelivery delivery, string errorCode, CancellationToken cancellationToken = default)
        {
            Assert.Equal("REMINDER_DELIVERY_FAILED", errorCode);
            FailedReminders.Add(delivery.OccurrenceId);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
