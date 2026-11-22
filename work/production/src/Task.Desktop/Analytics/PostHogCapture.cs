using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Task.Desktop.Modes;

namespace Task.Desktop.Analytics;

internal enum UsageEvent
{
    AppOpened,
    TaskCreated,
    TaskUpdated,
    TaskCompleted,
    TaskPlanned,
    CalendarEventCreated,
    CalendarEventUpdated,
    ProjectCreated,
    InboxItemCaptured,
}

internal sealed record UsageRecord(Guid Id, UsageEvent Event, ApplicationMode Mode, Guid Session, DateTimeOffset Timestamp, Version Version);
internal sealed record CaptureBatch(Guid Id, int Attempt, DateTimeOffset NextAttempt, Guid[] Events);
internal sealed record CaptureResult(HashSet<Guid> Retry, TimeSpan RetryAfter);

internal sealed class PostHogCapture(AnalyticsConfiguration configuration, HttpClient http, TimeProvider clock)
{
    public async Task<CaptureResult> SendAsync(Guid installation, CaptureBatch batch, UsageRecord[] events, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
        request.Headers.UserAgent.ParseAdd("Task-Desktop/1");
        request.Headers.Add("PostHog-Sdk-Info", "task-desktop/1");
        request.Headers.Add("PostHog-Request-Id", batch.Id.ToString("D"));
        request.Headers.Add("PostHog-Attempt", batch.Attempt.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("PostHog-Request-Timestamp", Timestamp(now));
        request.Content = JsonContent.Create(new
        {
            created_at = Timestamp(now),
            batch = events.Select(item => new
            {
                uuid = item.Id,
                @event = EventName(item.Event),
                distinct_id = installation,
                session_id = item.Session,
                timestamp = Timestamp(item.Timestamp),
                options = new { process_person_profile = false, disable_skew_correction = true },
                properties = new Dictionary<string, object>
                {
                    ["app_version"] = item.Version.ToString(),
                    ["mode"] = item.Mode == ApplicationMode.Personal ? "personal" : "corporate",
                    ["task_analytics_schema"] = 1,
                    ["task_analytics_test"] = configuration.IsTest,
                    ["$geoip_disable"] = true,
                },
            }).ToArray(),
        });

        CaptureResult RetryAll(TimeSpan delay = default) => new(events.Select(item => item.Id).ToHashSet(), delay);

        try
        {
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var retry = response.Headers.RetryAfter;
                return RetryAll(retry?.Delta ?? (retry?.Date - now) ?? TimeSpan.Zero);
            }

            if (response.StatusCode != HttpStatusCode.OK) return new([], TimeSpan.Zero);
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object)
                return RetryAll();

            var pending = new HashSet<Guid>();
            foreach (var item in events)
            {
                if (!results.TryGetProperty(item.Id.ToString("D"), out var entry) || entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String
                    || result.GetString() == "retry")
                    pending.Add(item.Id);
            }

            return new(pending, TimeSpan.Zero);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or JsonException)
        {
            return RetryAll();
        }
    }

    private static string Timestamp(DateTimeOffset timestamp) => timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static string EventName(UsageEvent usage) => usage switch
    {
        UsageEvent.AppOpened => "app_opened",
        UsageEvent.TaskCreated => "task_created",
        UsageEvent.TaskUpdated => "task_updated",
        UsageEvent.TaskCompleted => "task_completed",
        UsageEvent.TaskPlanned => "task_planned",
        UsageEvent.CalendarEventCreated => "calendar_event_created",
        UsageEvent.CalendarEventUpdated => "calendar_event_updated",
        UsageEvent.ProjectCreated => "project_created",
        UsageEvent.InboxItemCaptured => "inbox_item_captured",
        _ => throw new ArgumentOutOfRangeException(nameof(usage)),
    };
}
