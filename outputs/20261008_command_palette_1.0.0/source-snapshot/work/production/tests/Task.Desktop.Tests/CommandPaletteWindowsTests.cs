using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Task.Desktop.Converters;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using FakeClient = Task.Desktop.Tests.Work.WorkHubViewModelTests.FakeClient;

namespace Task.Desktop.Tests;

public sealed class CommandPaletteWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task ShellShortcut_KeyboardOnly_FocusTrapAndRestore()
    {
        await OnSta(() =>
        {
            EnsureResources();
            using var hub = new WorkHubViewModel(new FakeClient(), ["Task.Read", "Contact.Read"]);
            using var shell = new MainWindowViewModel(new Uri("https://task.test"), null, workHub: hub);
            var window = new MainWindow(shell);
            try
            {
                window.Show(); window.Activate(); Drain();
                var button = (Button)window.FindName("GlobalSearchButton");
                button.Focus(); Assert.Same(button, Keyboard.FocusedElement);
                Assert.False(window.HandlePaletteShortcut(Key.K, ModifierKeys.Alt));
                Assert.True(window.HandlePaletteShortcut(Key.K, ModifierKeys.Control)); Drain();
                var view = (CommandPaletteView)window.FindName("PaletteSurface");
                var field = (TextBox)view.FindName("QueryField");
                Assert.Same(field, Keyboard.FocusedElement);
                Press(field, Key.Down); Assert.Equal("tasks", shell.Palette.Selected?.Id);
                Press(field, Key.Up); Assert.Equal("inbox", shell.Palette.Selected?.Id);
                shell.Palette.Query = "Задачи";
                window.HandlePaletteShortcut(Key.K, ModifierKeys.Control); Drain();
                Assert.Equal("Задачи", shell.Palette.Query);
                foreach (var direction in new[] { FocusNavigationDirection.Next, FocusNavigationDirection.Previous })
                    for (var i = 0; i < 12; i++)
                    {
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(direction));
                        Assert.True(view.IsKeyboardFocusWithin);
                    }
                field.Focus(); Press(field, Key.Escape); Drain();
                Assert.False(shell.Palette.IsOpen); Assert.Same(button, Keyboard.FocusedElement);
                window.HandlePaletteShortcut(Key.K, ModifierKeys.Control); Drain();
                shell.Palette.Query = "Контакты"; Press(field, Key.Enter); Drain();
                Assert.Equal("contacts", shell.SelectedSection?.Route); Assert.False(shell.Palette.IsOpen);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(760, 640)]
    [InlineData(752, 432)]
    public async System.Threading.Tasks.Task Palette_IsBoundedAndVirtualized_WithAccessibleSelection(double width, double height)
    {
        await OnSta(() =>
        {
            EnsureResources();
            using var palette = new CommandPaletteViewModel(null, () => Enumerable.Range(0, 1000)
                .Select(i => new PaletteCommand(i.ToString(), $"Команда {i}", () => { }, () => true)));
            palette.Open();
            var view = new CommandPaletteView { DataContext = palette };
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            var list = (ListBox)view.FindName("EntriesList");
            var selected = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.Equal(SystemColors.HighlightColor, ((SolidColorBrush)selected.Background).Color);
            Assert.Equal(SystemColors.HighlightTextColor, ((SolidColorBrush)selected.Foreground).Color);
            Assert.Equal(new Thickness(2), selected.BorderThickness);
            Assert.InRange(view.DesiredSize.Height, 1, height); Assert.True(list.ActualHeight < height);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            var realized = Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
            Assert.InRange(realized, 1, 30);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(view));
            Assert.Equal("Команды и результаты поиска", System.Windows.Automation.AutomationProperties.GetName(list));
            Assert.Contains("1 из 1000", palette.SelectionText);
            var evidence = Environment.GetEnvironmentVariable("TASK_PALETTE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                System.IO.Directory.CreateDirectory(evidence);
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(evidence, $"palette-{width}x{height}.png"));
                encoder.Save(stream);
            }
            palette.MoveSelection(-1); Assert.Contains("1000 из 1000", palette.SelectionText);
        });
    }

    private static void Press(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args); Assert.True(args.Handled);
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static void EnsureResources()
    {
        var app = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
        if (app.Resources.Contains("Task.Shell.NavigationSurface")) return;
        app.Resources.MergedDictionaries.Add((ResourceDictionary)global::System.Windows.Application.LoadComponent(new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative)));
        app.Resources["Task.IconKeyToGeometryConverter"] = new IconKeyToGeometryConverter();
        app.Resources["Task.ShellNavigationWidthConverter"] = new ShellNavigationWidthConverter();
        app.Resources["Task.WindowWidthAtLeastConverter"] = new WindowWidthAtLeastConverter();
        app.Resources["Task.ResponsiveGridLengthConverter"] = new ResponsiveGridLengthConverter();
        app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
    }
    private static System.Threading.Tasks.Task OnSta(Action action)
    {
        var done = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
}
