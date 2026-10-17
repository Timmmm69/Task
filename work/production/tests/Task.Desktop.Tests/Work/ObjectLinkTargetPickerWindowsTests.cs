using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;
using PageResult = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>;

namespace Task.Desktop.Tests.Work;

[Collection("Employee picker native WPF")]
public sealed class ObjectLinkTargetPickerWindowsTests
{
    [Fact]
    public async AsyncTask NativeKeyboardBindingsNamesAndNarrowLayout()
    {
        if (Environment.GetEnvironmentVariable("TASK_LINK_TARGET_PICKER_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(ObjectLinkTargetPickerWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ObjectLinkTargetPickerWindowsTests).FullName + "." + nameof(NativeKeyboardBindingsNamesAndNarrowLayout));
            start.Environment["TASK_LINK_TARGET_PICKER_PROBE"] = "1";
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
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    var first = ObjectLinkTargetPickerTests.Hit(); var second = ObjectLinkTargetPickerTests.Hit();
                    var client = new ObjectLinkTargetPickerTests.SearchClient
                    { Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded([first, second])) };
                    using var picker = ObjectLinkTargetPickerTests.Picker(client);
                    var view = new ObjectLinkTargetPickerView { DataContext = picker, Margin = new Thickness(16) };
                    window = new Window { Content = view, Width = 520, Height = 700, Title = "Task · Object Link Target Picker" };
                    window.Show(); window.Activate(); Pump();
                    var search = (TextBox)view.FindName("SearchBox"); var list = (ListBox)view.FindName("ResultsList");
                    search.Text = "План"; await picker.CurrentOperation; Pump();
                    Assert.Equal("План", picker.SearchText); Assert.Equal(2, list.Items.Count);
                    Assert.Equal("Название связанного объекта", new TextBoxAutomationPeer(search).GetName());
                    Assert.Equal("Найденные объекты", new ListBoxAutomationPeer(list).GetName());
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(row)!;
                    Assert.Equal(first.Title + ", " + first.TypeLabel, peer.GetName());
                    Assert.Contains(first.ObjectId.ToString(), peer.GetHelpText());
                    Assert.True(search.Focus()); Pump(); SendKey(view, Key.Down); Pump();
                    Assert.True(list.IsKeyboardFocusWithin); Assert.Same(first, picker.Candidate);
                    SendKey(view, Key.Enter); Assert.Equal(first.ObjectId, picker.SelectedObjectId);
                    list.SelectedIndex = 1; Pump(); SendKey(view, Key.Enter); Assert.Equal(second.ObjectId, picker.SelectedObjectId);
                    Assert.True(search.Focus()); Assert.True(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))); Pump(); Assert.False(search.IsKeyboardFocusWithin);
                    var buttons = Descendants<Button>(view).ToArray(); Assert.Equal(5, buttons.Length);
                    Assert.All(buttons, button => { Assert.True(button.Focusable); Assert.True(button.IsTabStop); Assert.NotEmpty(new ButtonAutomationPeer(button).GetName()); });
                    SaveFrame(view, "target-picker-selected.png");
                    window.Width = 360; Pump();
                    Assert.All(buttons, button => Assert.True(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Right <= view.ActualWidth + 1));
                    var text = Descendants<TextBlock>(list).Select(block => block.Text).ToArray();
                    Assert.Contains(first.ObjectId.ToString(), string.Join(' ', text)); Assert.Contains(second.ObjectId.ToString(), string.Join(' ', text));
                    SaveFrame(view, "target-picker-narrow.png");
                    SendKey(view, Key.Escape); Pump(); Assert.True(search.IsKeyboardFocusWithin); Assert.Null(picker.SelectedObjectId); Assert.Empty(list.Items);
                    picker.UpdateAccess([]); Pump(); Assert.Empty(list.Items); Assert.False(search.IsEnabled);
                    SaveFrame(view, "target-picker-forbidden.png");
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(35));
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void SendKey(UIElement view, Key key) => view.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(view), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void SaveFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_LINK_TARGET_PICKER_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
