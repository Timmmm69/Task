using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;

namespace Task.Desktop.Tests.Work;

[Collection("Employee picker native WPF")]
public sealed class ObjectLinksIntegrationWindowsTests
{
    [Fact]
    public async AsyncTask EmbeddedPickerKeyboardCreateAndReadOnlyCatalog()
    {
        if (Environment.GetEnvironmentVariable("TASK_LINK_INTEGRATION_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ObjectLinksIntegrationWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ObjectLinksIntegrationWindowsTests).FullName + "." + nameof(EmbeddedPickerKeyboardCreateAndReadOnlyCatalog));
            start.Environment["TASK_LINK_INTEGRATION_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }
        var completion = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var client = new ObjectLinksIntegrationTests.Client(); using var vm = ObjectLinksIntegrationTests.Model(client, delete: () => false);
                    var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
                    var view = new ObjectLinksView { DataContext = vm };
                    window = new Window { Content = view, Width = 520, Height = 730, Title = "Task · Корпоративные связи" };
                    window.Show(); window.Activate(); Pump();
                    var pickerView = Assert.Single(Descendants<ObjectLinkTargetPickerView>(view));
                    var search = (TextBox)pickerView.FindName("SearchBox"); var list = (ListBox)pickerView.FindName("ResultsList");
                    Assert.True(pickerView.IsVisible); Assert.False(Descendants<TextBox>(view).Single(box => box != search).IsVisible);
                    var types = Assert.Single(Descendants<ComboBox>(view)); Assert.Equal(4, types.Items.Count);
                    vm.SetSource(Guid.NewGuid(), 3, "project"); Pump(); Assert.Equal("project_file", vm.LinkType); Assert.Equal("project_file", types.SelectedValue);
                    vm.SetSource(source, 7, "task"); Pump(); Assert.Equal("task_file", vm.LinkType);
                    search.Text = "План"; await vm.TargetPicker!.CurrentOperation; Pump(); Assert.Single(list.Items);
                    Assert.True(search.Focus()); SendKey(pickerView, Key.Down); Pump(); Assert.True(list.IsKeyboardFocusWithin);
                    SendKey(pickerView, Key.Enter); Pump(); Assert.Equal(client.Hit.ObjectId, vm.TargetPicker.SelectedObjectId);
                    Assert.Contains(Descendants<TextBlock>(pickerView), block => block.Text == client.Hit.Title || block.Text.Contains("Выбран: " + client.Hit.Title));
                    Assert.Contains(Descendants<TextBlock>(pickerView), block => block.Text == client.Hit.TypeLabel);
                    SaveFrame(view, "corporate-links-selected.png");
                    var add = Descendants<Button>(view).Single(button => Equals(button.Content, "Связать")); Assert.True(add.IsEnabled);
                    var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); client.Write = _ => pending.Task;
                    ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
                    Assert.Empty(vm.Items); Assert.True(vm.IsBusy); Assert.False(add.IsEnabled);
                    var row = new Task.Desktop.Work.DesktopObjectLink(Guid.NewGuid(), source, client.Hit.ObjectId, "task_file", client.Hit.Title);
                    pending.SetResult(new LinkResult.Succeeded(new([row], 8)));
                    for (var i = 0; i < 100 && vm.IsBusy; i++) await AsyncTask.Delay(10);
                    Pump(); Assert.Single(Descendants<DataGrid>(view).Single().Items); Assert.False(vm.RemoveCommand.CanExecute(null));
                    SaveFrame(view, "corporate-links-confirmed.png");
                    window.Width = 360; Pump();
                    Assert.All(Descendants<Button>(view).Where(button => button.IsVisible), button => Assert.True(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Right <= view.ActualWidth + 1));
                    SaveFrame(view, "corporate-links-narrow.png");
                    vm.SetSource(Guid.NewGuid(), 1, "catalog_item"); Pump();
                    Assert.True(types.IsVisible); Assert.True(add.IsVisible); Assert.True(pickerView.IsVisible);
                    Assert.Equal("related", vm.LinkType); Assert.False(add.IsEnabled);
                    Assert.True(Descendants<DataGrid>(view).Single().IsVisible);
                    SaveFrame(view, "corporate-links-catalog-read-only.png"); completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            }); app.Run();
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(35));
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void SendKey(UIElement view, Key key) => view.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(view), 0, key)
    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void SaveFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_LINK_INTEGRATION_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
