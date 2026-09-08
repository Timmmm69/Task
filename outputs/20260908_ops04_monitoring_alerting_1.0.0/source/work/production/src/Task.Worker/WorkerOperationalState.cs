using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Task.Worker;

public sealed class WorkerOperationalState(TimeProvider? timeProvider = null)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public void Register(string worker, TimeSpan maxSilence, bool configured)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);
        if (maxSilence <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxSilence));
        _entries.AddOrUpdate(worker, _ => new Entry(maxSilence, configured), (_, entry) =>
        {
            lock (entry.Gate)
            {
                entry.MaxSilence = maxSilence;
                entry.Configured = configured;
            }
            return entry;
        });
    }

    public void MarkAttempt(string worker) => Update(worker, entry => entry.LastAttemptUtc = _timeProvider.GetUtcNow());

    public void MarkSuccess(string worker) => Update(worker, entry =>
    {
        entry.LastSuccessUtc = _timeProvider.GetUtcNow();
        entry.ConsecutiveFailures = 0;
    });

    public void MarkFailure(string worker) => Update(worker, entry =>
    {
        entry.LastFailureUtc = _timeProvider.GetUtcNow();
        entry.Failures++;
        entry.ConsecutiveFailures++;
    });

    public string RenderPrometheus()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HELP task_worker_metrics_generated_unixtime_seconds Time when this worker snapshot was generated.");
        builder.AppendLine("# TYPE task_worker_metrics_generated_unixtime_seconds gauge");
        builder.Append("task_worker_metrics_generated_unixtime_seconds ")
            .AppendLine(_timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        AppendHelp(builder, "task_worker_configured", "Whether the worker has its required dependencies.", "gauge");
        AppendHelp(builder, "task_worker_max_silence_seconds", "Maximum expected time between successful passes.", "gauge");
        AppendHelp(builder, "task_worker_last_attempt_unixtime_seconds", "Last pass attempt time.", "gauge");
        AppendHelp(builder, "task_worker_last_success_unixtime_seconds", "Last successful pass time.", "gauge");
        AppendHelp(builder, "task_worker_last_failure_unixtime_seconds", "Last failed pass time.", "gauge");
        AppendHelp(builder, "task_worker_failures_total", "Total failed passes or deliveries.", "counter");
        AppendHelp(builder, "task_worker_consecutive_failures", "Current consecutive failed pass count.", "gauge");

        foreach (var pair in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            lock (pair.Value.Gate)
            {
                Append(builder, "task_worker_configured", pair.Key, pair.Value.Configured ? 1 : 0);
                Append(builder, "task_worker_max_silence_seconds", pair.Key, pair.Value.MaxSilence.TotalSeconds);
                Append(builder, "task_worker_last_attempt_unixtime_seconds", pair.Key, Unix(pair.Value.LastAttemptUtc));
                Append(builder, "task_worker_last_success_unixtime_seconds", pair.Key, Unix(pair.Value.LastSuccessUtc));
                Append(builder, "task_worker_last_failure_unixtime_seconds", pair.Key, Unix(pair.Value.LastFailureUtc));
                Append(builder, "task_worker_failures_total", pair.Key, pair.Value.Failures);
                Append(builder, "task_worker_consecutive_failures", pair.Key, pair.Value.ConsecutiveFailures);
            }
        }

        return builder.ToString();
    }

    private void Update(string worker, Action<Entry> update)
    {
        if (!_entries.TryGetValue(worker, out var entry))
            throw new InvalidOperationException($"Worker '{worker}' is not registered for operational monitoring.");
        lock (entry.Gate) update(entry);
    }

    private static void AppendHelp(StringBuilder builder, string metric, string help, string type) => builder
        .Append("# HELP ").Append(metric).Append(' ').AppendLine(help)
        .Append("# TYPE ").Append(metric).Append(' ').AppendLine(type);

    private static void Append(StringBuilder builder, string metric, string worker, double value) => builder
        .Append(metric).Append("{worker=\"").Append(Escape(worker)).Append("\"} ")
        .AppendLine(value.ToString("R", CultureInfo.InvariantCulture));

    private static long Unix(DateTimeOffset? value) => value?.ToUnixTimeSeconds() ?? 0;

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class Entry(TimeSpan maxSilence, bool configured)
    {
        public object Gate { get; } = new();
        public TimeSpan MaxSilence { get; set; } = maxSilence;
        public bool Configured { get; set; } = configured;
        public DateTimeOffset? LastAttemptUtc { get; set; }
        public DateTimeOffset? LastSuccessUtc { get; set; }
        public DateTimeOffset? LastFailureUtc { get; set; }
        public long Failures { get; set; }
        public long ConsecutiveFailures { get; set; }
    }
}

public sealed class WorkerMetricsPublisher(
    WorkerOperationalState state,
    IConfiguration configuration,
    ILogger<WorkerMetricsPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan PublishPeriod = TimeSpan.FromSeconds(5);

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = configuration["Task:Monitoring:MetricsPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("Worker Prometheus textfile publishing is disabled; Task:Monitoring:MetricsPath is not configured");
            return;
        }
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Task:Monitoring:MetricsPath must be an absolute path.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (!stoppingToken.IsCancellationRequested)
        {
            WriteAtomic(path, state.RenderPrometheus());
            try
            {
                await global::System.Threading.Tasks.Task.Delay(PublishPeriod, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
