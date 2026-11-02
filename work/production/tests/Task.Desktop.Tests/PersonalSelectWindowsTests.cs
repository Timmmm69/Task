using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Infrastructure;
using Task.Desktop.Modes;
using Task.Desktop.Personal;

namespace Task.Desktop.Tests;

public sealed class PersonalSelectWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task PersonalDropdownsPreserveBindingsSearchKeyboardAndSharedViewIsolation()
    {
        const string flag = "TASK_PERSONAL_SELECT_PROBE";
        if (Environment.GetEnvironmentVariable(flag) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(PersonalSelectWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(PersonalDropdownsPreserveBindingsSearchKeyboardAndSharedViewIsolation));
            start.Environment[flag] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(100)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(true); }
            return;
        }
        var done = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new App(); app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? host = null; PersonalWindow? personal = null;
                var data = Path.Combine(Path.GetTempPath(), "task-personal-select-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var source = new SelectionModel();
                    var search = new ComboBox { DisplayMemberPath = "Name", MinWidth = 260, DataContext = source,
                        Style = (Style)app.FindResource("Task.Input.Combo") };
                    search.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SelectionModel.Projects)));
                    search.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SelectionModel.Selected)) { Mode = BindingMode.TwoWay });
                    var other = new ComboBox { ItemsSource = source.Projects, DisplayMemberPath = "Name" };
                    var compact = new ComboBox { ItemsSource = new[] { "Первый", "Второй" }, SelectedIndex = 0, MinWidth = 260 };
                    var authored = new ComboBox { MinWidth = 260 };
                    authored.Items.Add(new ComboBoxItem { Content = "Напомнить", Tag = "notify" });
                    authored.Items.Add(new ComboBoxItem { Content = "Не напоминать", Tag = "silent" });
                    PersonalSelect.SetVariant(authored, PersonalSelectVariant.Search);
                    authored.SelectedValuePath = "Tag"; authored.SelectedValue = "notify";
                    var detailed = new ComboBox { ItemsSource = new[] { "Низкий", "Обычный", "Высокий", "Критический" }, SelectedIndex = 1, MinWidth = 260 };
                    PersonalSelect.SetVariant(detailed, PersonalSelectVariant.Detailed);
                    var panel = new StackPanel { Margin = new Thickness(24) };
                    panel.Children.Add(search); panel.Children.Add(compact); panel.Children.Add(detailed); panel.Children.Add(other); panel.Children.Add(authored);
                    // Scope on a parent also replaces an explicitly styled, shared ComboBox template.
                    PersonalSelect.SetScope(panel, true);
                    host = new Window { Content = panel, Width = 650, Height = 650, ShowInTaskbar = false };
                    host.Show(); host.Activate(); await Drain(host);
                    var template = app.FindResource("Personal.Select.Template");
                    Assert.Same(template, search.Template); Assert.Equal(PersonalSelectVariant.Search, PersonalSelect.GetVariant(search));
                    Assert.Equal(PersonalSelectVariant.Compact, PersonalSelect.GetVariant(compact));
                    compact.IsDropDownOpen = true; await Drain(host); Assert.True(Part<Popup>(compact, "PART_Popup").IsOpen);
                    compact.SelectedIndex = 1; await Drain(host);
                    var selected = (ComboBoxItem)compact.ItemContainerGenerator.ContainerFromIndex(1);
                    Assert.Equal(Visibility.Visible, Part<System.Windows.Shapes.Path>(selected, "SelectedMark").Visibility);
                    compact.IsDropDownOpen = false;
                    detailed.IsDropDownOpen = true; await Drain(host);
                    var detail = (ComboBoxItem)detailed.ItemContainerGenerator.ContainerFromIndex(1);
                    Assert.Equal("Стандартный приоритет задачи", PersonalSelect.GetDescription(detail));
                    detailed.IsDropDownOpen = false;
                    authored.IsDropDownOpen = true; await Drain(host);
                    var authoredItem = (ComboBoxItem)authored.Items[0];
                    Assert.Equal(Visibility.Visible, Part<System.Windows.Shapes.Path>(authoredItem, "SelectedMark").Visibility);
                    authored.SelectedValue = "silent"; Assert.Equal("silent", authored.SelectedValue);
                    authored.IsDropDownOpen = false;
                    authoredItem.IsEnabled = false;
                    authored.IsDropDownOpen = true; await Drain(host);
                    var authoredSearch = Part<TextBox>(authored, "PART_Search");
                    authoredSearch.Text = "Напомнить"; await Drain(host);
                    Assert.Equal(Visibility.Collapsed, Part<TextBlock>(authored, "PART_Empty").Visibility);
                    PressKey(authoredSearch, Key.Enter); await Drain(host);
                    Assert.True(authored.IsDropDownOpen); Assert.Equal("silent", authored.SelectedValue);
                    authored.IsDropDownOpen = false;

                    var originalView = CollectionViewSource.GetDefaultView(source.Projects);
                    search.IsDropDownOpen = true; await Drain(host);
                    var input = Part<TextBox>(search, "PART_Search"); Assert.True(input.IsVisible);
                    input.Text = "бета"; await Drain(host);
                    Assert.Same(source.Projects, search.ItemsSource); Assert.Null(originalView.Filter);
                    Assert.Equal(3, other.Items.Count); Assert.Same(source.Projects[0], source.Selected);
                    Assert.Equal(Visibility.Collapsed, ((ComboBoxItem)search.ItemContainerGenerator.ContainerFromIndex(0)).Visibility);
                    Assert.Equal(Visibility.Visible, ((ComboBoxItem)search.ItemContainerGenerator.ContainerFromIndex(1)).Visibility);
                    PressKey(input, Key.Down); await Drain(host);
                    Assert.Same(source.Projects[0], source.Selected); // Merely browsing search results never commits.
                    PressKey((UIElement)Keyboard.FocusedElement, Key.Enter); await Drain(host);
                    Assert.Same(source.Projects[1], search.SelectedItem); Assert.Same(source.Projects[1], source.Selected);
                    Assert.False(search.IsDropDownOpen); Assert.NotNull(BindingOperations.GetBinding(search, Selector.SelectedItemProperty));
                    search.IsDropDownOpen = true; await Drain(host); input = Part<TextBox>(search, "PART_Search");
                    Assert.Equal("", input.Text); input.Text = "нет совпадений"; await Drain(host);
                    Assert.Equal(Visibility.Visible, Part<TextBlock>(search, "PART_Empty").Visibility);
                    PressKey(input, Key.Enter); Assert.True(search.IsDropDownOpen); Assert.Same(source.Projects[1], source.Selected);
                    PressKey(input, Key.Escape); await Drain(host); Assert.False(search.IsDropDownOpen); Assert.True(search.IsKeyboardFocusWithin);
                    source.Projects.Add(new("Новый проект"));
                    search.IsDropDownOpen = true; await Drain(host); input = Part<TextBox>(search, "PART_Search");
                    input.Text = "НОВЫЙ"; await Drain(host); PressKey(input, Key.Enter); await Drain(host);
                    Assert.Equal("Новый проект", ((ProjectOption)search.SelectedItem).Name);
                    for (var scale = 1d; scale <= 2; scale += .5)
                    {
                        panel.LayoutTransform = new ScaleTransform(scale, scale);
                        search.IsDropDownOpen = true; await Drain(host);
                        Assert.True(Part<Popup>(search, "PART_Popup").IsOpen);
                        Assert.True(Part<Border>(search, "PopupSurface").ActualWidth <= search.ActualWidth + .5);
                        Assert.True(Part<Border>(search, "PopupSurface").ActualHeight <= search.MaxDropDownHeight + .5);
                        search.IsDropDownOpen = false;
                    }
                    PersonalSelect.SetScope(panel, false); await Drain(host);
                    Assert.NotSame(template, search.Template); Assert.True(search.IsTextSearchEnabled);
                    Assert.Null(originalView.Filter);
                    host.Close(); host = null;
                    await CheckHighContrast(app);

                    using var model = new PersonalApplicationModel(data);
                    model.Store.SaveProject(new(Guid.Empty, 0, "Проверка списков", null, Task.Desktop.Projects.DesktopProjectStatus.Active, null, null));
                    model.Planning!.Refresh();
                    Exception? createError = null;
                    model.Tasks!.NewCommand.ExecutionFailed += error => createError = error;
                    personal = new PersonalWindow(model) { ShowInTaskbar = false };
                    personal.Show(); personal.Activate(); await Drain(personal);
                    var shell = (PersonalShellViewModel)personal.DataContext;
                    var inspected = new HashSet<ComboBox>();
                    foreach (var section in shell.Sections)
                    {
                        shell.SelectedSection = section; await Drain(personal);
                        if (section.Route == "tasks")
                        {
                            Assert.True(await model.Tasks!.NewCommand.ExecuteAsync(), "Personal new-task command disabled");
                            Assert.True(model.Tasks.Editor is not null, "Personal editor failed: " + createError);
                            await Drain(personal);
                        }
                        for (var pass = 0; pass < 3; pass++)
                        {
                            foreach (var expander in Descendants<Expander>(personal).Where(e => e.IsVisible).ToArray()) expander.IsExpanded = true;
                            await Drain(personal);
                        }
                        foreach (var combo in Descendants<ComboBox>(personal).Where(c => c.IsLoaded))
                        { Assert.True(PersonalSelect.GetScope(combo)); Assert.Same(template, combo.Template); inspected.Add(combo); }
                        if (section.Route == "tasks") await CheckTaskEditor(personal, model);
                        if (model.Tasks!.Editor is not null) await model.Tasks.CancelCommand.ExecuteAsync();
                    }
                    Assert.True(inspected.Count >= 15, $"Expected personal fields and shared calendar/workspace selectors; inspected {inspected.Count}.");
                    personal.CloseForContextTransition(); personal = null;
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { host?.Close(); personal?.CloseForContextTransition(); app.Shutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }
    public sealed record ProjectOption(string Name);
    private static async System.Threading.Tasks.Task CheckHighContrast(System.Windows.Application app)
    {
        string? sourcePath = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Task.Desktop", "Resources", "Personal.Select.xaml");
            if (File.Exists(candidate)) { sourcePath = candidate; break; }
        }
        Assert.NotNull(sourcePath);
        // Exercise production HC setters with deterministic system colours; do not change Windows settings.
        app.Resources["Personal.Select.TestHighContrast"] = true;
        app.Resources[SystemColors.WindowBrushKey] = Brushes.Black;
        app.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
        app.Resources[SystemColors.HighlightBrushKey] = Brushes.Yellow;
        app.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
        var source = File.ReadAllText(sourcePath).Replace("clr-namespace:Task.Desktop.Infrastructure", "clr-namespace:Task.Desktop.Infrastructure;assembly=Task.Desktop")
            .Replace("{Binding Source={x:Static SystemParameters.HighContrast}}", "{Binding Source={StaticResource Personal.Select.TestHighContrast}}");
        var styles = (ResourceDictionary)XamlReader.Parse(source);
        var keys = styles.Keys.Cast<object>().ToArray();
        foreach (var key in keys) app.Resources[key] = styles[key];
        var combo = new ComboBox { ItemsSource = new[] { "Первый", "Второй" }, SelectedIndex = 1, MinWidth = 260 };
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(combo); PersonalSelect.SetScope(panel, true);
        var window = new Window { Content = panel, Width = 400, Height = 400, ShowInTaskbar = false };
        try
        {
            window.Show(); combo.IsDropDownOpen = true; await Drain(window);
            Assert.Equal(Colors.Black, ((SolidColorBrush)Part<Border>(combo, "PopupSurface").Background).Color);
            Assert.Equal(Colors.White, ((SolidColorBrush)combo.Foreground).Color);
            var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.Equal(Colors.Yellow, ((SolidColorBrush)Part<Border>(item, "Item").Background).Color);
            Assert.Equal(Colors.Black, ((SolidColorBrush)item.Foreground).Color);
        }
        finally
        {
            window.Close();
            foreach (var key in keys) app.Resources.Remove(key);
            foreach (var key in new object[] { "Personal.Select.TestHighContrast", SystemColors.WindowBrushKey, SystemColors.WindowTextBrushKey, SystemColors.HighlightBrushKey, SystemColors.HighlightTextBrushKey }) app.Resources.Remove(key);
        }
    }
    private static async System.Threading.Tasks.Task CheckTaskEditor(PersonalWindow window, PersonalApplicationModel model)
    {
        var project = Descendants<ComboBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == "PersonalTaskProject");
        project.BringIntoView(); project.IsDropDownOpen = true; await Drain(window);
        Assert.True(project.IsVisible); Assert.Equal(PersonalSelectVariant.Search, PersonalSelect.GetVariant(project));
        Assert.True(project.Items.Count >= 2);
        Capture(window, "personal-project-select.png"); CapturePopup(project, "project-popup.png");
        var editor = model.Tasks!.Editor;
        PressKey(Part<TextBox>(project, "PART_Search"), Key.Escape); await Drain(window);
        Assert.False(project.IsDropDownOpen); Assert.Same(editor, model.Tasks.Editor);
        var priority = Descendants<ComboBox>(window).First(c => c.IsVisible && AutomationProperties.GetName(c) == "Приоритет");
        priority.BringIntoView(); priority.IsDropDownOpen = true; await Drain(window);
        Assert.Equal(PersonalSelectVariant.Detailed, PersonalSelect.GetVariant(priority));
        Capture(window, "personal-priority-select.png"); CapturePopup(priority, "priority-popup.png");
        priority.IsDropDownOpen = false;
    }
    public sealed class SelectionModel
    {
        public ObservableCollection<ProjectOption> Projects { get; } = [new("Альфа"), new("Бета"), new("Гамма")];
        public ProjectOption? Selected { get; set; }
        public SelectionModel() => Selected = Projects[0];
    }
    private static async System.Threading.Tasks.Task Drain(Window window)
    { await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout(); await System.Threading.Tasks.Task.Delay(100); }
    private static T Part<T>(Control control, string name) where T : DependencyObject
    { control.ApplyTemplate(); return Assert.IsType<T>(control.Template.FindName(name, control)); }
    private static void PressKey(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(element), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_PERSONAL_SELECT_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var target = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
    private static void CapturePopup(ComboBox combo, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_PERSONAL_SELECT_EVIDENCE") is not { Length: > 0 } directory) return;
        var popup = (FrameworkElement)Part<Popup>(combo, "PART_Popup").Child;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth), (int)Math.Ceiling(popup.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(popup); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
