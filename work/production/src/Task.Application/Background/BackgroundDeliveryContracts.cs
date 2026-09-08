namespace Task.Application.Background;

public sealed record OutboxDelivery(
    Guid Id,
    Guid OrganizationId,
    string Destination,
    string MessageType,
    string PayloadJson,
    int AttemptCount,
    Guid LeaseToken);

public sealed record ReminderDelivery(
    Guid OccurrenceId,
    Guid OrganizationId,
    Guid ReminderId,
    Guid TargetObjectId,
    Guid RecipientUserId,
    DateTimeOffset DueAtUtc,
    string IdempotencyKey,
    int AttemptCount,
    Guid LeaseToken);

/// <summary>
/// Durable worker port. Implementations must claim with a lease and make each
/// delivery idempotent so an expired lease can safely be recovered.
/// </summary>
public interface IBackgroundDeliveryStore
{
    global::System.Threading.Tasks.Task<IReadOnlyList<OutboxDelivery>> ClaimOutboxAsync(
        string workerName,
        Guid leaseToken,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task DeliverOutboxAsync(
        OutboxDelivery delivery,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RenewOutboxLeaseAsync(
        OutboxDelivery delivery,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task FailOutboxAsync(
        OutboxDelivery delivery,
        string errorCode,
        string errorDetail,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<int> MaterializeDueRemindersAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ReminderDelivery>> ClaimReminderDeliveriesAsync(
        string workerName,
        Guid leaseToken,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task DeliverReminderAsync(
        ReminderDelivery delivery,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RenewReminderLeaseAsync(
        ReminderDelivery delivery,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task FailReminderAsync(
        ReminderDelivery delivery,
        string errorCode,
        CancellationToken cancellationToken = default);
}
