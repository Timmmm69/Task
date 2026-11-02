using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Infrastructure;
using Task.Desktop.Tests.TaskScreen;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public sealed class CorporateSelectWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task CorporateSelectorsKeepFilteringDraftsPermissionsAndCustomTemplates()
    {
        const string flag = "TASK_CORPORATE_SELECT_PROBE";
        if (Environment.GetEnvironmentVariable(flag) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(CorporateSelectWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(CorporateSelectorsKeepFilteringDraftsPermissionsAndCustomTemplates));
            start.Environment[flag] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(85)); Assert.True(child.ExitCode == 0, await output + await error); }
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
                MainWindow? window = null;
                try
                {
                    var client = new ActionTasksClient(ActionTasksClient.Source("Корпоративная задача"));
                    using var tasks = new TasksViewModel(client, ["Task.Read", "Task.Create", "Task.Update"]);
                    using var shell = new MainWindowViewModel(new Uri("https://fixture.invalid"), null, tasks);
                    shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks"); await tasks.ActivateAsync();
                    window = new MainWindow(shell) { ShowInTaskbar = false };
                    window.Show(); window.Activate(); await Drain(window);
                    var template = app.FindResource("Personal.Select.Template");
                    Assert.True(PersonalSelect.GetScope(window));
                    foreach (var combo in Descendants<ComboBox>(window).Where(c => c.IsLoaded)) Assert.Same(template, combo.Template);
                    foreach (var id in new[] { "TasksStatusFilter", "TasksPriorityFilter", "TasksDeadlineFilter", "TasksSortOrder" })
                    {
                        var combo = Find(window, id); Assert.Equal(PersonalSelectVariant.Compact, PersonalSelect.GetVariant(combo));
                        combo.IsDropDownOpen = true; await Drain(window); Assert.True(Part<Popup>(combo, "PART_Popup").IsOpen);
                        combo.IsDropDownOpen = false;
                    }
                    Assert.Equal(PersonalSelectVariant.Search, PersonalSelect.GetVariant(Find(window, "TasksProjectFilter")));
                    Assert.True(await tasks.NewTaskCommand.ExecuteAsync()); await Drain(window); Assert.NotNull(tasks.Editor);
                    tasks.Editor!.Title = "Черновик корпоративной задачи";
                    var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
                    tasks.Editor.Card.SetOptions(new JsonObject { ["projects"] = new JsonArray(
                        new JsonObject { ["id"] = firstId.ToString("D"), ["name"] = "Альфа" },
                        new JsonObject { ["id"] = secondId.ToString("D"), ["name"] = "Бета" }) });
                    for (var pass = 0; pass < 3; pass++)
                    {
                        foreach (var expander in Descendants<Expander>(window).Where(e => e.IsVisible).ToArray()) expander.IsExpanded = true;
                        await Drain(window);
                    }
                    var project = Find(window, "TaskProjectComboBox"); project.BringIntoView(); project.IsDropDownOpen = true; await Drain(window);
                    Assert.True(project.IsVisible); Assert.Equal(PersonalSelectVariant.Search, PersonalSelect.GetVariant(project));
                    var source = project.ItemsSource; var selected = project.SelectedItem;
                    var input = Part<TextBox>(project, "PART_Search"); input.Text = "БЕТА"; await Drain(window);
                    Assert.Same(source, project.ItemsSource); Assert.Same(selected, project.SelectedItem);
                    Assert.Null(CollectionViewSource.GetDefaultView(source).Filter);
                    Press(input, Key.Enter); await Drain(window);
                    Assert.Equal(secondId, tasks.Editor.Card.Project!.Id); Assert.False(project.IsDropDownOpen);
                    Assert.Empty(client.Creates); // Choosing a value never saves a task or issues a write.
                    project.IsDropDownOpen = true; await Drain(window);
                    CapturePopup(project, "corporate-project-popup.png");
                    var editor = tasks.Editor;
                    Press(Part<TextBox>(project, "PART_Search"), Key.Escape); await Drain(window);
                    Assert.Same(editor, tasks.Editor); Assert.False(project.IsDropDownOpen);
                    var priority = Find(window, "TaskPriorityComboBox"); priority.BringIntoView(); priority.IsDropDownOpen = true; await Drain(window);
                    Assert.Equal(PersonalSelectVariant.Detailed, PersonalSelect.GetVariant(priority));
                    CapturePopup(priority, "corporate-priority-popup.png"); priority.IsDropDownOpen = false;
                    foreach (var combo in Descendants<ComboBox>(window).Where(c => c.IsLoaded)) Assert.Same(template, combo.Template);
                    var people = Descendants<ComboBox>(window).Where(c =>
                        BindingOperations.GetBinding(c, ItemsControl.ItemsSourceProperty)?.Path?.Path is "People" or "Counterparties").ToArray();
                    Assert.Equal(2, people.Length);
                    Assert.All(people, c => Assert.Equal(PersonalSelectVariant.Search, PersonalSelect.GetVariant(c)));
                    window.Width = 800; window.Height = 600; await Drain(window);
                    project.BringIntoView(); project.IsDropDownOpen = true; await Drain(window);
                    Assert.True(Part<Border>(project, "PopupSurface").ActualWidth <= project.ActualWidth + .5);
                    Assert.True(Part<Border>(project, "PopupSurface").ActualHeight <= project.MaxDropDownHeight + .5);
                    Press(Part<TextBox>(project, "PART_Search"), Key.Escape); await Drain(window); Assert.Same(editor, tasks.Editor);
                    await tasks.DiscardEditorCommand.ExecuteAsync(); await Drain(window);
                    tasks.UpdateCapabilities(["Task.Read"]); await Drain(window);
                    Assert.False(tasks.NewTaskCommand.CanExecute(null)); Assert.Empty(client.Creates);

                    // A custom item template remains intact, and search only uses its displayed date/title.
                    var occurrence = new Occurrence(new DateOnly(2026, 11, 12), "Повторяющаяся задача", Guid.NewGuid());
                    var occurrenceModel = new { Occurrences = new[] { occurrence } };
                    var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("Title"));
                    var itemTemplate = new DataTemplate { VisualTree = text };
                    var custom = new ComboBox { ItemTemplate = itemTemplate, Width = 280, DataContext = occurrenceModel };
                    custom.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Occurrences"));
                    var overlay = new Border { Child = custom, Background = Brushes.White, Padding = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumnSpan(overlay, 2); Grid.SetRowSpan(overlay, 3); Panel.SetZIndex(overlay, 999);
                    ((Grid)window.Content).Children.Add(overlay); await Drain(window);
                    custom.IsDropDownOpen = true; await Drain(window);
                    Assert.Same(template, custom.Template); Assert.Same(itemTemplate, custom.ItemTemplate);
                    Assert.Equal("12.11.2026 — Повторяющаяся задача", PersonalSelect.DisplayText(custom, occurrence));
                    var query = Part<TextBox>(custom, "PART_Search"); query.Text = occurrence.Id.ToString(); await Drain(window);
                    Assert.Equal(Visibility.Visible, Part<TextBlock>(custom, "PART_Empty").Visibility);
                    query.Text = "12.11.2026"; await Drain(window); Press(query, Key.Enter); await Drain(window);
                    Assert.Same(occurrence, custom.SelectedItem);
                    window.CloseAfterModeSwitch(); window = null;
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.CloseAfterModeSwitch(); app.Shutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(75));
    }
    public sealed record Occurrence(DateOnly LocalDate, string Title, Guid Id);
    private static async System.Threading.Tasks.Task Drain(Window window)
    { await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout(); await System.Threading.Tasks.Task.Delay(100); }
    private static ComboBox Find(Window window, string id) => Descendants<ComboBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static T Part<T>(Control control, string name) where T : DependencyObject
    { control.ApplyTemplate(); return Assert.IsType<T>(control.Template.FindName(name, control)); }
    private static void Press(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void CapturePopup(ComboBox combo, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_CORPORATE_SELECT_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var surface = (FrameworkElement)Part<Popup>(combo, "PART_Popup").Child;
        var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
