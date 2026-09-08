using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Task.Api;

internal sealed class TaskApiMetrics
{
    private static readonly string[] StatusClasses = ["1xx", "2xx", "3xx", "4xx", "5xx"];
    private readonly long[] _requests = new long[StatusClasses.Length];
    private long _durationTicks;
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    public void Record(int statusCode, TimeSpan duration)
    {
        var index = Math.Clamp(statusCode / 100 - 1, 0, StatusClasses.Length - 1);
        Interlocked.Increment(ref _requests[index]);
        Interlocked.Add(ref _durationTicks, duration.Ticks);
    }

    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HELP task_api_process_start_time_seconds Unix time when the API process started.");
        builder.AppendLine("# TYPE task_api_process_start_time_seconds gauge");
        builder.Append("task_api_process_start_time_seconds ")
            .AppendLine(_startedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("# HELP task_api_http_requests_total Completed HTTP requests grouped by status class.");
        builder.AppendLine("# TYPE task_api_http_requests_total counter");
        for (var index = 0; index < StatusClasses.Length; index++)
        {
            builder.Append("task_api_http_requests_total{status_class=\"")
                .Append(StatusClasses[index])
                .Append("\"} ")
                .AppendLine(Interlocked.Read(ref _requests[index]).ToString(CultureInfo.InvariantCulture));
        }

        long requestCount = 0;
        for (var index = 0; index < _requests.Length; index++)
            requestCount += Interlocked.Read(ref _requests[index]);
        builder.AppendLine("# HELP task_api_http_request_duration_seconds_sum Sum of completed request durations.");
        builder.AppendLine("# TYPE task_api_http_request_duration_seconds_sum counter");
        builder.Append("task_api_http_request_duration_seconds_sum ")
            .AppendLine(TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks)).TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
        builder.AppendLine("# HELP task_api_http_request_duration_seconds_count Completed requests included in duration sum.");
        builder.AppendLine("# TYPE task_api_http_request_duration_seconds_count counter");
        builder.Append("task_api_http_request_duration_seconds_count ")
            .AppendLine(requestCount.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}

internal static class TaskApiMetricsAuthorization
{
    public static bool IsAuthorized(HttpContext context, string? tokenFile)
    {
        if (string.IsNullOrWhiteSpace(tokenFile) || !Path.IsPathFullyQualified(tokenFile) || !File.Exists(tokenFile))
            return false;

        var expected = File.ReadAllText(tokenFile).Trim();
        if (expected.Length < 32)
            return false;

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var actualBytes = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
