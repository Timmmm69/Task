using System.Globalization;
using System.Text;

namespace Task.BackupAgent;

public sealed class BackupMetricsPublisher(
    BackupSchedule schedule,
    BackupOptions options,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<BackupMetricsPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan PublishPeriod = TimeSpan.FromSeconds(15);

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = configuration["Task:Monitoring:MetricsPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("Backup Prometheus textfile publishing is disabled; Task:Monitoring:MetricsPath is not configured");
            return;
        }
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Task:Monitoring:MetricsPath must be an absolute path.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (!stoppingToken.IsCancellationRequested)
        {
            WriteAtomic(path, Render(schedule.State, options.Enabled, timeProvider.GetUtcNow(), ReadCapacities()));
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

    internal static string Render(
        BackupState state,
        bool enabled,
        DateTimeOffset generatedAtUtc,
        IReadOnlyDictionary<string, (long Available, long Total)> capacities)
    {
        var builder = new StringBuilder();
        Gauge(builder, "task_backup_metrics_generated_unixtime_seconds", generatedAtUtc.ToUnixTimeSeconds());
        Gauge(builder, "task_backup_enabled", enabled ? 1 : 0);
        Gauge(builder, "task_backup_last_backup_unixtime_seconds", Unix(state.LastBackup));
        Gauge(builder, "task_backup_last_check_unixtime_seconds", Unix(state.LastCheck));
        Gauge(builder, "task_backup_last_restore_test_unixtime_seconds", Unix(state.LastRestoreTest));
        Gauge(builder, "task_backup_last_attempt_unixtime_seconds", Unix(state.LastAttempt));
        Gauge(builder, "task_backup_failed", state.FailedOperation is null ? 0 : 1);
        builder.AppendLine("# TYPE task_backup_filesystem_available_bytes gauge");
        builder.AppendLine("# TYPE task_backup_filesystem_size_bytes gauge");
        foreach (var capacity in capacities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            GaugeSample(builder, "task_backup_filesystem_available_bytes", capacity.Value.Available, capacity.Key);
            GaugeSample(builder, "task_backup_filesystem_size_bytes", capacity.Value.Total, capacity.Key);
        }
        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, (long Available, long Total)> ReadCapacities()
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        foreach (var pair in new[]
        {
            (Name: "local", Path: "/backup/local"),
            (Name: "offhost", Path: "/backup/offhost"),
            (Name: "restore", Path: "/restore"),
        })
        {
            if (!Directory.Exists(pair.Path))
            {
                result[pair.Name] = (0, 0);
                continue;
            }

            // Query the mounted path itself. Using Path.GetPathRoot here would collapse every
            // Linux bind/volume mount to "/" and silently report container-root capacity.
            var drive = new DriveInfo(Path.GetFullPath(pair.Path));
            result[pair.Name] = (drive.AvailableFreeSpace, drive.TotalSize);
        }
        return result;
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static long Unix(DateTimeOffset? value) => value?.ToUnixTimeSeconds() ?? 0;

    private static void Gauge(StringBuilder builder, string name, double value, string? location = null)
    {
        builder.Append("# TYPE ").Append(name).AppendLine(" gauge");
        GaugeSample(builder, name, value, location);
    }

    private static void GaugeSample(StringBuilder builder, string name, double value, string? location = null)
    {
        builder.Append(name);
        if (location is not null) builder.Append("{location=\"").Append(location).Append("\"}");
        builder.Append(' ').AppendLine(value.ToString("R", CultureInfo.InvariantCulture));
    }
}
