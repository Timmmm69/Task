using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Task.Desktop.Infrastructure;

public sealed record ViewSort(string Field, bool Descending);

public interface IViewStateStore
{
    System.Threading.Tasks.Task Ready { get; }
    double? Width(string key, double minimum, double maximum);
    void SetWidth(string key, double value, double minimum, double maximum);
    bool? Expanded(string key);
    void SetExpanded(string key, bool value);
    ViewSort? Sort(string surface);
    void SetSort(string surface, ViewSort sort);
    System.Threading.Tasks.Task FlushAsync();
    System.Threading.Tasks.Task ResetAsync();
}

/// <summary>Fixed UI identifiers only. Never include an object ID, title, path or query.</summary>
internal static class ViewStateKeys
{
    internal static readonly HashSet<string> LinkSurfaces = ["work-links/v1", "personal-links/v1"];
    internal static readonly HashSet<string> Columns = ["type", "title"];
    internal static readonly HashSet<string> SortFields = ["TypeLabel", "Title"];
    internal static readonly HashSet<string> Sections =
    [
        "main/v1/inspector", "inbox/v1/inspector", "projects/v1/members", "projects/v1/tasks",
        "today/v1/queue", "today/v1/inspector", "work/v1/links",
        "task/v1/checklist", "task/v1/subtasks", "task/v1/files", "task/v1/predecessors",
        "task/v1/comments", "task/v1/history", "task-editor/v1/planning",
        "personal-tasks/v1/details", "personal-tasks/v1/planning", "personal-projects/v1/storage",
        "personal-workspace/v1/actions", "personal-workspace/v1/links", "personal-workspace/v1/backup",
        "personal-reminders/v1/create"
    ];
    internal static bool IsWidth(string key) => LinkSurfaces.Any(surface => Columns.Any(column => key == surface + "/" + column));
}

/// <summary>Small device-local preference file, separate from credentials and business caches.
/// All disk operations run on workers; UI setters only mutate bounded in-memory primitives.</summary>
public sealed class ViewStateStore : IViewStateStore, IAsyncDisposable
{
    private const int MaxBytes = 32 * 1024;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly TimeSpan _debounce;
    private readonly Action<string> _diagnostic;
    private Dictionary<string, double> _widths = [];
    private Dictionary<string, bool> _sections = [];
    private Dictionary<string, ViewSort> _sorts = [];
    private long _revision;
    private long _written;
    private bool _disposed;
    private System.Threading.Tasks.Task? _worker;
    public System.Threading.Tasks.Task Ready { get; }
    internal string FilePath { get; }
    internal int WriteCount { get; private set; }

    public ViewStateStore(string directory, string authority, Guid userId, string deviceNamespace,
        TimeSpan? debounce = null, Action<string>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceNamespace);
        if (userId == Guid.Empty) throw new ArgumentException("An opaque user ID is required.", nameof(userId));
        // Length-prefixed components avoid ambiguous namespaces; no PII in filenames.
        var components = new[] { authority, userId.ToString("D"), deviceNamespace };
        var identity = string.Concat(components.Select(value => value.Length + ":" + value));
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        FilePath = Path.Combine(directory, "view-state", name + ".json");
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        if (_debounce <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        _diagnostic = diagnostic ?? (message => Trace.TraceWarning(message));
        Ready = System.Threading.Tasks.Task.Run(Load);
    }

