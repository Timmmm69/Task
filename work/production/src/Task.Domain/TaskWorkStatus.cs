namespace Task.Domain;

/// <summary>
/// MVP workflow status of a task.
/// Overdue is not stored as a TaskWorkStatus: it is computed later from the
/// task deadline and its terminal state.
/// </summary>
public enum TaskWorkStatus
{
    New = 0,
    InProgress = 1,
    Review = 2,
    Completed = 3,
    Cancelled = 4,
}