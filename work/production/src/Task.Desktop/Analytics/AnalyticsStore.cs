using System.IO;
using System.Text.Json;

namespace Task.Desktop.Analytics;

internal sealed record AnalyticsState(string Destination, Guid Installation, List<UsageRecord> Events, CaptureBatch? Batch = null);

/// <summary>A separate, size-limited snapshot. Each replace preserves the previous complete snapshot until commit.</summary>
internal sealed class AnalyticsStore : IDisposable
{
    private readonly string _file;
    private readonly string _temporary;
    private readonly FileStream _lease;

    public AnalyticsStore(string root)
    {
        var directory = Path.Combine(root, "analytics");
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "usage-v1.json");
        _temporary = _file + ".tmp";
        // Only one desktop process may collect or change consent for this data directory.
        _lease = new FileStream(Path.Combine(directory, "usage.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public AnalyticsState? Read()
    {
        File.Delete(_temporary);
        if (!File.Exists(_file)) return null;
        try
        {
            if (new FileInfo(_file).Length > 512 * 1024) throw new JsonException();
            using var file = File.OpenRead(_file);
            var state = JsonSerializer.Deserialize<AnalyticsState>(file);
            if (state is null || state.Installation == Guid.Empty || state.Events is null || state.Events.Count > ProductAnalytics.QueueLimit
                || state.Events.Any(item => item is null || item.Id == Guid.Empty || item.Session == Guid.Empty || item.Version is null
                    || !Enum.IsDefined(item.Event) || !Enum.IsDefined(item.Mode))
                || state.Events.Select(item => item.Id).Distinct().Count() != state.Events.Count
                || state.Batch is { } batch && (batch.Events is null || batch.Events.Length is < 1 or > 20
                    || batch.Id == Guid.Empty || batch.Attempt is < 0 or > ProductAnalytics.MaxAttempts))
                throw new JsonException();
            return state;
        }
        catch (JsonException)
        {
            Delete();
            return null;
        }
    }

    public void Write(AnalyticsState state)
    {
        using (var file = new FileStream(_temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, state);
            file.Flush(flushToDisk: true);
        }
        File.Move(_temporary, _file, overwrite: true);
    }

    public void Delete()
    {
        File.Delete(_temporary);
        File.Delete(_file);
    }

    public void Dispose() => _lease.Dispose();
}
