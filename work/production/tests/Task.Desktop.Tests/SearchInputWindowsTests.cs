using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Task.Desktop.Infrastructure;

namespace Task.Desktop.Tests;

public sealed class SearchInputWindowsTests
{
    [Fact]
    public void CorporateAndSharedSearchInputsUseTheSharedCapsuleStyle()
    {
        var source = SourceDirectory();
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var inputs = Directory.EnumerateFiles(source, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            // Personal browser screens have their own approved presentation styles.
            .Where(path => !Path.GetRelativePath(source, path).StartsWith(Path.Combine("Views", "Personal"), StringComparison.Ordinal))
            .SelectMany(path => XDocument.Load(path).Descendants(ui + "TextBox")
                .Where(input => Regex.IsMatch((string?)input.Attribute("Text") ?? "", "Search|Query")
                    || (string?)input.Attribute(x + "Name") == "PART_Search")
                .Select(input => (Path: Path.GetRelativePath(source, path), Input: input))).ToArray();
        Assert.NotEmpty(inputs);
        foreach (var input in inputs)
        {
            Assert.Contains("Task.Input.Search", (string?)input.Input.Attribute("Style") ?? "");
            Assert.False(string.IsNullOrWhiteSpace((string?)input.Input.Attribute("Tag")), input.Path);
            Assert.Empty(input.Input.Elements(ui + "TextBox.Template"));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task NativeSearchKeepsEditingFocusContrastAndDropdownFiltering()
    {
        const string flag = "TASK_SEARCH_INPUT_PROBE";
        if (Environment.GetEnvironmentVariable(flag) != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(SearchInputWindowsTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeSearchKeepsEditingFocusContrastAndDropdownFiltering));
            start.Environment[flag] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(70)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(true); }
            return;
        }
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new App(); app.InitializeComponent();
            Window? window = null;
            app.Dispatcher.BeginInvoke(() =>
            {
            try
            {
                var model = new SearchModel();
                var input = new TextBox { Style = (Style)app.FindResource("Task.Input.Search"), Tag = "Найти вариант…" };
                input.SetBinding(TextBox.TextProperty, new Binding(nameof(SearchModel.Query))
                    { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                var ordinary = new TextBox { Text = "Обычное поле", Style = (Style)app.FindResource("Task.Input.Text"), Margin = new Thickness(0, 16, 0, 16) };
                var combo = new ComboBox { Style = (Style)app.FindResource("Task.Input.Combo"), ItemsSource = new[] { "Альфа", "Бета", "Гамма" }, SelectedIndex = 0 };
                PersonalSelect.SetVariant(combo, PersonalSelectVariant.Search);
                var chips = new ListBox { ItemsSource = new[] { "Все", "Задачи", "Проекты", "Файлы" },
                    ItemContainerStyle = (Style)app.FindResource("Task.Filter.Chip"), BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent, Margin = new Thickness(0, 16, 0, 0) };
                chips.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse(
                    "<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><WrapPanel/></ItemsPanelTemplate>");
                chips.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SearchModel.Kind))
                    { Source = model, Mode = BindingMode.TwoWay });
                var panel = new StackPanel { Margin = new Thickness(22), Width = 350 };
                panel.Children.Add(input); panel.Children.Add(ordinary); panel.Children.Add(combo); panel.Children.Add(chips);
                PersonalSelect.SetScope(panel, true);
                window = new Window { Content = panel, Width = 820, Height = 620, ShowInTaskbar = false };
                window.Show(); window.Activate(); Pump();
                Assert.Null(input.FocusVisualStyle);
                Assert.Equal(Visibility.Visible, Part<TextBlock>(input, "SearchPlaceholder").Visibility);
                Assert.True(input.Focus()); Pump();
                Assert.Equal(new Thickness(1), Part<Border>(input, "SearchChrome").BorderThickness);
                Assert.Equal(new CornerRadius(24), Part<Border>(input, "SearchChrome").CornerRadius);
                Assert.Same(app.FindResource("Task.Capsule.Accent"), Part<Border>(input, "SearchChrome").BorderBrush);
                Assert.Equal(42, input.ActualHeight);
                Assert.Equal("Segoe UI", input.FontFamily.Source);
                var provider = (IValueProvider)new TextBoxAutomationPeer(input).GetPattern(PatternInterface.Value);
                provider.SetValue("Договор"); Pump();
                Assert.Equal("Договор", model.Query);
                var clearButton = Part<Button>(input, "ClearSearch");
                Assert.True(clearButton.Command.CanExecute(null));
                ((IInvokeProvider)new ButtonAutomationPeer(clearButton).GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
                Assert.Equal("", model.Query);
                Assert.NotNull(BindingOperations.GetBindingExpression(input, TextBox.TextProperty));
                provider.SetValue("Договор"); Pump();
                Assert.NotNull(BindingOperations.GetBindingExpression(input, TextBox.TextProperty));
                Assert.Equal(Visibility.Collapsed, Part<TextBlock>(input, "SearchPlaceholder").Visibility);
                Assert.NotNull(Part<ScrollViewer>(input, "PART_ContentHost"));
                input.IsReadOnly = true; Assert.True(provider.IsReadOnly); input.IsReadOnly = false;
                ordinary.Focus(); Pump();
                Assert.Equal(new Thickness(1), Part<Border>(input, "SearchChrome").BorderThickness);
                Assert.Same(app.FindResource("Task.Capsule.Fill"), Part<Border>(input, "SearchChrome").Background);
                Assert.Equal(new Thickness(1), ordinary.BorderThickness);
                Assert.Equal(new CornerRadius(24), Part<Border>(combo, "Chrome").CornerRadius);
                var taskChip = (ListBoxItem)chips.ItemContainerGenerator.ContainerFromIndex(1);
                var taskSelection = (ISelectionItemProvider)new ListBoxItemAutomationPeer(chips.Items[1], new ListBoxAutomationPeer(chips)).GetPattern(PatternInterface.SelectionItem);
                taskSelection.Select(); Pump();
                Assert.Equal("Задачи", model.Kind);
                Assert.Equal(new CornerRadius(24), Part<Border>(taskChip, "ChipChrome").CornerRadius);
                Assert.Same(app.FindResource("Task.Capsule.Selected"), taskChip.Background);
                Assert.True(taskChip.Focus()); Pump();
                Assert.Same(app.FindResource("Task.Capsule.Accent"), taskChip.BorderBrush);
                Assert.NotNull(BindingOperations.GetBindingExpression(chips, Selector.SelectedItemProperty));
                foreach (var scale in new[] { 1d, 1.5d, 2d })
                {
                    panel.LayoutTransform = new ScaleTransform(scale, scale); Pump();
                    Assert.True(input.ActualHeight >= 40);
                    Assert.True(Part<ScrollViewer>(input, "PART_ContentHost").ActualWidth > 100);
                }
                panel.LayoutTransform = Transform.Identity; Pump();
                combo.IsDropDownOpen = true; Pump();
                var popupSearch = Part<TextBox>(combo, "PART_Search");
                Assert.Equal(new CornerRadius(8), Part<Border>(combo, "PopupSurface").CornerRadius);
                Assert.Same(input.Style, popupSearch.Style);
                Assert.Equal(new CornerRadius(24), Part<Border>(popupSearch, "SearchChrome").CornerRadius);
                popupSearch.Text = "Бета"; Pump();
                Assert.Equal(Visibility.Collapsed, ((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0)).Visibility);
                Assert.Equal(Visibility.Visible, ((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1)).Visibility);
                Assert.Equal("Альфа", combo.SelectedItem);
                combo.IsDropDownOpen = false;
                combo.Width = 370;
                combo.ItemsSource = new[] { "Альфа", "Обновление офиса", "Сайт компании" };
                combo.SelectedIndex = 2; combo.IsDropDownOpen = true; Pump();
                ((ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(2)).Focus(); Pump();
                var popupSurface = Part<Border>(combo, "PopupSurface");
                Assert.Equal(((SolidColorBrush)app.FindResource("Task.Capsule.Panel")).Color, ((SolidColorBrush)popupSurface.Background).Color);
                Assert.Equal(((SolidColorBrush)app.FindResource("Task.Capsule.Fill")).Color, ((SolidColorBrush)Part<TextBox>(combo, "PART_Search").Background).Color);
                CaptureComponent(popupSurface, "reference-dropdown.png");
                combo.IsDropDownOpen = false;
                input.Focus(); Pump(); Capture(window, "capsule-fields-and-filters.png");
                app.Resources[SystemColors.WindowBrushKey] = Brushes.Black;
                app.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
                app.Resources[SystemColors.HighlightBrushKey] = Brushes.Yellow;
                app.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
                app.Resources["Task.Test.SearchHighContrast"] = true;
                var xaml = File.ReadAllText(Path.Combine(SourceDirectory(), "Resources", "Controls.Search.xaml"))
                    .Replace("{Binding Source={x:Static SystemParameters.HighContrast}}", "{Binding Source={StaticResource Task.Test.SearchHighContrast}}", StringComparison.Ordinal)
                    .Replace("clr-namespace:Task.Desktop.Infrastructure", "clr-namespace:Task.Desktop.Infrastructure;assembly=Task.Desktop", StringComparison.Ordinal);
                var highContrast = (ResourceDictionary)XamlReader.Parse(xaml, new ParserContext
                    { BaseUri = new Uri(Path.Combine(SourceDirectory(), "Resources", "Controls.Search.xaml")) });
                input.Style = (Style)highContrast["Task.Input.Search"]; input.ApplyTemplate(); input.Focus(); Pump();
                Assert.True(input.IsKeyboardFocusWithin);
                Assert.Equal(Colors.Yellow, ((SolidColorBrush)Part<Border>(input, "SearchChrome").BorderBrush).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)Part<System.Windows.Shapes.Path>(input, "SearchIcon").Stroke).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)input.Foreground).Color);
                // Exercise the production chip template with simulated OS contrast resources as well.
                var chipPath = Path.Combine(SourceDirectory(), "Resources", "Controls.Data.xaml");
                var chipXaml = File.ReadAllText(chipPath).Replace(
                    "{Binding Source={x:Static SystemParameters.HighContrast}}", "{Binding Source={StaticResource Task.Test.SearchHighContrast}}", StringComparison.Ordinal)
                    .Replace("clr-namespace:Task.Desktop.ViewModels", "clr-namespace:Task.Desktop.ViewModels;assembly=Task.Desktop", StringComparison.Ordinal);
                var chipStyles = (ResourceDictionary)XamlReader.Parse(chipXaml, new ParserContext { BaseUri = new Uri(chipPath) });
                chips.ItemContainerStyle = (Style)chipStyles["Task.Filter.Chip"]; Pump();
                taskChip = (ListBoxItem)chips.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.Equal(Colors.Yellow, ((SolidColorBrush)taskChip.Background).Color);
                Assert.Equal(Colors.Black, ((SolidColorBrush)taskChip.Foreground).Color);
                Capture(window, "capsule-fields-and-filters-high-contrast.png");
            }
            catch (Exception e) { failure = e; }
            finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    public sealed class SearchModel
    {
        public string Query { get; set; } = "";
        public string Kind { get; set; } = "Все";
    }
    private static T Part<T>(Control control, string name) where T : class => Assert.IsType<T>(control.Template.FindName(name, control));
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static string SourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Task.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Task.Desktop");
    }
    private static void Capture(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_SEARCH_EVIDENCE") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }

    private static void CaptureComponent(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_SEARCH_EVIDENCE") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(element), rect.Size), Stretch = Stretch.Fill }, null, rect);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
}
