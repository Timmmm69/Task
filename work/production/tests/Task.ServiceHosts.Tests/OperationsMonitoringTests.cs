using Microsoft.AspNetCore.Http;
using Task.Api;
using Task.BackupAgent;
using Task.Worker;

namespace Task.ServiceHosts.Tests;

public sealed class OperationsMonitoringTests
{
    [Fact]
    public void ApiMetrics_ExposeBoundedStatusClassesAndDurations()
    {
        var metrics = new TaskApiMetrics();
        metrics.Record(200, TimeSpan.FromMilliseconds(25));
        metrics.Record(503, TimeSpan.FromMilliseconds(75));

        var text = metrics.Render();

        Assert.Contains("task_api_http_requests_total{status_class=\"2xx\"} 1", text);
        Assert.Contains("task_api_http_requests_total{status_class=\"5xx\"} 1", text);
        Assert.Contains("task_api_http_request_duration_seconds_count 2", text);
        Assert.DoesNotContain("/api/", text);
    }

    [Fact]
    public void ApiMetricsAuthorization_RequiresExactLongBearerTokenFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"task-metrics-{Guid.NewGuid():N}.token");
        const string token = "0123456789abcdef0123456789abcdef";
        File.WriteAllText(path, token);
        try
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = $"Bearer {token}";
            Assert.True(TaskApiMetricsAuthorization.IsAuthorized(context, path));
            context.Request.Headers.Authorization = "Bearer wrong";
            Assert.False(TaskApiMetricsAuthorization.IsAuthorized(context, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WorkerState_ExportsHeartbeatFailureAndConfigurationWithoutSensitiveData()
    {
        var state = new WorkerOperationalState();
        state.Register("outbox.publish", TimeSpan.FromSeconds(15), configured: true);
        state.MarkAttempt("outbox.publish");
        state.MarkFailure("outbox.publish");

        var text = state.RenderPrometheus();

        Assert.Contains("task_worker_configured{worker=\"outbox.publish\"} 1", text);
        Assert.Contains("task_worker_failures_total{worker=\"outbox.publish\"} 1", text);
        Assert.Contains("task_worker_consecutive_failures{worker=\"outbox.publish\"} 1", text);
    }

    [Fact]
    public void BackupMetrics_ExposeFreshnessFailureAndCapacity()
    {
        var instant = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var state = new BackupState(instant.AddHours(-1), instant.AddMinutes(-1), instant.AddDays(-1), instant, "check");
        var capacity = new Dictionary<string, (long Available, long Total)>
        {
            ["offhost"] = (25, 100),
        };

        var text = BackupMetricsPublisher.Render(
            state,
            enabled: true,
            generatedAtUtc: instant,
            capacities: capacity);

        Assert.Contains("task_backup_failed 1", text);
        Assert.Contains("task_backup_filesystem_available_bytes{location=\"offhost\"} 25", text);
        Assert.Contains("task_backup_filesystem_size_bytes{location=\"offhost\"} 100", text);
    }
}
