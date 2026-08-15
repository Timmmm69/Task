namespace Task.Domain;

/// <summary>
/// Computes the derived overdue state: a task is overdue only when its deadline
/// is strictly before now and its work status is not terminal.
/// </summary>
public static class TaskOverduePolicy
{
    public static bool IsOverdue(
        TaskWorkStatus workStatus,
        DateTimeOffset? deadlineUtc,
        DateTimeOffset nowUtc)
    {
        EnsureKnownStatus(workStatus);
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(deadlineUtc, nameof(deadlineUtc));

        if (IsTerminal(workStatus) || deadlineUtc is null)
        {
            return false;
        }

        return deadlineUtc.Value < nowUtc;
    }

    private static bool IsTerminal(TaskWorkStatus workStatus) =>
        workStatus == TaskWorkStatus.Completed || workStatus == TaskWorkStatus.Cancelled;

    private static void EnsureKnownStatus(TaskWorkStatus workStatus)
    {
        if (!Enum.IsDefined(workStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(workStatus),
                "Unknown task work status.");
        }
    }

    private static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", parameterName);
        }
    }
}