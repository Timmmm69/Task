using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Personal;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
        using var store = new PersonalTaskStore(new PersonalDataPaths(Path.Combine(output, "data")));
        using var model = new RecurrencePaneViewModel(new PersonalRecurrenceClient(store), store.LocalActorId, true);
        var rows = new List<object>();
        foreach (var manage in new[] { true, false })
        foreach (var width in new[] { 1240d, 900d, 700d })
        {
            model.SetAccess(manage ? ["Recurrence.Read", "Recurrence.Manage"] : ["Recurrence.Read"], true);
            model.OpenCommand.Execute(null);
            var view = new RecurrenceView { DataContext = model };
            var window = new Window { Content = view, Width = width + 16, Height = 800, Left = -20000, ShowInTaskbar = false };
            window.Show(); Pump();
            var editor = Descendants<Grid>(view).Single(e => AutomationProperties.GetAutomationId(e) == "RecurrenceEditorPanel");
            var preview = Descendants<StackPanel>(view).Single(e => AutomationProperties.GetAutomationId(e) == "RecurrencePreviewPanel");
            var save = Descendants<Button>(view).Single(b => b.Content as string == "Сохранить правило серии");
            var ep = editor.TranslatePoint(new Point(), view);
            var pp = preview.TranslatePoint(new Point(), view);
            var sp = save.TranslatePoint(new Point(), view);
            bool stacked = pp.Y > ep.Y + 10;
            if (stacked != (view.ActualWidth < 1050)) throw new Exception("Responsive preview position is wrong");
            if (sp.Y < 0 || sp.Y + save.ActualHeight > view.ActualHeight) throw new Exception("Save action is clipped");
            if (save.IsEnabled != manage) throw new Exception("Read-only action state is wrong");
            rows.Add(new { width = view.ActualWidth, manage, stacked, editorWidth = editor.ActualWidth, editorHeight = editor.ActualHeight, previewY = pp.Y, saveY = sp.Y, saveEnabled = save.IsEnabled });
            foreach (var scale in new[] { 1d, 1.5d, 2d })
            {
                var bitmap = new RenderTargetBitmap((int)(view.ActualWidth * scale), (int)(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, $"recurrence-{width}-{(manage ? "edit" : "readonly")}-{scale * 100:0}pct.png"));
                encoder.Save(stream);
            }
            window.Close(); Pump(); model.Close();
        }
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        app.Shutdown();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
