namespace Task.Domain;

/// <summary>
/// User-facing priority of a task. Normal is the default; higher values are more urgent.
/// </summary>
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}