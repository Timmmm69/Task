using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Converters;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;
using FakeClient = Task.Desktop.Tests.Work.WorkHubViewModelTests.FakeClient;

namespace Task.Desktop.Tests;

public sealed class SettingsWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeSettingsChoicesBindingsKeyboardValidationAndPagination()
    {
        if (Environment.GetEnvironmentVariable("TASK_FOUR_SETTINGS_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeSettingsChoicesBindingsKeyboardValidationAndPagination));
            start.Environment["TASK_FOUR_SETTINGS_PROBE"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); Assert.True(process.ExitCode == 0, await stdout + "\n" + await stderr); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var app = new global::System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                    app.Resources["Task.IconKeyToGeometryConverter"] = new IconKeyToGeometryConverter();
                    app.Resources["Task.WindowWidthAtLeastConverter"] = new WindowWidthAtLeastConverter();
                    app.Resources["Task.ResponsiveGridLengthConverter"] = new ResponsiveGridLengthConverter();
                    var calls = 0;
                    var client = new FakeClient
                    {
                        SearchPageHandler = (_, _, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>>(
                        new DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded([
                            new(Guid.NewGuid(), "task", "Результат " + ++calls, null, 1, DateTimeOffset.UtcNow)])
                        { NextCursor = calls == 1 ? "next" : null })
                    };
                    using var hub = new WorkHubViewModel(client, ["Settings.ReadOwn", "Settings.UpdateOwn", "Search.Use"]);
                    hub.Activate(WorkHubArea.Settings);
                    var view = new WorkHubView { DataContext = hub, Background = Brushes.White };
                    window = new Window { Content = view, Width = 1280, Height = 900, Title = "Task — проверка настроек" };
                    window.Show(); window.Activate(); Drain();
                    var language = Find<ComboBox>(view, "SettingsLanguage");
                    var weekday = Find<ComboBox>(view, "SettingsFirstDay");
                    var startField = Find<TextBox>(view, "SettingsWorkdayStart");
                    Assert.False(language.IsEditable); Assert.Single(language.Items);
                    Assert.Equal("ru-RU", language.SelectedValue);
                    Assert.Equal(7, weekday.Items.Count); Assert.Equal(1, weekday.SelectedValue);
                    Assert.Equal("09:00", startField.Text);
                    Assert.True(weekday.Focusable && KeyboardNavigation.GetIsTabStop(weekday));
                    Assert.Equal("Первый день недели", AutomationProperties.GetName(weekday));
                    language.Focus(); Assert.True(language.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    Assert.Same(weekday, Keyboard.FocusedElement);
                    weekday.SelectedValue = 7; Drain(); Assert.Equal(7, hub.FirstDayOfWeek);
                    startField.Text = "08:30"; Drain();
                    await hub.SaveUserSettingsCommand.ExecuteAsync();
                    Assert.Equal(7, client.SavedUserSettings?.FirstDayOfWeek);
                    Assert.Equal("08:30:00", client.SavedUserSettings?.WorkdayStart);
                    Assert.Equal(WorkHubFeedbackKind.Success, hub.FeedbackKind);
                    Save(view, "settings-profile.png");
                    startField.Text = "25:00"; Drain();
                    Assert.Contains("Начало рабочего дня", Find<TextBlock>(view, "SettingsProfileError").Text);
                    await hub.SaveUserSettingsCommand.ExecuteAsync();
                    Assert.Equal(WorkHubFeedbackKind.Warning, hub.FeedbackKind);
                    Save(view, "settings-invalid-time.png");
                    hub.SettingsSection = "Уведомления"; Drain();
                    var quiet = Find<TextBox>(view, "SettingsQuietStart");
                    Assert.True(quiet.Focusable); Assert.NotEmpty(AutomationProperties.GetName(quiet));
                    hub.Activate(WorkHubArea.Search); hub.SearchQuery = "результат";
                    await hub.SearchCommand.ExecuteAsync(); Drain();
                    var more = Find<Button>(view, "WorkHubLoadMoreButton");
                    Assert.True(more.IsVisible, "Load-more button must be visible.");
                    Assert.True(more.IsEnabled, "Load-more button must be enabled after the first page.");
                    Assert.True(more.Focusable);
                    await hub.LoadMoreCommand.ExecuteAsync(); Drain();
                    Assert.Equal(2, hub.SearchResults.Count); Assert.False(more.IsVisible);
                    Save(view, "search-two-pages.png");
                    hub.UpdateCapabilities(["FileCatalog.Read", "Contact.Read", "Notification.ReadOwn", "History.Read", "Trash.Read"]);
                    foreach (var area in new[] { WorkHubArea.Catalog, WorkHubArea.Contacts, WorkHubArea.Notifications, WorkHubArea.Archive, WorkHubArea.Trash })
                    {
                        hub.Activate(area); Drain();
                        Assert.True(hub.CanReadCurrentArea);
                        foreach (var width in new[] { 1280d, 780d })
                        {
                            window.Width = width; Drain();
                            Assert.True(view.ActualWidth > 0);
                            if (area == WorkHubArea.Catalog)
                            {
                                var catalog = Find<Border>(view, "FilesCatalogRegion");
                                var inspector = Find<Grid>(view, "FilesInspectorRegion");
                                var left = catalog.TransformToAncestor(view).TransformBounds(new Rect(catalog.RenderSize));
                                var right = inspector.TransformToAncestor(view).TransformBounds(new Rect(inspector.RenderSize));
                                Assert.True(right.Right <= view.ActualWidth + 1);
                                if (width < 860) Assert.True(right.Top >= left.Bottom - 1);
                                else Assert.True(right.Left >= left.Right - 1);
                            }
                            Save(view, $"{area}-{width}.png");
                        }
                    }
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { window?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task;
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T found && AutomationProperties.GetAutomationId(found) == id) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (TryFind<T>(VisualTreeHelper.GetChild(root, i), id) is { } item) return item;
        throw new InvalidOperationException("Control not found: " + id);
    }
    private static T? TryFind<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T found && AutomationProperties.GetAutomationId(found) == id) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (TryFind<T>(VisualTreeHelper.GetChild(root, i), id) is { } item) return item;
        return null;
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static void Save(FrameworkElement view, string name)
    {
        Drain();
        var directory = Environment.GetEnvironmentVariable("TASK_FOUR_UI_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
