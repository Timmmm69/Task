using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Task.Desktop.Converters;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public sealed class VisualFoundationTests
{
    [Fact]
    public void Theme_LoadsCanonicalResourcesAndIcons()
    {
        var snapshot = RunOnSta(() =>
        {
            var theme = (ResourceDictionary)global::System.Windows.Application.LoadComponent(
                new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative));

            return new
            {
                Brand = (Color)theme["Task.Color.Brand.Primary"],
                Strong = (Color)theme["Task.Color.Brand.Strong"],
                Soft = (Color)theme["Task.Color.Brand.Soft"],
                Text = (Color)theme["Task.Color.Text.Primary"],
                Secondary = (Color)theme["Task.Color.Text.Secondary"],
                Surface = (Color)theme["Task.Color.Surface.Base"],
                Subtle = (Color)theme["Task.Color.Surface.Subtle"],
                Border = (Color)theme["Task.Color.Border.Default"],
                Critical = (Color)theme["Task.Color.Semantic.Critical"],
                Success = (Color)theme["Task.Color.Semantic.Success"],
                Warning = (Color)theme["Task.Color.Semantic.Warning"],
                ControlHeight = (double)theme["Task.Control.Height.Compact"],
                NavigationRow = (double)theme["Task.Navigation.RowHeight"],
                ExpandedNavigation = (double)theme["Task.Shell.Navigation.ExpandedWidth"],
                CompactNavigation = (double)theme["Task.Shell.Navigation.CompactWidth"],
                Header = (double)theme["Task.Shell.HeaderHeight"],
                Footer = (double)theme["Task.Shell.FooterHeight"],
                TaskRow = (double)theme["Task.TaskRow.MinHeight"],
                Focus = (double)theme["Task.Stroke.Focus"],
                Radius = (CornerRadius)theme["Task.Radius.Control"],
                TasksIcon = theme["Task.Icon.Tasks"] as Geometry,
                ConnectedIcon = theme["Task.Icon.Connected"] as Geometry,
                PriorityIcon = theme["Task.Icon.Priority.Critical"] as Geometry,
            };
        });

        Assert.Equal("#FF0F6CBD", snapshot.Brand.ToString());
        Assert.Equal("#FF005A9E", snapshot.Strong.ToString());
        Assert.Equal("#FFEAF3FF", snapshot.Soft.ToString());
        Assert.Equal("#FF1B1A19", snapshot.Text.ToString());
        Assert.Equal("#FF605E5C", snapshot.Secondary.ToString());
        Assert.Equal("#FFFFFFFF", snapshot.Surface.ToString());
        Assert.Equal("#FFFAFAFA", snapshot.Subtle.ToString());
        Assert.Equal("#FFE1DFDD", snapshot.Border.ToString());
        Assert.Equal("#FFD13438", snapshot.Critical.ToString());
        Assert.Equal("#FF107C10", snapshot.Success.ToString());
        Assert.Equal("#FFF2A900", snapshot.Warning.ToString());
        Assert.Equal(40, snapshot.ControlHeight);
        Assert.Equal(52, snapshot.NavigationRow);
        Assert.Equal(212, snapshot.ExpandedNavigation);
        Assert.Equal(178, snapshot.CompactNavigation);
        Assert.Equal(70, snapshot.Header);
        Assert.Equal(46, snapshot.Footer);
        Assert.Equal(60, snapshot.TaskRow);
        Assert.Equal(2, snapshot.Focus);
        Assert.Equal(new CornerRadius(5), snapshot.Radius);
        Assert.NotNull(snapshot.TasksIcon);
        Assert.NotNull(snapshot.ConnectedIcon);
        Assert.NotNull(snapshot.PriorityIcon);
    }

    [Theory]
    [InlineData(1487, 212)]
    [InlineData(1220, 212)]
    [InlineData(1219, 178)]
    [InlineData(800, 178)]
    public void NavigationWidth_UsesCanonicalCompactBreakpoint(double windowWidth, double expected)
    {
        var converter = new ShellNavigationWidthConverter();

        var result = (GridLength)converter.Convert(
            windowWidth,
            typeof(GridLength),
            null,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(1320, "1320:336", 336)]
    [InlineData(1319, "1320:336", 0)]
    [InlineData(1100, "1120:126", 0)]
    [InlineData(1120, "1120:126", 126)]
    public void ResponsiveColumns_SelectExpectedWidth(double windowWidth, string parameter, double expected)
    {
        var converter = new ResponsiveGridLengthConverter();

        var result = (GridLength)converter.Convert(
            windowWidth,
            typeof(GridLength),
            parameter,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(DesktopTaskStatus.New, "Task.Icon.Status.New", TaskVisualTone.Neutral)]
    [InlineData(DesktopTaskStatus.InProgress, "Task.Icon.Status.InProgress", TaskVisualTone.Brand)]
    [InlineData(DesktopTaskStatus.Review, "Task.Icon.Status.Review", TaskVisualTone.Warning)]
    [InlineData(DesktopTaskStatus.Completed, "Task.Icon.Status.Completed", TaskVisualTone.Success)]
    [InlineData(DesktopTaskStatus.Cancelled, "Task.Icon.Status.Cancelled", TaskVisualTone.Neutral)]
    public void StatusMapping_IsTextPlusShapeAndSemanticTone(
        DesktopTaskStatus status,
        string expectedIcon,
        TaskVisualTone expectedTone)
    {
        var item = new TaskItemViewModel(CreateTask(status: status));

        Assert.False(string.IsNullOrWhiteSpace(item.StatusText));
        Assert.Equal(expectedIcon, item.StatusIconKey);
        Assert.Equal(expectedTone, item.StatusTone);
        Assert.Contains("Статус:", item.AutomationName);
    }

    [Theory]
    [InlineData(DesktopTaskPriority.Low, "Task.Icon.Priority.Low", TaskVisualTone.Success)]
    [InlineData(DesktopTaskPriority.Normal, "Task.Icon.Priority.Normal", TaskVisualTone.Warning)]
    [InlineData(DesktopTaskPriority.High, "Task.Icon.Priority.High", TaskVisualTone.Critical)]
    [InlineData(DesktopTaskPriority.Critical, "Task.Icon.Priority.Critical", TaskVisualTone.Critical)]
    public void PriorityMapping_IsTextPlusDirectionalShapeAndSemanticTone(
        DesktopTaskPriority priority,
        string expectedIcon,
        TaskVisualTone expectedTone)
    {
        var item = new TaskItemViewModel(CreateTask(priority: priority));

        Assert.False(string.IsNullOrWhiteSpace(item.PriorityText));
        Assert.NotEqual("Средний", item.PriorityText);
        Assert.Equal(expectedIcon, item.PriorityIconKey);
        Assert.Equal(expectedTone, item.PriorityTone);
        Assert.Contains("Приоритет:", item.AutomationName);
    }

    [Fact]
    public void NavigationItems_HaveOfficialIconKeysAccessibleNamesAndStableIds()
    {
        using var viewModel = new MainWindowViewModel();

        Assert.All(viewModel.Sections, section =>
        {
            Assert.StartsWith("Task.Icon.", section.IconKey, StringComparison.Ordinal);
            Assert.Equal($"Navigation_{section.Route}", section.AutomationId);
            Assert.Contains(section.Title, section.NavigationHelpText, StringComparison.Ordinal);
        });
        Assert.Equal("tasks", viewModel.Sections.Single(section => section.Route == "tasks").Route);
    }

    [Fact]
    public void Xaml_PreservesAutomationContractAndNonColorSelectionIndicators()
    {
        var mainWindow = File.ReadAllText(ProjectFile("src", "Task.Desktop", "MainWindow.xaml"));
        var navigation = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.Navigation.xaml"));
        var data = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.Data.xaml"));
        var states = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.States.xaml"));
        var buttons = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.Buttons.xaml"));

        var stableAutomationIds = new[]
        {
            "MainWindow", "NavigationListBox", "ConnectionStatusText", "LogoutButton",
            "SessionMessageText", "SelectedSectionArea", "TasksScreen", "TasksRefreshButton",
            "NewTaskButton", "ReadOnlyNoticeText", "TasksStateMessage", "TasksList",
            "TaskDetailsArea", "TaskDetailsState", "TasksLoadMoreButton",
        };
        Assert.All(stableAutomationIds, id =>
            Assert.Contains($"AutomationProperties.AutomationId=\"{id}\"", mainWindow, StringComparison.Ordinal));

        Assert.Contains("AutomationProperties.HelpText=\"{Binding ReadOnlyActionReason}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedRail\"", navigation, StringComparison.Ordinal);
        Assert.Contains("BorderBrush", navigation, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedRail\"", data, StringComparison.Ordinal);
        Assert.Contains("Task.Thickness.Focus", buttons, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", navigation, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", data, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", states, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", states, StringComparison.OrdinalIgnoreCase);
    }

    private static DesktopTaskDto CreateTask(
        DesktopTaskStatus status = DesktopTaskStatus.New,
        DesktopTaskPriority priority = DesktopTaskPriority.Normal) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            "Проверить визуальную основу",
            Guid.NewGuid(),
            status,
            priority,
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            [],
            [],
            null);

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

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException($"WPF resource load failed: {failure}");
        }

        return result!;
    }
}
