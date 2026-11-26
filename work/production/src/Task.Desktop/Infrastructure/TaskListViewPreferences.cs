using System.Text.Json;

namespace Task.Desktop.Infrastructure;

internal sealed record TaskListViewPreference(
    int? Status,
    Guid? ProjectId,
    int? Priority,
    string Deadline,
    string Sort,
    string? Search = null,
    bool HideCompleted = false,
    bool WithoutProject = false);

public sealed partial class ViewStateStore
{
    private readonly Dictionary<string, TaskListViewPreference> _taskListViews = [];

    internal TaskListViewPreference? TaskListView(string mode)
    {
        lock (_sync) return _taskListViews.GetValueOrDefault(mode);
    }

    internal void SetTaskListView(string mode, TaskListViewPreference? view)
    {
        if (mode is not ("personal" or "corporate")) return;
        Change(() =>
        {
            if (view is null) _taskListViews.Remove(mode);
            else _taskListViews[mode] = view;
        });
    }

    private void ReadTaskListViews(JsonElement root)
    {
        if (!root.TryGetProperty("taskListViews", out var values) || values.ValueKind != JsonValueKind.Object) return;
        foreach (var value in values.EnumerateObject())
        {
            if (value.Name is not ("personal" or "corporate")) continue;
            try
            {
                var view = value.Value.Deserialize<TaskListViewPreference>();
                if (view is not null) _taskListViews[value.Name] = view;
            }
            catch (JsonException) { Report("invalid task list view discarded"); }
        }
    }
}
