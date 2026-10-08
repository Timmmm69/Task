using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;

internal static class Program
{
    private static string _output = "";
    [STAThread]
    private static void Main(string[] args)
    {
        _output = Path.GetFullPath(args[0]); Directory.CreateDirectory(_output);
        var app = new Task.Desktop.App(); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Probe(app); File.WriteAllText(Path.Combine(_output, "ui-result.json"), JsonSerializer.Serialize(new { passed = true, viewport = "1120x700", slotAccessibleName = _slotName, scrollOffset = _offset, logicalFocus = _focus, automaticWrites = 0, uiHeartbeatsDuringRead = _heartbeats })); }
            catch (Exception error) { File.WriteAllText(Path.Combine(_output, "ui-error.txt"), error.ToString()); Environment.ExitCode = 1; }
            finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
    }
    private static string? _slotName;
    private static double _offset;
    private static string? _focus;
    private static int _heartbeats;
    private static async System.Threading.Tasks.Task Probe(Application app)
    {
        var date = new DateOnly(2026, 10, 8); var now = new DateTimeOffset(date.ToDateTime(new(8, 0)), TimeSpan.Zero);
        var settings = new DesktopUserSettings(1, "ru", "24h", 1, "09:00", "18:00", [6, 7], 30, 10, false, true, true, "ask");
        var client = new Client(); using var vm = new CalendarViewModel(client, ["Calendar.Read", "CalendarEvent.Create"], TimeZoneInfo.Utc,
            date.ToDateTime(TimeOnly.MinValue), clock: () => now,
            userSettings: _ => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopUserSettings>>(new DesktopWorkResult<DesktopUserSettings>.Succeeded(settings)));
        var view = new CalendarView { DataContext = vm };
        var window = new Window { Content = view, Width = 1120, Height = 700, Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Style = (Style)app.FindResource("Task.Window.Style") };
        window.Show(); await vm.ActivateAsync(); await Idle();
        var find = Descendants(view).OfType<Button>().Single(b => b.Command == vm.FindTimeCommand);
        if (!find.IsVisible || !find.IsTabStop) throw new Exception("Toolbar command is not accessible.");
        client.ReadDelayMilliseconds = 250;
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        heartbeat.Tick += (_, _) => _heartbeats++;
        heartbeat.Start(); await vm.FindTimeCommand.ExecuteAsync(); heartbeat.Stop();
        client.ReadDelayMilliseconds = 0;
        if (_heartbeats < 2) throw new Exception("Search blocked the UI dispatcher.");
        await Idle();
        Capture(window, "search.png");
        var buttons = Descendants(view).OfType<Button>().Where(b => b.Command == vm.ChooseFreeTimeCommand).ToArray();
        if (buttons.Length != 3 || buttons.Any(b => !b.IsVisible || !b.IsTabStop)) throw new Exception("Expected 3 visible keyboard slots.");
        var peer = new ButtonAutomationPeer(buttons[0]); _slotName = peer.GetName();
        if (_slotName != vm.FreeTimeSlots[0].Label || !_slotName.Contains("30 мин")) throw new Exception("Screen reader name is incomplete.");
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke(); await Idle();
        var scroll = (ScrollViewer)view.FindName("CalendarTimelineScroll");
        _offset = scroll.VerticalOffset; _focus = (FocusManager.GetFocusedElement(window) as FrameworkElement)?.Name;
        if (vm.ViewMode != CalendarViewMode.Day || !vm.ShowFreeTimeHighlight || _offset < 500 || vm.Editor is not null || client.Writes != 0) throw new Exception("Slot navigation did not work.");
        Capture(window, "selected.png");
        await System.Threading.Tasks.Task.Delay(5200); await Idle();
        if (vm.ShowFreeTimeHighlight) throw new Exception("Highlight did not expire.");
        window.Close();
    }
    private static async System.Threading.Tasks.Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void Capture(FrameworkElement view, string name)
    {
        view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(_output, name)); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private sealed class Client : IDesktopCalendarApiClient
    {
        public int Writes { get; private set; }
        public int ReadDelayMilliseconds { get; set; }
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset from, DateTimeOffset to, string zone, CancellationToken token)
        {
            if (ReadDelayMilliseconds > 0) Thread.Sleep(ReadDelayMilliseconds);
            return System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<DesktopSchedulePage>>(new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], from, to)));
        }
        public System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token) => System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>>(new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([]));
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand c, CancellationToken token) { Writes++; throw new NotSupportedException(); }
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid id, long version, DesktopCalendarEventCommand c, CancellationToken token) { Writes++; throw new NotSupportedException(); }
    }
}
