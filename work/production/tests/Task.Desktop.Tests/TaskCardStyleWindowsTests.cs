using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using AsyncTask = System.Threading.Tasks.Task;
using ShapePath = System.Windows.Shapes.Path;

namespace Task.Desktop.Tests;

public sealed class TaskCardStyleWindowsTests
{
    [Fact]
    public async AsyncTask MaterializedTaskCardStylesKeepContrastAndInteractionAtScaledLayouts()
    {
        const string childFlag = "TASK_CARD_STYLE_PROBE";
        if (Environment.GetEnvironmentVariable(childFlag) != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(TaskCardStyleWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(TaskCardStyleWindowsTests).FullName + "." + nameof(MaterializedTaskCardStylesKeepContrastAndInteractionAtScaledLayouts));
            start.Environment[childFlag] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }

        var done = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.BeginInvoke(() =>
            {
                Window? window = null;
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
                    app.Resources[SystemColors.WindowBrushKey] = Brushes.Black;
                    app.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
                    app.Resources[SystemColors.HighlightBrushKey] = Brushes.Yellow;
                    app.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
                    app.Resources["Task.Test.HighContrast"] = true;
                    // Only the HC condition source is changed; every template/setter is production XAML.
                    var source = File.ReadAllText(FindStyleSource()).Replace(
                        "{Binding Source={x:Static SystemParameters.HighContrast}}",
                        "{Binding Source={StaticResource Task.Test.HighContrast}}", StringComparison.Ordinal);
                    var styles = (ResourceDictionary)XamlReader.Parse(source, new ParserContext { BaseUri = new Uri(FindStyleSource()) });
                    foreach (var key in styles.Keys) app.Resources[key] = styles[key];
                    var panel = new StackPanel { Width = 320 };
                    var text = new TextBlock { Text = "Участники задачи" };
                    var group = new Border { Style = (Style)app.FindResource("Task.Card.Group"), Child = text };
                    var check = new CheckBox { Style = (Style)app.FindResource("Task.Card.Check"), Content = "Исполнитель", IsThreeState = true };
                    var combo = new ComboBox { Style = (Style)app.FindResource("Task.Card.Combo") };
                    combo.Items.Add("Первый проект"); combo.Items.Add("Второй проект"); combo.SelectedIndex = 0;
                    var section = new Expander { Style = (Style)app.FindResource("Task.Card.Section"), Header = "Планирование", Content = new TextBlock { Text = "Детали" } };
                    panel.Children.Add(group); panel.Children.Add(check); panel.Children.Add(combo); panel.Children.Add(section);
                    window = new Window { Content = panel, Width = 750, Height = 850, ShowInTaskbar = false };
                    window.Show(); window.Activate(); Pump();
                    foreach (var scale in new[] { 1d, 1.5d, 2d })
                    {
                        panel.LayoutTransform = new ScaleTransform(scale, scale); Pump();
                        ColorIs(group.Background, Colors.Black); ColorIs(text.Foreground, Colors.White);
                        Assert.True(check.Focus());
                        foreach (var state in new bool?[] { false, true, null })
                        {
                            check.IsChecked = state; Pump();
                            var box = Part<Border>(check, "Box");
                            ColorIs(box.Background, state == false ? Colors.Black : Colors.Yellow);
                            ColorIs(check.Foreground, Colors.White);
                            if (state != false)
                            {
                                var glyph = Part<ShapePath>(check, state == true ? "Tick" : "Mixed");
                                Assert.Equal(Visibility.Visible, glyph.Visibility); ColorIs(glyph.Stroke, Colors.Black);
                            }
                        }
                        check.IsChecked = false;
                        ((IToggleProvider)new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)!).Toggle();
                        Assert.True(check.IsChecked);
                        Assert.True(check.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))); Pump();
                        Assert.True(combo.IsKeyboardFocusWithin);
                        combo.IsDropDownOpen = true; Pump();
                        var popup = Part<Popup>(combo, "PART_Popup");
                        Assert.True(popup.IsOpen);
                        ColorIs(Assert.IsType<Border>(popup.Child).Background, Colors.Black);
                        combo.SelectedIndex = 1; Pump();
                        var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(1));
                        ColorIs(Part<Border>(item, "Item").Background, Colors.Yellow);
                        ColorIs(item.Foreground, Colors.Black);
                        combo.IsDropDownOpen = false; Pump();
                        var header = Part<ToggleButton>(section, "HeaderToggle");
                        Assert.True(header.Focus());
                        var expanded = section.IsExpanded;
                        ((IToggleProvider)new ToggleButtonAutomationPeer(header).GetPattern(PatternInterface.Toggle)!).Toggle(); Pump();
                        Assert.Equal(!expanded, section.IsExpanded);
                        ColorIs(Part<Border>(header, "HeaderSurface").Background, Colors.Black);
                        ColorIs(header.Foreground, Colors.White);
                        Assert.True(group.ActualWidth > 0); Assert.True(section.ActualHeight > 0);
                    }
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(50));
    }

    private static T Part<T>(Control control, string name) where T : DependencyObject
    {
        control.ApplyTemplate();
        return Assert.IsType<T>(control.Template.FindName(name, control));
    }

    private static void ColorIs(Brush brush, Color expected) => Assert.Equal(expected, Assert.IsType<SolidColorBrush>(brush).Color);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static string FindStyleSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "work", "production", "src", "Task.Desktop", "Resources", "Controls.TaskCard.xaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Production task-card XAML was not found above the test output directory.");
    }
}
