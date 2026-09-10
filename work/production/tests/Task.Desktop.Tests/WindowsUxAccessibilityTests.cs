using System.Globalization;
using System.IO;
using System.Xml.Linq;
using System.Windows;

namespace Task.Desktop.Tests;

public sealed class WindowsUxAccessibilityTests
{
    private static readonly (double Scale, double Width, double Height)[] WindowsMatrix =
    [
        (1.00, 1920, 1040),
        (1.25, 1920 / 1.25, 1040 / 1.25),
        (1.50, 1920 / 1.50, 1040 / 1.50),
        (2.00, 1920 / 2.00, 1040 / 2.00),
    ];

    [Fact]
    public void DesktopManifest_DeclaresPerMonitorV2AndAsInvoker()
    {
        var project = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Task.Desktop.csproj"));
        var manifest = File.ReadAllText(ProjectFile("src", "Task.Desktop", "app.manifest"));

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project, StringComparison.Ordinal);
        Assert.Contains(">PerMonitorV2, PerMonitor</dpiAwareness>", manifest, StringComparison.Ordinal);
        Assert.Contains("requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsUxRunner_UsesAnIsolatedDesktopDataDirectory()
    {
        var app = File.ReadAllText(ProjectFile("src", "Task.Desktop", "App.xaml.cs"));
        var cleanStand = File.ReadAllText(ProjectFile("verification", "Test-TaskWriteE2E.ps1"));
        var runner = File.ReadAllText(ProjectFile("verification", "Test-Desk05WindowsUx.ps1"));

        Assert.Contains("TASK_DESKTOP_DATA_DIRECTORY", app, StringComparison.Ordinal);
        Assert.Contains("DesktopAppDataPath must be inside the isolated E2E runtime", cleanStand, StringComparison.Ordinal);
        Assert.Contains("-DesktopAppDataPath', $isolatedDesktopData", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("Move-Item -LiteralPath $env:LOCALAPPDATA", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1120, 700)]
    [InlineData(1200, 900)]
    public void StartupSizing_FitsSupportedWindowsScaleMatrix(double desiredWidth, double desiredHeight)
    {
        var minimum = new Size(800, 480);

        foreach (var entry in WindowsMatrix)
        {
            var workArea = new Size(entry.Width, entry.Height);
            var fitted = WindowsUxLayout.CalculateStartupSize(
                new Size(desiredWidth, desiredHeight), minimum, workArea);

            Assert.InRange(fitted.Width, minimum.Width, workArea.Width);
            Assert.InRange(fitted.Height, minimum.Height, workArea.Height);
        }
    }

    [Fact]
    public void WindowContracts_FitAtTwoHundredPercentAndExposeKeyboardHelp()
    {
        var auth = XDocument.Load(ProjectFile("src", "Task.Desktop", "AuthWindow.xaml")).Root!;
        var main = XDocument.Load(ProjectFile("src", "Task.Desktop", "MainWindow.xaml")).Root!;

        AssertWindowMinimum(auth, 800, 480);
        AssertWindowMinimum(main, 800, 480);
        Assert.Equal("Cycle", Attribute(auth, "TabNavigation"));
        Assert.Contains("Tab", Attribute(auth, "HelpText"), StringComparison.Ordinal);
        Assert.Equal("OnWindowPreviewKeyDown", Attribute(main, "PreviewKeyDown"));
        Assert.Contains("F6", Attribute(main, "HelpText"), StringComparison.Ordinal);

        var scrollViewer = auth.Descendants().Single(element =>
            element.Name.LocalName == "ScrollViewer" && TryAttribute(element, "Padding") == "28");
        Assert.Equal("Auto", Attribute(scrollViewer, "VerticalScrollBarVisibility"));
    }

    [Fact]
    public void PrimaryDesktopSurfaces_ExposeStableWindowsAutomationRoots()
    {
        var contracts = new Dictionary<string, string[]>
        {
            ["MainWindow.xaml"] = ["MainWindow", "NavigationListBox", "SelectedSectionArea", "TasksScreen"],
            [Path.Combine("Views", "TodayView.xaml")] = ["TodayScreen"],
            [Path.Combine("Views", "CalendarView.xaml")] = ["CalendarScreen"],
            [Path.Combine("Views", "ProjectsView.xaml")] = ["ProjectsView"],
            [Path.Combine("Views", "WorkHubView.xaml")] =
                ["WorkHubView", "CatalogList", "ContactsList", "NotificationsList", "SettingsView"],
        };

        foreach (var (relative, ids) in contracts)
        {
            var text = File.ReadAllText(ProjectFile("src", "Task.Desktop", relative));
            Assert.All(ids, id => Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"", text, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CriticalWindowsHeadingsAndLiveRegions_AreDeclaredForScreenReaders()
    {
        var auth = File.ReadAllText(ProjectFile("src", "Task.Desktop", "AuthWindow.xaml"));
        var main = File.ReadAllText(ProjectFile("src", "Task.Desktop", "MainWindow.xaml"));

        Assert.True(Count(auth, "AutomationProperties.HeadingLevel=\"Level1\"") >= 4);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", main, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", auth, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", auth, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", main, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", main, StringComparison.Ordinal);
    }

    private static void AssertWindowMinimum(XElement root, double width, double height)
    {
        Assert.Equal(width, double.Parse(Attribute(root, "MinWidth"), CultureInfo.InvariantCulture));
        Assert.Equal(height, double.Parse(Attribute(root, "MinHeight"), CultureInfo.InvariantCulture));
    }

    private static string Attribute(XElement element, string localName) =>
        TryAttribute(element, localName)
        ?? throw new InvalidOperationException($"Attribute '{localName}' was not found on {element.Name.LocalName}.");

    private static string? TryAttribute(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == localName
            || attribute.Name.LocalName.EndsWith('.' + localName, StringComparison.Ordinal))?.Value;

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private static string ProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Task.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
