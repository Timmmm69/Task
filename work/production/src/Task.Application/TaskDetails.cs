using Task.Domain;

namespace Task.Application;

/// <summary>
/// Read-only projection of a single task for query use cases.
/// </summary>
public sealed record TaskDetails(
    Guid Id,
    Guid OrganizationId,
    string Title,
    TaskWorkStatus WorkStatus,
    EntityLifecycleState LifecycleState,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? CompletedBy);