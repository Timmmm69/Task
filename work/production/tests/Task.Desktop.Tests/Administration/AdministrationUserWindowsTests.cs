using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Administration;

public sealed class AdministrationUserWindowsTests
{
    [Fact]
    public async AsyncTask NativeEditorBindingsCommandsReadonlyAndCompactLayout()
    {
        if (Environment.GetEnvironmentVariable("TASK_ADMIN_EDITOR_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(AdministrationUserWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeEditorBindingsCommandsReadonlyAndCompactLayout));
            start.Environment["TASK_ADMIN_EDITOR_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources.Add("Task.BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                var bindingErrors = new StringWriter(); var listener = new TextWriterTraceListener(bindingErrors);
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                try
                {
                    var client = new AdministrationUserEditorTests.Client(); using var vm = AdministrationUserEditorTests.Vm(client);
                    var view = new AdministrationView { DataContext = vm, Margin = new Thickness(16) };
                    window = new Window { Content = view, Width = 1120, Height = 900, ShowActivated = false, ShowInTaskbar = false, Title = "Task · сотрудники · проверка" };
                    window.Show(); Pump();
                    T Find<T>(string id) where T : DependencyObject => Descendants<T>(view).Single(c => AutomationProperties.GetAutomationId(c) == id);
                    void Invoke(string id)
                    {
                        var button = Find<Button>(id); Assert.True(button.IsEnabled); Assert.True(button.IsVisible);
                        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
                    }
                    void Enter(string id, string text)
                    {
                        var box = Find<TextBox>(id); Assert.True(box.IsEnabled); Assert.True(box.IsTabStop);
                        Assert.False(string.IsNullOrWhiteSpace(new TextBoxAutomationPeer(box).GetName()));
                        ((IValueProvider)new TextBoxAutomationPeer(box).GetPattern(PatternInterface.Value)).SetValue(text); Pump();
                    }
                    Assert.Single(Find<ListBox>("AdminUsersList").Items);
                    Invoke("AdminCreateUserButton");
                    Enter("AdminFirstName", "Ирина"); Enter("AdminLastName", "Смирнова"); Enter("AdminLogin", "irina.s");
                    Enter("AdminWorkEmail", "irina@example.test"); Enter("AdminJobTitle", "Инженер");
                    Assert.Equal("Ирина", vm.UserEditor!.FirstName); Assert.Equal("irina@example.test", vm.UserEditor.WorkEmail);
                    Save(view, "create-wide.png");
                    window.Width = 800; window.Height = 600; Pump();
                    Assert.True(Find<Button>("AdminSaveUserButton").IsVisible);
                    Assert.Contains(Descendants<ScrollViewer>(Find<Border>("AdminUserEditor")), s => s.ScrollableHeight > 0);
                    var saveBounds = Find<Button>("AdminSaveUserButton").TransformToAncestor(view).TransformBounds(new Rect(Find<Button>("AdminSaveUserButton").RenderSize));
                    Assert.True(saveBounds.Bottom <= view.ActualHeight + 1); Save(view, "create-compact.png");
                    Invoke("AdminSaveUserButton"); Assert.Null(vm.UserEditor); Assert.Single(client.Creates);
                    Invoke("AdminEditUserButton"); Assert.Equal("Анна", Find<TextBox>("AdminFirstName").Text);
                    Enter("AdminJobTitle", "Новая должность");
                    vm.UpdateConnectivity(false); Pump();
                    Assert.False(Find<TextBox>("AdminJobTitle").IsEnabled); Assert.False(Find<Button>("AdminSaveUserButton").IsEnabled);
                    vm.UpdateConnectivity(true); Pump(); Invoke("AdminCancelUserButton"); Assert.Empty(client.Updates);
                    Invoke("AdminEditUserButton"); Enter("AdminJobTitle", "Сохранённая должность");
                    Invoke("AdminSaveUserButton"); Assert.Single(client.Updates);
                    vm.UpdateCapabilities(["User.Read"]); Pump();
                    Assert.False(Find<Button>("AdminCreateUserButton").IsEnabled); Assert.False(Find<Button>("AdminEditUserButton").IsEnabled);
                    Save(view, "read-only.png");
                    foreach (var (status, buttonId) in new[] { ("pending_activation", "AdminActivateButton"),
                        ("active", "AdminBlockButton"), ("blocked", "AdminUnblockButton"),
                        ("active", "AdminDeactivateButton"), ("deactivated", "AdminReactivateButton") })
                    {
                        var lifecycleClient = new AdministrationUserTransitionTests.Client(status);
                        using var lifecycle = AdministrationUserTransitionTests.Vm(lifecycleClient);
                        view.DataContext = lifecycle; Pump(); Invoke(buttonId);
                        Assert.True(Find<Border>("AdminTransitionConfirmation").IsVisible);
                        Assert.Contains(lifecycleClient.User.DisplayName, lifecycle.TransitionTitle);
                        Invoke("AdminCancelTransitionButton"); Assert.Empty(lifecycleClient.Calls);
                        Invoke(buttonId);
                        if (lifecycle.TransitionRequiresReason)
                        {
                            Invoke("AdminConfirmTransitionButton"); Assert.Empty(lifecycleClient.Calls);
                            Enter("AdminTransitionReason", "Проверка состояния");
                        }
                        window.Width = 1120; window.Height = 900; Pump();
                        foreach (var scroll in Descendants<ScrollViewer>(view)) scroll.ScrollToTop(); Pump();
                        Save(view, buttonId + "-wide.png");
                        window.Width = 800; window.Height = 600; Pump();
                        foreach (var scroll in Descendants<ScrollViewer>(view)) scroll.ScrollToBottom(); Pump();
                        var confirm = Find<Button>("AdminConfirmTransitionButton");
                        var bounds = confirm.TransformToAncestor(view).TransformBounds(new Rect(confirm.RenderSize));
                        Assert.True(bounds.Top >= 0 && bounds.Bottom <= view.ActualHeight + 1);
                        Save(view, buttonId + "-confirmation.png");
                        lifecycle.UpdateConnectivity(false); Pump(); Assert.False(confirm.IsEnabled);
                        lifecycle.UpdateConnectivity(true); Pump(); Invoke("AdminConfirmTransitionButton");
                        Assert.Single(lifecycleClient.Calls); Assert.False(lifecycle.IsTransitionOpen);
                        Assert.Equal(20, lifecycle.SelectedUser!.Version);
                        lifecycle.UpdateCapabilities(["User.Read"]); Pump();
                        Assert.False(Find<Button>("AdminBlockButton").IsVisible);
                        Assert.False(Find<Button>("AdminDeactivateButton").IsVisible);
                    }
                    Assert.DoesNotContain("System.Windows.Data Error", bindingErrors.ToString());
                    await AsyncTask.CompletedTask; done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Save(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_ADMIN_USER_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); view.UpdateLayout();
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view)
        { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(view), view.RenderSize), Stretch = Stretch.Fill }, null, new Rect(view.RenderSize));
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