    public double? Width(string key, double minimum, double maximum)
    {
        lock (_sync) return _widths.TryGetValue(key, out var value) ? Clamp(value, minimum, maximum) : null;
    }
    public void SetWidth(string key, double value, double minimum, double maximum)
    {
        if (!ViewStateKeys.IsWidth(key) || !double.IsFinite(value) || value <= 0) return;
        Change(() => _widths[key] = Clamp(value, minimum, maximum));
    }
    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Clamp(value, Math.Max(16, minimum), Math.Max(Math.Max(16, minimum), Math.Min(4096, maximum)));
    public bool? Expanded(string key) { lock (_sync) return _sections.TryGetValue(key, out var value) ? value : null; }
    public void SetExpanded(string key, bool value)
    {
        if (ViewStateKeys.Sections.Contains(key)) Change(() => _sections[key] = value);
    }
    public ViewSort? Sort(string surface) { lock (_sync) return _sorts.GetValueOrDefault(surface); }
    public void SetSort(string surface, ViewSort sort)
    {
        if (ViewStateKeys.LinkSurfaces.Contains(surface) && ViewStateKeys.SortFields.Contains(sort.Field))
            Change(() => _sorts[surface] = sort);
    }
    private void Change(Action change)
    {
        lock (_sync)
        {
            if (_disposed || !Ready.IsCompleted) return;
            change(); _revision++;
            if (_worker is null || _worker.IsCompleted) _worker = System.Threading.Tasks.Task.Run(SaveAfterQuietPeriodAsync);
        }
    }
    private async System.Threading.Tasks.Task SaveAfterQuietPeriodAsync()
    {
        while (true)
        {
            long before;
            lock (_sync) { if (_disposed || _written == _revision) { _worker = null; return; } before = _revision; }
            await System.Threading.Tasks.Task.Delay(_debounce).ConfigureAwait(false);
            lock (_sync) { if (_revision != before) continue; }
            await FlushAsync().ConfigureAwait(false);
            // A failed write is retried on the next edit/flush. Edits made during a
            // successful write must instead get their own quiet period and snapshot.
            lock (_sync) { if (_written != _revision && _revision == before) { _worker = null; return; } }
        }
    }
    public async System.Threading.Tasks.Task FlushAsync()
    {
        await Ready.ConfigureAwait(false);
        await _io.WaitAsync().ConfigureAwait(false);
        try { await System.Threading.Tasks.Task.Run(Write).ConfigureAwait(false); }
        finally { _io.Release(); }
    }
    private void Write()
    {
        byte[] bytes; long revision;
        lock (_sync)
        {
            if (_revision == _written) return;
            revision = _revision;
            bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, widths = _widths, sections = _sections, sorts = _sorts });
        }
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, FilePath, overwrite: true);
            lock (_sync) { _written = revision; WriteCount++; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Report("write failed; defaults remain usable"); }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            using var stream = File.OpenRead(FilePath);
            if (stream.Length > MaxBytes) throw new JsonException();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) throw new JsonException();
            foreach (var item in root.GetProperty("widths").EnumerateObject())
            {
                if (!ViewStateKeys.IsWidth(item.Name)) continue;
                if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetDouble(out var value) && double.IsFinite(value) && value > 0)
                    _widths[item.Name] = Clamp(value, 16, 4096);
                else Report("invalid column preference discarded");
            }
            foreach (var item in root.GetProperty("sections").EnumerateObject())
            {
                if (!ViewStateKeys.Sections.Contains(item.Name)) continue;
                if (item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) _sections[item.Name] = item.Value.GetBoolean();
                else Report("invalid section preference discarded");
            }
            foreach (var item in root.GetProperty("sorts").EnumerateObject())
            {
                if (!ViewStateKeys.LinkSurfaces.Contains(item.Name)) continue;
                try
                {
                    var sort = item.Value.Deserialize<ViewSort>();
                    if (sort is not null && ViewStateKeys.SortFields.Contains(sort.Field)) _sorts[item.Name] = sort;
                    else Report("unsupported sort discarded");
                }
                catch (JsonException) { Report("invalid sort preference discarded"); }
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IOException or UnauthorizedAccessException)
        {
            _widths.Clear(); _sections.Clear(); _sorts.Clear();
            Report("unreadable preference discarded; using defaults");
            try { File.Delete(FilePath); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
        }
    }
    private void Report(string message) { try { _diagnostic("View state: " + message); } catch { /* Diagnostics must not break preferences. */ } }
    public async System.Threading.Tasks.Task ResetAsync()
    {
        await Ready.ConfigureAwait(false);
        Change(() => { _widths.Clear(); _sections.Clear(); _sorts.Clear(); });
        await FlushAsync().ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        System.Threading.Tasks.Task? worker;
        lock (_sync) { _disposed = true; worker = _worker; }
        await FlushAsync().ConfigureAwait(false);
        if (worker is not null) await worker.ConfigureAwait(false);
    }
}
