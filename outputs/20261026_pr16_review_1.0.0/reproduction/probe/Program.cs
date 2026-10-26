using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Task.Desktop.Administration;
using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

internal static class Program
{
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../evidence/probe"));
    static readonly string[] Grants = ["Calendar.Read", "CalendarEvent.Create", "CalendarEvent.Update", "User.Read"];
    static readonly Type Fixture = typeof(Task.Desktop.Tests.Calendar.CalendarEmployeePickerTests);
    static readonly List<object> Metrics = [];
    static Window window = null!;
    static ContentControl host = null!;
    static CalendarViewModel? vm;
    static bool personal;
    static bool edit;
    static IDesktopCalendarApiClient Client() => (IDesktopCalendarApiClient)Activator.CreateInstance(Fixture.GetNestedType("Client", BindingFlags.NonPublic)!, true)!;
    static IDesktopEmployeeDirectory DirectoryClient() => (IDesktopEmployeeDirectory)Fixture.GetMethod("Directory", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
    [STAThread]
    static void Main()
    {
        Directory.CreateDirectory(Root);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
        app.Resources["Task.CalendarLaneWidthConverter"] = new Task.Desktop.Converters.CalendarLaneWidthConverter();
        app.Resources["Task.CalendarLaneLeftConverter"] = new Task.Desktop.Converters.CalendarLaneLeftConverter();
        app.Resources["Task.TimelineLaneMarginConverter"] = new Task.Desktop.Converters.TimelineLaneMarginConverter();
        app.Resources["Task.WindowWidthAtLeastConverter"] = new Task.Desktop.Converters.WindowWidthAtLeastConverter();
        app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        window = new Window { Title = "Task · PR16 review · synthetic data", Width = 1120, Height = 900, Style = (Style)app.FindResource("Task.Window.Style") };
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        root.Children.Add(host); window.Content = root;
        void Button(string title, Func<System.Threading.Tasks.Task> action)
        { var b = new Button { Content = title, Padding = new Thickness(8), Margin = new Thickness(3) }; b.Click += async (_, _) => { try { await action(); } catch(Exception ex) { File.WriteAllText(Path.Combine(Root,"error.txt"),ex.ToString()); throw; } }; toolbar.Children.Add(b); }
        Button("Corporate", async () => { personal = false; await Open(); });
        Button("Personal", async () => { personal = true; await Open(); });
        Button("New / Edit", async () => { edit = !edit; await Open(); });
        Button("1120 × 900", () => { window.Width=1120; window.Height=900; return System.Threading.Tasks.Task.CompletedTask; });
        Button("900 × 700", () => { window.Width=900; window.Height=700; return System.Threading.Tasks.Task.CompletedTask; });
        Button("Today", async () => { vm?.Dispose(); vm=null; var today = new TodayViewModel(Client(), Grants); await today.ActivateAsync(); host.Content=new TodayView { DataContext=today }; });
        Button("Capture", () => { Capture((FrameworkElement)host.Content,"manual",1); return System.Threading.Tasks.Task.CompletedTask; });
        window.Loaded += async (_, _) =>
        {
            try
            {
                if(Environment.GetEnvironmentVariable("PR16_KEYBOARD_ONLY")=="1")
                {
                    await Open();
                    var view=(CalendarView)host.Content;
                    var status=Descendants<ComboBox>(view).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Статус события");
                    status.BringIntoView(); status.Focus(); Pump();
                    var source=PresentationSource.FromVisual(status)!;
                    var beforeStatus=vm!.Editor!.Status;
                    status.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Down) { RoutedEvent=Keyboard.KeyDownEvent });
                    Pump();
                    var arrowChangedStatus=vm.Editor.Status!=beforeStatus;
                    if(!arrowChangedStatus) throw new Exception("Arrow did not select status");
                    status.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Pump();
                    var tabStayedInEditor=((Border)view.FindName("CalendarEditorDialog")).IsKeyboardFocusWithin;
                    if(!tabStayedInEditor) throw new Exception("Focus left editor");
                    status.Focus();status.IsDropDownOpen=true; Pump();
                    status.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    File.WriteAllText(Path.Combine(Root,"keyboard.json"),System.Text.Json.JsonSerializer.Serialize(new{arrowChangedStatus,tabStayedInEditor,editorRetained=vm!.Editor is not null, status.IsDropDownOpen}));
                    app.Shutdown(); return;
                }
                foreach (var p in new[]{false,true}) foreach(var e in new[]{false,true})
                {
                    personal=p; edit=e; await Open();
                    foreach (var size in new[]{(1120d,900d),(900d,700d)})
                    {
                        window.Width=size.Item1; window.Height=size.Item2; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        var view=(CalendarView)host.Content;
                        var actions=(Grid)view.FindName("CalendarEditorActions");
                        var attendee=(Border)view.FindName("CalendarEditorAttendees");
                        var body=(Grid)view.FindName("CalendarEditorBody");
                        var bounds=actions.TransformToAncestor(view).TransformBounds(new Rect(actions.RenderSize));
                        if(bounds.Bottom>view.ActualHeight || bounds.Height<30) throw new Exception("Footer clipped");
                        if(personal && attendee.IsVisible) throw new Exception("Personal attendees visible");
                        if(personal && body.ColumnDefinitions[1].ActualWidth>1) throw new Exception("Personal empty column");
                        var label=$"{(p?"personal":"corporate")}-{(e?"edit":"new")}-{size.Item1}x{size.Item2}";
                        Metrics.Add(new { label, view.ActualWidth, view.ActualHeight, footerBottom=bounds.Bottom, attendees=attendee.IsVisible, column=Grid.GetColumn(attendee), nativeDpi=VisualTreeHelper.GetDpi(view).DpiScaleX });
                        foreach(var scale in new[]{1d,1.5d,2d}) Capture(view,label,scale);
                    }
                }
                File.WriteAllText(Path.Combine(Root,"metrics.json"),System.Text.Json.JsonSerializer.Serialize(Metrics,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
                personal=false;edit=false;window.Width=1120;window.Height=900;await Open();
                if(Environment.GetEnvironmentVariable("PR16_AUTO_EXIT")=="1") app.Shutdown();
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(Root,"error.txt"),ex.ToString()); if(Environment.GetEnvironmentVariable("PR16_AUTO_EXIT")=="1") app.Shutdown(1); }
        };
        app.Run(window);
    }
    static async System.Threading.Tasks.Task Open()
    {
        vm?.Dispose(); var client=Client();
        vm=new CalendarViewModel(client,Grants,personal:personal,projects:()=>[new(null,"Без проекта"),new(Guid.Parse("e4ca1c08-0d9f-4af3-a3cc-5f48461ec7cc"),"Личный проект")],employeeDirectory:DirectoryClient());
        await vm.ActivateAsync();
        if(edit) { var source=(DesktopCalendarEvent)client.GetType().GetField("Source")!.GetValue(client)!; await vm.OpenEventByIdAsync(source.Id); await vm.EditEventCommand.ExecuteAsync(); }
        else await vm.NewEventCommand.ExecuteAsync();
        vm.Editor!.Title="Рабочая встреча";
        host.Content=new CalendarView { DataContext=vm }; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
    }
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    { for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i);if(child is T result)yield return result;foreach(var nested in Descendants<T>(child))yield return nested; } }
    static void Capture(FrameworkElement view,string label,double scale)
    {
        Pump();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*scale),(int)Math.Ceiling(view.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(Root,$"{label}-{scale*100:0}pct.png"));encoder.Save(stream);
    }
}
