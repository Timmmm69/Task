using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Converters;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    [Fact]
    public async System.Threading.Tasks.Task Inbox_NativeTitleOnlyConversionHasEnabledSaveAndClosesSource()
    {
        if (Environment.GetEnvironmentVariable("TASK_INBOX_CONVERSION_UI_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(Inbox_NativeTitleOnlyConversionHasEnabledSaveAndClosesSource));
            start.Environment["TASK_INBOX_CONVERSION_UI_PROBE"] = "1";
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
                    foreach (var name in new[] { "IconKeyToGeometryConverter", "WindowWidthAtLeastConverter", "ResponsiveGridLengthConverter" })
                        app.Resources["Task." + name] = Activator.CreateInstance(typeof(InboxView).Assembly.GetType("Task.Desktop.Converters." + name)!);
                    var converted = false;
                    await using var fixture = await Fixture.CreateAsync((request, _) =>
                    {
                        var path = request.RequestUri!.AbsolutePath;
                        if (path.EndsWith("/options")) return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{}"));
                        if (path.EndsWith("/inbox-items")) return System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[" + (converted ? "" : InboxJson()) + "]}"));
                        if (request.Method == HttpMethod.Patch) return System.Threading.Tasks.Task.FromResult(InboxWrite(InboxJson().Replace("\"version\":1", "\"version\":2"), 2, true));
                        converted = true; return System.Threading.Tasks.Task.FromResult(InboxWrite(ConvertedTask().ToJsonString()));
                    });
                    using var model = new InboxViewModel(fixture.Inbox, ["Inbox.ReadOwn", "Inbox.ManageOwn", "Task.Read"]);
                    await model.ActivateAsync(); await model.ConvertCommand.ExecuteAsync(model.SelectedItem);
                    var view = new InboxView { DataContext = model, Background = Brushes.White, MotionEnabled = () => false };
                    window = new Window { Content = view, Width = 1280, Height = 900, Left = -20000, ShowInTaskbar = false,
                        ShowActivated = false, Title = "Task — преобразование без срока" };
                    window.Show(); DrainInbox();
                    var save = InboxControl<Button>(view, "InboxSaveConversionButton");
                    var deadline = InboxControl<TextBox>(view, "InboxConversionDeadline");
                    Assert.Equal("", deadline.Text); Assert.True(save.IsEnabled); Assert.True(model.CanSaveConversion);
                    Assert.True(save.Focusable); SaveInboxFrame(view, "inbox-title-only.png");
                    await model.SaveConversionCommand.ExecuteAsync(); DrainInbox();
                    Assert.Null(model.Conversion); Assert.True(model.ShowInboxZero);
                    Assert.Single(fixture.Requests, request => request.Uri.AbsolutePath.EndsWith("/convert-to-task"));
                    Assert.DoesNotContain(fixture.Requests, request => request.Uri.AbsolutePath.Contains("/tasks/") && request.Method == HttpMethod.Patch);
                    SaveInboxFrame(view, "inbox-converted.png"); done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { window?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task;
    }
    private static T InboxControl<T>(DependencyObject root, string id) where T : DependencyObject
    {
        T? Find(DependencyObject node)
        {
            if (node is T typed && AutomationProperties.GetAutomationId(node) == id) return typed;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
        return Find(root) ?? throw new InvalidOperationException("Missing inbox control: " + id);
    }
    private static void DrainInbox() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static void SaveInboxFrame(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TASK_INBOX_CONVERSION_UI_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
