using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
        app.Resources.Add("Task.BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
        var errors = new StringWriter();
        var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var results = new List<object>();
        foreach (var size in new[] { (800d, 600d), (1100d, 1000d) })
        foreach (var mode in new[] { "channel", "address", "delete", "readonly" })
        {
            var client = new Client();
            using var model = new ContactCardViewModel(client, () => true, () => true, () => mode != "readonly");
            model.SelectAsync(client.Current.Contact.Id).GetAwaiter().GetResult();
            var forcedContrast = args.Contains("--contrast");
            UserControl view;
            if (forcedContrast)
            {
                var resources = File.ReadAllText("work/production/src/Task.Desktop/Resources/Controls.ContactRelations.xaml")
                    .Replace("{x:Static SystemParameters.HighContrast}", "True");
                var xaml = File.ReadAllText("work/production/src/Task.Desktop/Views/ContactRelationsView.xaml");
                xaml = Regex.Replace(xaml, " x:Class=\"[^\"]+\"", "");
                xaml = xaml.Replace(" IsVisibleChanged=\"OnRelationEditorVisibilityChanged\"", "")
                    .Replace("<ResourceDictionary Source=\"/Task.Desktop;component/Resources/Controls.ContactRelations.xaml\" />", resources);
                view = (UserControl)XamlReader.Parse(xaml);
                view.DataContext = model;
            }
            else view = new ContactRelationsView { DataContext = model };
            var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var window = new Window { Content = scroll, Width = size.Item1, Height = size.Item2, Title = "Task PR18 — isolated contact review", ShowInTaskbar = false };
            window.Show(); Pump();
            if (mode == "channel") model.AddChannelCommand.Execute(null);
            if (mode == "address") model.AddAddressCommand.Execute(null);
            if (mode == "delete") model.RemoveChannelCommand.Execute(model.Channels[0]);
            Pump();
            var editor = Descendants<Border>(view).Single(x => AutomationProperties.GetName(x) == "Редактирование контактных связей");
            if (forcedContrast && editor.IsVisible) { editor.BringIntoView(); Pump(); }
            var top = editor.IsVisible ? editor.TranslatePoint(new Point(), scroll).Y : 0;
            if (editor.IsVisible && (top < -1 || top > scroll.ViewportHeight - 30)) throw new Exception($"Editor not scrolled into view: {mode} {top}");
            if (mode == "readonly" && Descendants<Button>(view).Any(b => b.IsEnabled)) throw new Exception("Read-only mutation enabled");
            foreach (var combo in Descendants<ComboBox>(view).Where(c => c.IsVisible))
            {
                combo.IsDropDownOpen = true; Pump();
                if (combo.Template.FindName("PART_Popup", combo) is not System.Windows.Controls.Primitives.Popup { IsOpen: true }) throw new Exception("Popup did not open");
                combo.IsDropDownOpen = false; Pump();
            }
            if (forcedContrast)
            {
                foreach (var check in Descendants<CheckBox>(view).Where(c => c.IsVisible))
                {
                    check.IsChecked = true; Pump();
                    var row = (Border)check.Template.FindName("Row", check);
                    var tick = (System.Windows.Shapes.Path)check.Template.FindName("Tick", check);
                    if (!Equals(row.Background, SystemColors.WindowBrush) || !Equals(tick.Stroke, SystemColors.WindowTextBrush))
                        throw new Exception("Forced high contrast checkbox does not use system colors");
                }
            }
            foreach (var scale in new[] { 1d, 1.5d, 2d })
                Save(scroll, Path.Combine(output, $"{mode}-{size.Item1}x{size.Item2}-{scale * 100:0}pct.png"), scale);
            results.Add(new { mode, width = size.Item1, height = size.Item2, editorTop = top, offset = scroll.VerticalOffset, viewport = scroll.ViewportHeight });
            if (mode is "channel" or "address")
            {
                var save = Descendants<Button>(view).Single(b => AutomationProperties.GetAutomationId(b) == "SaveContactRelation");
                save.BringIntoView(); Pump();
                var bounds = save.TransformToAncestor(scroll).TransformBounds(new Rect(save.RenderSize));
                if (bounds.Top < -1 || bounds.Bottom > scroll.ViewportHeight + 1) throw new Exception("Save clipped");
                model.CancelRelationCommand.Execute(null); Pump();
                if (model.HasRelationDraft) throw new Exception("Cancel failed");
            }
            window.Close(); Pump();
        }
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "binding-errors.log"), errors.ToString());
        if (errors.ToString().Contains("System.Windows.Data Error")) throw new Exception(errors.ToString());
        if (args.Contains("--interactive"))
        {
            var client = new Client();
            var model = new ContactCardViewModel(client, () => true, () => true, () => true);
            model.SelectAsync(client.Current.Contact.Id).GetAwaiter().GetResult();
            var window = new Window { Content = new ScrollViewer { Content = new ContactRelationsView { DataContext = model }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Width = 800, Height = 600, Title = "Task PR18 — isolated contact review" };
            model.PropertyChanged += (_, _) => File.WriteAllText(Path.Combine(output, "native-state.json"), JsonSerializer.Serialize(new { model.HasChannelDraft, model.HasAddressDraft, type = model.ChannelDraft?.Type ?? model.AddressDraft?.Type, primary = model.ChannelDraft?.IsPrimary ?? model.AddressDraft?.IsPrimary, channels = model.Channels.Count, addresses = model.Addresses.Count, client.Operations }));
            window.Closed += (_, _) => app.Shutdown();
            app.Run(window);
        }
        else app.Shutdown();
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
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Save(FrameworkElement view, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(view.ActualWidth * scale), (int)(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class Client : IDesktopContactDetailsClient, IDesktopContactRelationsClient
    {
        public DesktopContactDetails Current = new(new(Guid.NewGuid(), 1, "Тестовый контакт PR18", "Тест", null, null, "active", "active"), null)
        {
            Channels = Enumerable.Range(1, 8).Select(i => new DesktopContactChannel(Guid.NewGuid(), "phone", $"+375 00 00000{i:00}", "Тестовый канал")).ToArray(),
            Addresses = [new(Guid.NewGuid(), "work", "Тестовый адрес, Минск")]
        };
        public List<string> Operations = [];
        private System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> Ok() => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(Current, Current.Contact.Version));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default) => Ok();
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails details, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> SaveChannelAsync(Guid id, long version, DesktopContactChannel channel, CancellationToken ct = default)
        {
            Operations.Add("channel");
            Current = Current with { Contact = Current.Contact with { Version = version + 1 }, Channels = Current.Channels.Where(c => c.Id != channel.Id).Append(channel with { Id = channel.Id == Guid.Empty ? Guid.NewGuid() : channel.Id }).ToArray() };
            return Ok();
        }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> DeleteChannelAsync(Guid id, long version, Guid channelId, CancellationToken ct = default)
        {
            Operations.Add("delete"); Current = Current with { Contact = Current.Contact with { Version = version + 1 }, Channels = Current.Channels.Where(c => c.Id != channelId).ToArray() }; return Ok();
        }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> AddAddressAsync(Guid id, long version, DesktopContactAddress address, CancellationToken ct = default)
        {
            Operations.Add("address"); Current = Current with { Contact = Current.Contact with { Version = version + 1 }, Addresses = Current.Addresses.Append(address with { Id = Guid.NewGuid() }).ToArray() }; return Ok();
        }
    }
}
