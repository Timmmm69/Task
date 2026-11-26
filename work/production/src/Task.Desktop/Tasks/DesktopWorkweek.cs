using System.Net;
using Task.Desktop.Security;
using System.Text.Json;
using Task.Application;

namespace Task.Desktop.TaskApi;

public sealed partial class DesktopTasksApiClient
{
    internal async System.Threading.Tasks.Task<WorkdayResult?> ReadDayAsync(TaskTodayWindow day,
        DateTimeOffset cutoff, CancellationToken token)
    {
        static string Stamp(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("O"));
        var uri = new Uri($"{_tasksUri}/day-result?date={day.Date:yyyy-MM-dd}&from={Stamp(day.FromUtc)}&to={Stamp(day.ToUtc)}&through={Stamp(cutoff)}");
        var result = await _executor.GetAsync(uri, Guid.NewGuid().ToString("D"), token);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return null;
        try
        {
            var dayResult = JsonSerializer.Deserialize<WorkdayResult>(response.Body, JsonOptions);
            return dayResult is { Completed: >= 0 } && dayResult.Planned >= dayResult.Completed ? dayResult : null;
        }
        catch (JsonException) { return null; }
    }

    internal async System.Threading.Tasks.Task<WorkweekResults?> ReadWeekAsync(DateTimeOffset from,
        DateTimeOffset through, CancellationToken token)
    {
        var uri = new Uri($"{_tasksUri}/week?from={Uri.EscapeDataString(from.ToUniversalTime().ToString("O"))}&through={Uri.EscapeDataString(through.ToUniversalTime().ToString("O"))}");
        var result = await _executor.GetAsync(uri, Guid.NewGuid().ToString("D"), token);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response) return null;
        try
        {
            var week = JsonSerializer.Deserialize<WorkweekResults>(response.Body, JsonOptions);
            return week is { Total: >= 0, Items: { Count: <= 2 } } && week.Items.Count == Math.Min(2, week.Total)
                && week.Items.All(i => !string.IsNullOrWhiteSpace(i.Title) && i.CompletedAt >= from && i.CompletedAt <= through) ? week : null;
        }
        catch (JsonException) { return null; }
    }
}
