namespace Task.Domain;

/// <summary>Task rules independent of corporate identity and persistence.</summary>
public static class TaskRules
{
    public static string NormalizeTitle(string title)
    {
        var normalized = title?.Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Task title must not be empty.", nameof(title));
        if (normalized.Length > 500)
            throw new ArgumentException("Task title must not exceed 500 characters.", nameof(title));
        return normalized;
    }

    public static bool IsTerminal(TaskWorkStatus status) =>
        status is TaskWorkStatus.Completed or TaskWorkStatus.Cancelled;

    public static bool CanTransition(TaskWorkStatus current, TaskWorkStatus target) => (current, target) switch
    {
        (TaskWorkStatus.New, TaskWorkStatus.InProgress) => true,
        (TaskWorkStatus.InProgress, TaskWorkStatus.Review) => true,
        (TaskWorkStatus.New or TaskWorkStatus.InProgress or TaskWorkStatus.Review,
            TaskWorkStatus.Completed or TaskWorkStatus.Cancelled) => true,
        _ => false,
    };
}
