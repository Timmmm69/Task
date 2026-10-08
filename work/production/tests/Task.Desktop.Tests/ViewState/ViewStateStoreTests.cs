using System.IO;
using System.Text.Json;
using Task.Desktop.Infrastructure;

namespace Task.Desktop.Tests.ViewStateTests;

public sealed class ViewStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-view-state-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _user = Guid.NewGuid();
    private ViewStateStore Create(string device = "device-a", Guid? user = null, List<string>? diagnostics = null) =>
        new(_root, "server/org", user ?? _user, device, TimeSpan.FromMilliseconds(120), diagnostics is null ? null : diagnostics.Add);

    [Fact]
    public async System.Threading.Tasks.Task Restart_RestoresLogicalWidthsSortAndBothSectionStates()
    {
        await using (var store = Create())
        {
            await store.Ready;
            store.SetWidth("work-links/v1/type", 155.5, 60, 500);
            store.SetExpanded("today/v1/queue", false);
            store.SetExpanded("projects/v1/tasks", true);
            store.SetSort("work-links/v1", new("Title", true));
            await store.FlushAsync();
            var text = await File.ReadAllTextAsync(store.FilePath);
            Assert.Contains("155.5", text); Assert.DoesNotContain("dpi", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_user.ToString(), Path.GetFileName(store.FilePath));
        }
        await using var restarted = Create(); await restarted.Ready;
        Assert.Equal(155.5, restarted.Width("work-links/v1/type", 60, 500));
        Assert.False(restarted.Expanded("today/v1/queue"));
        Assert.True(restarted.Expanded("projects/v1/tasks"));
        Assert.Equal(new ViewSort("Title", true), restarted.Sort("work-links/v1"));
    }

    [Theory]
    [InlineData(2, 60)] [InlineData(10000, 500)] [InlineData(125.25, 125.25)]
    public async System.Threading.Tasks.Task Width_ClampsToCurrentControlLimits(double input, double expected)
    {
        await using var store = Create(); await store.Ready;
        store.SetWidth("work-links/v1/type", input, 60, 500);
        await store.FlushAsync();
        await using var restart = Create(); await restart.Ready;
        Assert.Equal(expected, restart.Width("work-links/v1/type", 60, 500));
        Assert.InRange(restart.Width("work-links/v1/type", 80, 200)!.Value, 80, 200);
    }

    [Fact]
    public async System.Threading.Tasks.Task UnknownColumnsSectionsAndCorruptIndividualValues_KeepDefaultsAndValidPreferences()
    {
        var diagnostics = new List<string>();
        await using var store = Create(diagnostics: diagnostics);
        await store.Ready;
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        await File.WriteAllTextAsync(store.FilePath, """
            {"version":1,"widths":{"work-links/v1/deleted":90,"work-links/v1/type":"NaN","work-links/v1/title":-1},
            "sections":{"today/v1/queue":false,"today/v1/deleted":true},
            "sorts":{"work-links/v1":{"Field":"RemovedField","Descending":true}}}
            """);
        await using var restart = Create(diagnostics: diagnostics); await restart.Ready;
        Assert.Null(restart.Width("work-links/v1/type", 60, 500));
        Assert.Null(restart.Width("work-links/v1/title", 100, 1600));
        Assert.Null(restart.Width("work-links/v1/deleted", 60, 500));
        Assert.Null(restart.Width("work-links/v1/new", 60, 500));
        Assert.Null(restart.Expanded("today/v1/deleted"));
        Assert.False(restart.Expanded("today/v1/queue"));
        Assert.Null(restart.Sort("work-links/v1"));
        Assert.Equal(3, diagnostics.Count);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"version\":2,\"widths\":{},\"sections\":{},\"sorts\":{}}")]
    [InlineData("{\"version\":1,\"widths\":null,\"sections\":{},\"sorts\":{}}")]
    public async System.Threading.Tasks.Task CorruptOrUnsupportedStore_IsDiagnosedAndDiscarded(string json)
    {
        var diagnostics = new List<string>();
        await using var first = Create(); await first.Ready;
        Directory.CreateDirectory(Path.GetDirectoryName(first.FilePath)!);
        await File.WriteAllTextAsync(first.FilePath, json);
        await using var recovered = Create(diagnostics: diagnostics); await recovered.Ready;
        Assert.Null(recovered.Expanded("today/v1/queue"));
        Assert.Single(diagnostics); Assert.False(File.Exists(first.FilePath));
        recovered.SetExpanded("today/v1/queue", false); await recovered.FlushAsync();
        await using var restart = Create(); await restart.Ready;
        Assert.False(restart.Expanded("today/v1/queue"));
    }

    [Fact]
    public async System.Threading.Tasks.Task UserDeviceAuthorityAndSurface_Isolation()
    {
        await using var a = Create(); await a.Ready;
        a.SetWidth("work-links/v1/type", 200, 60, 500);
        a.SetExpanded("projects/v1/tasks", false); await a.FlushAsync();
        await using var b = Create(user: Guid.NewGuid());
        await using var otherDevice = Create(device: "device-b");
        await using var otherServer = new ViewStateStore(_root, "other-server/org", _user, "device-a");
        await System.Threading.Tasks.Task.WhenAll(b.Ready, otherDevice.Ready, otherServer.Ready);
        Assert.Null(b.Width("work-links/v1/type", 60, 500));
        Assert.Null(otherDevice.Expanded("projects/v1/tasks"));
        Assert.Null(otherServer.Expanded("projects/v1/tasks"));
        Assert.Null(a.Width("personal-links/v1/type", 60, 500));
        Assert.Null(a.Expanded("projects/v2/tasks"));
        Assert.NotEqual(a.FilePath, b.FilePath); Assert.NotEqual(a.FilePath, otherDevice.FilePath);
    }

    [Fact]
    public async System.Threading.Tasks.Task Debounce_NoSynchronousWritesAndFinalSnapshotSurvivesOrphanTempAndReset()
    {
        await using var store = Create(); await store.Ready;
        for (int pixel = 100; pixel < 300; pixel++) store.SetWidth("work-links/v1/type", pixel, 60, 500);
        Assert.Equal(0, store.WriteCount); Assert.False(File.Exists(store.FilePath));
        for (int attempt = 0; attempt < 100 && store.WriteCount == 0; attempt++) await System.Threading.Tasks.Task.Delay(20);
        Assert.Equal(1, store.WriteCount);
        await File.WriteAllTextAsync(store.FilePath + ".orphan.tmp", "partial crash payload");
        await using var restart = Create(); await restart.Ready;
        Assert.Equal(299, restart.Width("work-links/v1/type", 60, 500));
        await restart.ResetAsync();
        await using var reset = Create(); await reset.Ready;
        Assert.Null(reset.Width("work-links/v1/type", 60, 500));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async System.Threading.Tasks.Task Sort_AscendingAndDescendingSurviveRestart(bool descending)
    {
        await using var store = Create(); await store.Ready;
        store.SetSort("work-links/v1", new("TypeLabel", descending));
        store.SetSort("tasks/v1", new("Title", descending)); // Cursor API has no sortable fields.
        await store.FlushAsync();
        await using var restart = Create(); await restart.Ready;
        Assert.Equal(new ViewSort("TypeLabel", descending), restart.Sort("work-links/v1"));
        Assert.Null(restart.Sort("tasks/v1"));
    }

    [Fact]
    public async System.Threading.Tasks.Task FailedAtomicReplace_PreservesPreviousPreferenceAndCanRetry()
    {
        var diagnostics = new List<string>();
        await using var store = Create(diagnostics: diagnostics); await store.Ready;
        store.SetExpanded("today/v1/queue", false); await store.FlushAsync();
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.SetExpanded("today/v1/queue", true); await store.FlushAsync();
            using var document = JsonDocument.Parse(locked);
            Assert.False(document.RootElement.GetProperty("sections").GetProperty("today/v1/queue").GetBoolean());
            Assert.NotEmpty(diagnostics);
        }
        await store.FlushAsync();
        await using var restart = Create(); await restart.Ready;
        Assert.True(restart.Expanded("today/v1/queue"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
