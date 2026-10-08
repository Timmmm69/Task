using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

/// <summary>A single acknowledged transition, never a status-observation trigger.</summary>
public sealed class TaskCompletionFeedback(Guid taskId, long version, bool motionEnabled)
{
    public static TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(200);
    public Guid TaskId { get; } = taskId;
    public long Version { get; } = version;
    public bool MotionEnabled { get; } = motionEnabled;
    public bool IsActive { get; private set; } = true;
    private bool _played;

    public bool TryPlay(Guid taskId, long version)
    {
        if (!IsActive || !MotionEnabled || _played || TaskId != taskId || Version != version) return false;
        _played = true;
        return true;
    }

    public void Reset() => IsActive = false;
}

public sealed partial class TasksViewModel
{
    private readonly Dictionary<Guid, TaskCompletionFeedback> _completionFeedback = [];
    private readonly Dictionary<Guid, long> _acknowledgedCompletions = [];
    public event Action<Guid>? CompletionPresented;
    public event Action<Guid>? CompletionPresentationEnded;

    private TaskCompletionFeedback? GetCompletionFeedback(DesktopTaskDto task) =>
        _completionFeedback.TryGetValue(task.Id, out var feedback)
        && feedback.IsActive && task.Version == feedback.Version && task.Status == DesktopTaskStatus.Completed
            ? feedback : null;

    private TaskCompletionFeedback? AcknowledgeCompletion(DesktopTaskDto before, DesktopTaskDto after)
    {
        if (before.Id != after.Id || before.Status is DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled
            || after.Status != DesktopTaskStatus.Completed || after.Version <= before.Version
            || (_acknowledgedCompletions.TryGetValue(after.Id, out var version) && version >= after.Version)) return null;
        _acknowledgedCompletions[after.Id] = after.Version;
        var feedback = new TaskCompletionFeedback(after.Id, after.Version, _completionMotionEnabled());
        _completionFeedback[after.Id] = feedback;
        CompletionPresented?.Invoke(after.Id);
        return feedback;
    }

    private async global::System.Threading.Tasks.Task FinishCompletionAsync(TaskCompletionFeedback feedback)
    {
        if (feedback.MotionEnabled) await _completionDelay(TaskCompletionFeedback.Duration).ConfigureAwait(true);
        feedback.Reset();
        if (!_completionFeedback.TryGetValue(feedback.TaskId, out var current) || !ReferenceEquals(current, feedback)) return;
        _completionFeedback.Remove(feedback.TaskId);
        if (_disposed || !IsActive) return;
        ApplyFilters(SelectedItem?.Id);
        CompletionPresentationEnded?.Invoke(feedback.TaskId);
    }

    private void ResetCompletionFeedback()
    {
        foreach (var feedback in _completionFeedback.Values) feedback.Reset();
        _completionFeedback.Clear();
    }
}
