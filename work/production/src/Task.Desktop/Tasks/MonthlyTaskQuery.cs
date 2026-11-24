using Task.Domain;

namespace Task.Desktop.TaskApi;

public static class MonthlyTaskQuery
{
    /// <summary>Reads the complete visible plan, including terminal tasks. A null month selects undated work.</summary>
    public static async System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetMonthlyTasksAsync(
        this IDesktopTasksApiClient client, string? month, CancellationToken cancellationToken = default)
    {
        if (!TaskCardContent.IsValidPlanningMonth(month))
            throw new ArgumentException("Planning month must be yyyy-MM.", nameof(month));

        var matches = new List<DesktopTaskDto>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await client.GetTasksAsync(cursor, cancellationToken).ConfigureAwait(false);
            if (result is not DesktopTasksApiResult<DesktopTaskPage>.Succeeded page) return result;
            matches.AddRange(page.Value.Items.Where(task => task.Card?.EffectivePlanningMonth == month));
            cursor = page.Value.NextCursor;
            if (cursor is not null && !cursors.Add(cursor))
                return new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor();
        } while (cursor is not null);

        return new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(matches, null, matches.Count));
    }
}
