using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Task.Desktop.Converters;
using Task.Desktop.TaskApi;
using Task.Desktop.Tests.Work;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

namespace Task.Desktop.Tests;

public sealed class VisualFoundationTests
{
    [Fact]
    public async System.Threading.Tasks.Task MainWindow_LoadsAndMeasuresDirection2Shell()
    {
        const string isolatedProbe = "TASK_CORPORATE_LIFECYCLE_SHELL_PROBE";
        if (Environment.GetEnvironmentVariable(isolatedProbe) != "1")
        {
            // Application resources belong to their dispatcher for the lifetime of a WPF process.
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(VisualFoundationTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(MainWindow_LoadsAndMeasuresDirection2Shell));
            start.Environment[isolatedProbe] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Assert.True(child.ExitCode == 0, await output + await errors);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }

            return;
        }

        var snapshot = RunOnSta(() =>
        {
            var application = new App();
            application.InitializeComponent();

            using var workHub = new WorkHubViewModel(new WorkHubViewModelTests.FakeClient(),
                ["History.Read", "Archive.Restore", "Trash.Read", "Trash.Restore"]);
            using var viewModel = new MainWindowViewModel(null, null, workHub: workHub);
            var window = new MainWindow(viewModel);
            try
            {
                window.Measure(new Size(1487, 1058));
                window.Arrange(new Rect(0, 0, 1487, 1058));
                window.UpdateLayout();

                VerifyProjectContactsAndDefaultControls();
                Assert.IsType<CorporateLifecycleView>(LogicalTreeHelper.FindLogicalNode(window, "LifecycleRoot"));
                var surface = new CorporateLifecycleView { DataContext = workHub };
                foreach (var (route, area) in new[] { ("archive", WorkHubArea.Archive), ("trash", WorkHubArea.Trash) })
                {
                    viewModel.SelectedSection = viewModel.Sections.Single(section => section.Route == route);
                    surface.Measure(new Size(600, 360));
                    surface.Arrange(new Rect(0, 0, 600, 360));
                    Assert.Equal(area, workHub.Area);
                    Assert.Equal("LifecycleSurface", AutomationProperties.GetAutomationId(surface));
                    Assert.InRange(surface.ActualWidth, 1, 600);
                }

                return new
                {
                    NavigationFound = window.FindName("NavigationListBox") is FrameworkElement,
                    HeaderFound = window.FindName("HeaderRegion") is FrameworkElement,
                    ContentFound = window.FindName("ContentRegion") is FrameworkElement,
                };
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }
        });

