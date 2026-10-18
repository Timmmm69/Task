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
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class InteractionsWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeInteractionsCreateEditConflictAndCompactLayout()
    {
        if (Environment.GetEnvironmentVariable("TASK_INTERACTIONS_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeInteractionsCreateEditConflictAndCompactLayout));
            start.Environment["TASK_INTERACTIONS_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(true); }
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
                var errors = new StringWriter(); var listener = new TextWriterTraceListener(errors);
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                try
                {
                    var api = new InteractionsViewModelTests.Client(); var value = api.Record(); api.Values = [value];
                    using var vm = api.Model();
                    var view = new InteractionsView { DataContext = vm, Background = Brushes.White };
                    var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    window = new Window { Content = scroll, Width = 1100, Height = 1000, ShowInTaskbar = false, ShowActivated = false, Title = "Task — взаимодействия CRM" };
                    window.Show(); Pump();
                    await vm.RefreshCommand.ExecuteAsync(); await vm.OpenAsync(value.Id); Pump(); Save(scroll, "interaction-view.png");
                    Assert.Equal(value.Id, vm.Details?.Id);
                    await vm.EditCommand.ExecuteAsync(); Pump();
                    var field = Find<TextBox>(view, "InteractionSubject"); field.Text = "Изменённая тема";
                    field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    window.Width = 800; window.Height = 600; Pump();
                    var save = Find<Button>(view, "SaveInteractionButton"); save.BringIntoView(); Pump();
                    var bounds = save.TransformToAncestor(scroll).TransformBounds(new Rect(save.RenderSize));
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= scroll.ActualHeight + 1); Save(scroll, "interaction-edit-compact.png");
                    await vm.SaveCommand.ExecuteAsync(); Pump(); Assert.Equal("Изменённая тема", vm.Details?.Subject); Assert.False(vm.IsEditing);
                    await vm.EditCommand.ExecuteAsync(); api.Save = _ => new DesktopWorkResult<DesktopInteraction>.Conflict();
                    await vm.SaveCommand.ExecuteAsync(); Pump(); Assert.False(save.IsEnabled); Save(scroll, "interaction-conflict.png");
                    await vm.CancelCommand.ExecuteAsync(); await vm.NewCommand.ExecuteAsync(); vm.Subject = "Новое взаимодействие"; api.Save = null;
                    await vm.SaveCommand.ExecuteAsync(); Pump(); Assert.Equal("Новое взаимодействие", vm.Details?.Subject);
                    vm.InvalidateScope(); Pump(); Assert.False(Find<Button>(view, "NewInteractionButton").IsVisible);
                    Assert.DoesNotContain("System.Windows.Data Error", errors.ToString());
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task.WaitAsync(TimeSpan.FromSeconds(50));
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants<T>(root).Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Save(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_INTERACTIONS_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); view.UpdateLayout();
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view)
            { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(view), view.RenderSize), Stretch = Stretch.Fill }, null, new Rect(view.RenderSize));
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
