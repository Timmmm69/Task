using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application.Calendar;
using Task.Desktop.ViewModels;
using Task.Domain.Recurrence;

namespace Task.Desktop.Calendar;

/// <summary>Compatibility adapter retains all existing Corporate endpoints and payloads.</summary>
public sealed class DesktopRecurrenceHttpAdapter(IDesktopRecurrenceApiClient transport) : IDesktopRecurrenceClient
{
    public async System.Threading.Tasks.Task<DesktopCalendarResult<DesktopRecurrenceReply>> ExecuteAsync(DesktopRecurrenceCommand command, CancellationToken cancellationToken)
    {
        var method = HttpMethod.Get; var path = ""; object? body = null; long? version = null; string? key = null;
        switch (command)
        {
            case DesktopRecurrenceCommand.List: break;
            case DesktopRecurrenceCommand.Occurrences c: path = $"/{c.SeriesId}/occurrences"; break;
            case DesktopRecurrenceCommand.Preview c: method = HttpMethod.Post; path = "/preview"; body = new { rule = c.Definition, fromDate = c.From, limit = c.Limit }; break;
            case DesktopRecurrenceCommand.Save c:
                method = c.SeriesId is null ? HttpMethod.Post : HttpMethod.Patch; path = c.SeriesId is null ? "" : $"/{c.SeriesId}";
                var definition = JsonSerializer.SerializeToNode(c.Definition, RecurrenceService.JsonOptions)!.AsObject();
                definition.Remove("nextGenerationDate"); body = definition; version = c.Version; key = c.Key; break;
            case DesktopRecurrenceCommand.Generate c:
                method = HttpMethod.Post; path = $"/{c.SeriesId}/generate"; body = new { throughDate = c.Through, expectedSeriesVersion = c.Version }; key = c.Key; break;
            case DesktopRecurrenceCommand.SetStatus c:
                method = c.Status == "cancelled" ? HttpMethod.Delete : c.Status == "paused" ? HttpMethod.Patch : HttpMethod.Post;
                path = $"/{c.SeriesId}" + (c.Status == "active" ? "/resume" : ""); version = c.Version; key = c.Key;
                body = c.Status == "paused" ? new { status = c.Status } : new { expectedVersion = c.Version }; break;
            case DesktopRecurrenceCommand.Apply c:
                method = HttpMethod.Post; path = $"/{c.SeriesId}/apply-change?occurrenceKey={c.Date:yyyy-MM-dd}"; version = c.Version; key = c.Key;
                var scope = c.Scope switch { RecurrenceChangeScope.ThisOccurrence => "this_occurrence", RecurrenceChangeScope.ThisAndFuture => "this_and_future", _ => "entire_series" };
                body = new { scope, expectedTaskVersion = c.TaskVersion, patch = new { c.Title, c.Priority, plannedDurationMinutes = c.Duration } }; break;
            default: throw new ArgumentException("Unknown recurrence operation.");
        }
        var result = await transport.SendAsync(method, path, body is null ? null : JsonSerializer.Serialize(body, RecurrenceService.JsonOptions), version, key, cancellationToken);
        if (result is DesktopCalendarResult<JsonElement>.Succeeded ok)
        {
            try
            {
                DesktopRecurrenceSeries Parse(JsonElement j) { var item = RecurrencePaneViewModel.ParseSeries(j); return new(item.Id, item.Version, item.Definition); }
                var value = command switch
                {
                    DesktopRecurrenceCommand.List => new DesktopRecurrenceReply(Items: ok.Value.GetProperty("items").EnumerateArray().Select(Parse).ToArray()),
                    DesktopRecurrenceCommand.Occurrences => new(Occurrences: ok.Value.Deserialize<RecurrenceOccurrenceDetails[]>(RecurrenceService.JsonOptions)),
                    DesktopRecurrenceCommand.Preview => new(Preview: ok.Value.Deserialize<RecurrencePreviewItem[]>(RecurrenceService.JsonOptions)),
                    DesktopRecurrenceCommand.Generate => new(GeneratedCount: ok.Value.GetProperty("generatedCount").GetInt32(), SeriesVersion: ok.Value.GetProperty("seriesVersion").GetInt64()),
                    DesktopRecurrenceCommand.Apply => new(Series: Parse(ok.Value.GetProperty("series"))),
                    _ => new(Series: Parse(ok.Value)),
                };
                return new DesktopCalendarResult<DesktopRecurrenceReply>.Succeeded(value);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { return new DesktopCalendarResult<DesktopRecurrenceReply>.MalformedResponse(); }
        }
        return result switch
        {
            DesktopCalendarResult<JsonElement>.Forbidden => new DesktopCalendarResult<DesktopRecurrenceReply>.Forbidden(),
            DesktopCalendarResult<JsonElement>.AuthenticationFailure => new DesktopCalendarResult<DesktopRecurrenceReply>.AuthenticationFailure(),
            DesktopCalendarResult<JsonElement>.NotFound => new DesktopCalendarResult<DesktopRecurrenceReply>.NotFound(),
            DesktopCalendarResult<JsonElement>.VersionConflict => new DesktopCalendarResult<DesktopRecurrenceReply>.VersionConflict(),
            DesktopCalendarResult<JsonElement>.ValidationFailure v => new DesktopCalendarResult<DesktopRecurrenceReply>.ValidationFailure(v.Message),
            _ => new DesktopCalendarResult<DesktopRecurrenceReply>.ServerUnavailable(),
        };
    }
}
