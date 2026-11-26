using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Resources;
using System.Security.Cryptography;
using Task.Desktop.Modes;

namespace Task.Desktop.Tests;

/// <summary>Runs the actual WPF entry point in an isolated data directory.</summary>
public sealed class ApplicationModeStartupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Task-mode-startup", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DesktopIncludesTheApprovedVoravaDentResourcesAndExecutableMetadata()
    {
        var apphost = FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "Task.Desktop.exe"));
        Assert.Equal("Варава Дент", apphost.ProductName);
        Assert.Equal("Варава Дент", apphost.FileDescription);

        var resources = new ResourceManager("Task.Desktop.g", typeof(App).Assembly);
        using var logo = resources.GetStream("assets/voravadent.png");
        using var icon = resources.GetStream("assets/voravadent.ico");
        Assert.NotNull(logo);
        Assert.NotNull(icon);
        Assert.Equal("EED335E62E1C93A77D1C29D60617C59DD2F6C9FF9FE60B3FC2EA871181982F96", Convert.ToHexString(SHA256.HashData(logo)));
        Assert.Equal("56F2B72F0A4E511C4B12E972A7CED8E325C6B1593AB1FE810C573518A1994DF9", Convert.ToHexString(SHA256.HashData(icon)));
        Assert.Null(resources.GetStream("assets/task.ico"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NoPreference_ActualStartupOpensSelector()
    {
        using var process = Launch();
        try { await AssertWindow(process, "Варава Дент — выбор режима"); }
        finally { Stop(process); }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Personal_ActualStartupNeverProbesServerOrReadsCorporateCredentials()
    {
        Directory.CreateDirectory(_directory);
        new ApplicationModePreference(_directory).Save(ApplicationMode.Personal);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var settingsPath = Path.Combine(_directory, "server-settings.json");
        var vaultPath = Path.Combine(_directory, "credentials.bin");
        var settings = $"{{\"version\":1,\"baseUrl\":\"https://127.0.0.1:{port}/\"}}";
        File.WriteAllText(settingsPath, settings);
        File.WriteAllBytes(vaultPath, [1, 2, 3, 4]);
        using var process = Launch();
        try
        {
            await AssertWindow(process, "Варава Дент — личное пространство");
            await global::System.Threading.Tasks.Task.Delay(200);
            Assert.False(server.Pending()); // No probe, login, restore or authenticated request.
            Assert.Equal(settings, File.ReadAllText(settingsPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(vaultPath));
            Assert.Equal(ApplicationMode.Personal, new ApplicationModePreference(_directory).Load());
            Assert.Empty(Directory.GetFiles(_directory, "*.corrupt*"));
        }
        finally { Stop(process); }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Corporate_ActualStartupUsesExistingAuthenticationWindow()
    {
        new ApplicationModePreference(_directory).Save(ApplicationMode.Corporate);
        using var process = Launch();
        try { await AssertWindow(process, "Варава Дент — вход в пространство компании"); }
        finally { Stop(process); }
    }

    private Process Launch()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Task.Desktop.exe");
        Assert.True(File.Exists(executable), $"Missing desktop apphost: {executable}");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        };
        start.Environment["TASK_DESKTOP_DATA_DIRECTORY"] = _directory;
        return Process.Start(start)!;
    }

    private static async global::System.Threading.Tasks.Task AssertWindow(Process process, string title)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            Assert.False(process.HasExited, "Task exited before opening its selected context");
            process.Refresh();
            if (process.MainWindowTitle == title) return;
            await global::System.Threading.Tasks.Task.Delay(25);
        }
        Assert.Equal(title, process.MainWindowTitle);
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