        Assert.True(snapshot.NavigationFound);
        Assert.True(snapshot.HeaderFound);
        Assert.True(snapshot.ContentFound);
    }


    private static void VerifyProjectContactsAndDefaultControls()
    {
        if (SystemParameters.HighContrast) return;

        var contacts = new ProjectContactsView();
        var controls = new StackPanel();
        var checkBox = new CheckBox { IsChecked = true, Content = "Completed" };
        var input = new TextBox { Text = "Selected text" };
        controls.Children.Add(contacts);
        controls.Children.Add(checkBox);
        controls.Children.Add(input);
        controls.Measure(new Size(600, 500));
        controls.Arrange(new Rect(0, 0, 600, 500));

        var choose = Assert.IsType<Button>(FindAutomationElement(contacts, "ProjectChooseContact"));
        Assert.Equal(Color.FromRgb(181, 57, 78), Assert.IsType<SolidColorBrush>(choose.Background).Color);
        var list = Assert.IsType<ListBox>(FindAutomationElement(contacts, "ProjectContactsList"));
        Assert.Same(contacts.FindResource("Personal.Airbnb.BrowserRow"), list.ItemContainerStyle.BasedOn);

        var indicator = Assert.IsType<Border>(checkBox.Template.FindName("Indicator", checkBox));
        Assert.Equal(Color.FromRgb(181, 57, 78), Assert.IsType<SolidColorBrush>(indicator.Background).Color);
        Assert.Equal(Color.FromRgb(232, 183, 192), Assert.IsType<SolidColorBrush>(input.SelectionBrush).Color);
    }

    private static DependencyObject? FindAutomationElement(DependencyObject root, string id)
    {
        if (AutomationProperties.GetAutomationId(root) == id) return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindAutomationElement(VisualTreeHelper.GetChild(root, i), id) is { } element) return element;
        }

        return null;
    }

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
                Accent = (Color)theme["Task.Color.Brand.Accent"],
                Soft = (Color)theme["Task.Color.Brand.Soft"],
                Text = (Color)theme["Task.Color.Text.Primary"],
                Secondary = (Color)theme["Task.Color.Text.Secondary"],
                Surface = (Color)theme["Task.Color.Surface.Base"],
                Subtle = (Color)theme["Task.Color.Surface.Subtle"],
                Selected = (Color)theme["Task.Color.Surface.Selected"],
                Border = (Color)theme["Task.Color.Border.Default"],
                Critical = (Color)theme["Task.Color.Semantic.Critical"],
                Success = (Color)theme["Task.Color.Semantic.Success"],
                Warning = (Color)theme["Task.Color.Semantic.Warning"],
                ControlHeight = (double)theme["Task.Control.Height.Compact"],
                NavigationRow = (double)theme["Task.Navigation.RowHeight"],
                NavigationMenu = (double)theme["Task.Navigation.MenuHeight"],
                ExpandedNavigation = (double)theme["Task.Shell.Navigation.ExpandedWidth"],
                CompactNavigation = (double)theme["Task.Shell.Navigation.CompactWidth"],
                IconOnlyNavigation = (double)theme["Task.Shell.Navigation.IconOnlyWidth"],
                Header = (double)theme["Task.Shell.HeaderHeight"],
                Footer = (double)theme["Task.Shell.FooterHeight"],
                TaskRow = (double)theme["Task.TaskRow.MinHeight"],
                Focus = (double)theme["Task.Stroke.Focus"],
                Radius = (CornerRadius)theme["Task.Radius.Control"],
                TasksIcon = theme["Task.Icon.Tasks"] as Geometry,
                NavigationIcon = theme["Task.Icon.Navigation"] as Geometry,
                ConnectedIcon = theme["Task.Icon.Connected"] as Geometry,
                PriorityIcon = theme["Task.Icon.Priority.Critical"] as Geometry,
                NavigationSurface = theme["Task.Shell.NavigationSurface"] as Style,
                PageTitleStyle = theme["Task.Shell.PageTitle"] as Style,
            };
        });

        Assert.Equal("#FFB5394E", snapshot.Brand.ToString());
        Assert.Equal("#FF963047", snapshot.Strong.ToString());
        Assert.Equal("#FFB5394E", snapshot.Accent.ToString());
        Assert.Equal("#FFF9EDF0", snapshot.Soft.ToString());
        Assert.Equal("#FF222222", snapshot.Text.ToString());
        Assert.Equal("#FF595959", snapshot.Secondary.ToString());
        Assert.Equal("#FFFFFFFF", snapshot.Surface.ToString());
        Assert.Equal("#FFF7F7F7", snapshot.Subtle.ToString());
        Assert.Equal("#FFEEEEEE", snapshot.Selected.ToString());
        Assert.Equal("#FFDDDDDD", snapshot.Border.ToString());
        Assert.Equal("#FFD13438", snapshot.Critical.ToString());
        Assert.Equal("#FF28705D", snapshot.Success.ToString());
        Assert.Equal("#FFF2A900", snapshot.Warning.ToString());
        Assert.Equal(40, snapshot.ControlHeight);
        Assert.Equal(44, snapshot.NavigationRow);
        Assert.Equal(58, snapshot.NavigationMenu);
        Assert.Equal(212, snapshot.ExpandedNavigation);
        Assert.Equal(178, snapshot.CompactNavigation);
        Assert.Equal(64, snapshot.IconOnlyNavigation);
        Assert.Equal(70, snapshot.Header);
        Assert.Equal(46, snapshot.Footer);
        Assert.Equal(60, snapshot.TaskRow);
        Assert.Equal(2, snapshot.Focus);
        Assert.Equal(new CornerRadius(8), snapshot.Radius);
        Assert.NotNull(snapshot.TasksIcon);
        Assert.NotNull(snapshot.NavigationIcon);
        Assert.NotNull(snapshot.ConnectedIcon);
        Assert.NotNull(snapshot.PriorityIcon);
        Assert.NotNull(snapshot.NavigationSurface);
        Assert.NotNull(snapshot.PageTitleStyle);
    }

    [Theory]
    [InlineData(1487, 212)]
    [InlineData(1220, 212)]
    [InlineData(1219, 178)]
    [InlineData(900, 178)]
    [InlineData(899, 64)]
    [InlineData(800, 64)]
    public void NavigationWidth_UsesCanonicalResponsiveBreakpoints(double windowWidth, double expected)
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
        var shell = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.Shell.xaml"));
        var theme = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Theme.xaml"));

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
        Assert.Contains("Task.Button.Primary", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Key=\"N\" Modifiers=\"Alt\" Command=\"{Binding NewTaskCommand}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ConnectionTitle}\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Task.Workspace.NavigationItem", mainWindow, StringComparison.Ordinal);
        Assert.Contains("IsMouseCaptured", navigation, StringComparison.Ordinal);
        Assert.Contains("Task.Brush.Surface.Selected", navigation, StringComparison.Ordinal);
        Assert.Contains("Task.Brush.Brand.Strong", buttons, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsPressed\"", buttons, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", navigation, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", buttons, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", data, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", states, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", shell, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.HighContrast", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", data, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreground\" Value=\"Transparent", states, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TodayXaml_UsesDirection2ThreeZoneTimelineAndStateContract()
    {
        var xaml = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Views", "TodayView.xaml"));

        Assert.Contains("x:Name=\"TodayThreeZoneGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"69\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayPlannerRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayQueueRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayInspectorRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayLoadingState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayStateBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding QueueCount, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("Task.TimelineLaneMarginConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Top=\"{Binding CurrentTimeTop}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.SelectItemCommand, ElementName=TodayRoot}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Personal.Calendar.EntryChrome", xaml, StringComparison.Ordinal);
        Assert.Contains("Несрочные и просроченные", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CalendarXaml_UsesDirection2WeekEditorStateAndScalingContracts()
    {
        var xaml = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Views", "CalendarView.xaml"));
        var app = File.ReadAllText(ProjectFile("src", "Task.Desktop", "App.xaml"));

        Assert.Contains("Height=\"69\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarTimeline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarTimelineItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarSelectionInspector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarEventEditor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarReadOnlyBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CalendarRollbackBanner\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CalendarMonthScroll\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CalendarHorizontalScroll\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DayModeCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding WeekModeCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MonthModeCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Task.CalendarLaneWidthConverter", app, StringComparison.Ordinal);
        Assert.Contains("Task.CalendarLaneLeftConverter", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksAndInboxXaml_ExposeDirection2DensityKeyboardUiaAndScalingContracts()
    {
        var main = File.ReadAllText(ProjectFile("src", "Task.Desktop", "MainWindow.xaml"));
        var inbox = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Views", "InboxView.xaml"));
        var editor = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Views", "TaskCardEditorView.xaml"));
        var dataStyles = File.ReadAllText(ProjectFile("src", "Task.Desktop", "Resources", "Controls.Data.xaml"));

        Assert.Contains("TasksStatusFilter", main, StringComparison.Ordinal);
        Assert.Contains("TasksProjectFilter", main, StringComparison.Ordinal);
        Assert.Contains("ProjectText", main, StringComparison.Ordinal);
        Assert.Contains("AssigneeText", main, StringComparison.Ordinal);
        Assert.Contains("IsFilteredEmpty", main, StringComparison.Ordinal);
        Assert.Contains("Task.ResponsiveGridLengthConverter", main, StringComparison.Ordinal);

        Assert.Contains("AutomationProperties.AutomationId=\"InboxScreen\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxCaptureTextBox\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxList\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxConvertButton\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxInspectorExpander\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxConversionPanel\"", inbox, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"InboxSaveConversionButton\"", inbox, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnInboxListKeyDown\"", inbox, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=700:320", inbox, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", inbox, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", main, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", main, StringComparison.Ordinal);

        Assert.Contains("TaskProjectComboBox", editor, StringComparison.Ordinal);
        Assert.Contains("TaskDescriptionTextBox", editor, StringComparison.Ordinal);
        Assert.Contains("Дополнительные связи и планирование", editor, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", dataStyles, StringComparison.Ordinal);
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
