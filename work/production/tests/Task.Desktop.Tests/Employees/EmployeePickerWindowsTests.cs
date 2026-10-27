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
using Task.Desktop.Administration;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Employees;

[CollectionDefinition("Employee picker native WPF", DisableParallelization = true)]
public sealed class EmployeePickerWindowsCollection;

[Collection("Employee picker native WPF")]
public sealed class EmployeePickerWindowsTests
{
    [Fact]
    public async AsyncTask NativeControlKeyboardFocusBindingsAndAccessibleNames()
    {
        // Keep WPF Application/Dispatcher lifetime isolated from other native-window tests.
        if (Environment.GetEnvironmentVariable("TASK_EMPLOYEE_PICKER_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(EmployeePickerWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(EmployeePickerWindowsTests).FullName + "." + nameof(NativeControlKeyboardFocusBindingsAndAccessibleNames));
            start.Environment["TASK_EMPLOYEE_PICKER_PROBE"] = "1";
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
                    var first = EmployeePickerTests.User(); var second = EmployeePickerTests.User("ivan2");
                    var directory = new EmployeePickerTests.Directory
                    {
                        Search = (_, _, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(
                            new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([first, second])),
                    };
                    using var picker = new EmployeePickerViewModel(directory, ["User.Read"], debounce: TimeSpan.Zero);
                    var view = new EmployeePickerView { DataContext = picker, Margin = new Thickness(16) };
                    window = new Window { Content = view, Width = 480, Height = 620, Title = "Task · Employee Picker test" };
                    window.Show(); window.Activate(); await picker.CurrentOperation; Pump();
                    var search = (TextBox)view.FindName("SearchBox"); var list = (ListBox)view.FindName("ResultsList");
                    search.Text = "Иван"; await picker.CurrentOperation; Pump();
                    Assert.Equal("Иван", picker.SearchText); Assert.Equal(2, list.Items.Count);
                    Assert.Equal("Имя сотрудника для поиска", new TextBoxAutomationPeer(search).GetName());
                    Assert.Equal("Найденные сотрудники", new ListBoxAutomationPeer(list).GetName());
                    Assert.True(search.Focus()); Pump();
                    SendKey(view, Key.Down); Pump(); Assert.True(list.IsKeyboardFocusWithin); Assert.Same(first, picker.Candidate);
                    SendKey(view, Key.Enter); Assert.Equal(first.Id, picker.SelectedUserAccountId);
                    list.SelectedIndex = 1; Pump(); SendKey(view, Key.Enter); Assert.Equal(second.Id, picker.SelectedUserAccountId);
                    SendKey(view, Key.Escape); Pump(); Assert.True(search.IsKeyboardFocusWithin); Assert.Null(picker.Candidate);
                    // Tab moves through the native WPF focus traversal path without trapping the host form.
                    Assert.True(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))); Pump();
                    Assert.False(search.IsKeyboardFocusWithin);
                    var buttons = Descendants<Button>(view).ToArray(); Assert.Equal(6, buttons.Length);
                    Assert.All(buttons, button => { Assert.True(button.Focusable); Assert.True(button.IsTabStop); Assert.NotEmpty(new ButtonAutomationPeer(button).GetName()); });
                    await picker.ClearCommand.ExecuteAsync(); Assert.Null(picker.SelectedUserAccountId); Pump();
                    await picker.SearchAsync(); picker.Candidate = second; Assert.True(picker.ConfirmSelection()); Pump();
                    SaveFrame(view, "employee-picker-ready.png");
                    window.Width = 360; Pump();
                    foreach (var button in buttons)
                    {
                        var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                        Assert.True(bounds.Right <= view.ActualWidth + 1);
                    }
                    SaveFrame(view, "employee-picker-narrow.png");
                    picker.UpdateAccess([]); Pump(); Assert.Empty(list.Items); Assert.False(search.IsEnabled);
                    Assert.Contains("User.Read", picker.StatusText); SaveFrame(view, "employee-picker-forbidden.png");
                    picker.UpdateAccess(["User.Read"], online: false); Pump(); Assert.False(search.IsEnabled);
                    await picker.SearchAsync(); Assert.Equal(3, directory.SearchCalls);
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

    private static void SendKey(UIElement view, Key key) => view.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(view), 0, key)
    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void SaveFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_EMPLOYEE_PICKER_EVIDENCE") is not { Length: > 0 } directory) return;
        System.IO.Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
