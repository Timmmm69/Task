using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Calendar;

[Collection("Employee picker native WPF")]
public sealed partial class CalendarEmployeePickerTests
{
    [Fact]
    public async AsyncTask NativeCalendarUsesSharedPickerAndContactFallback()
    {
        if (Environment.GetEnvironmentVariable("TASK_CALENDAR_PICKER_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(typeof(CalendarEmployeePickerTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(CalendarEmployeePickerTests).FullName + "." + nameof(NativeCalendarUsesSharedPickerAndContactFallback));
            start.Environment["TASK_CALENDAR_PICKER_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            return;
        }
        var done = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources.Add("Task.CalendarLaneWidthConverter", new Task.Desktop.Converters.CalendarLaneWidthConverter());
            app.Resources.Add("Task.CalendarLaneLeftConverter", new Task.Desktop.Converters.CalendarLaneLeftConverter());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                try
                {
                    using var vm = new CalendarViewModel(new Client(), Grants, employeeDirectory: Directory()); await vm.ActivateAsync();
                    await vm.NewEventCommand.ExecuteAsync(); vm.Editor!.Title = "Рабочая встреча";
                    var view = new CalendarView { DataContext = vm };
                    window = new Window { Content = view, Width = 1120, Height = 900, Title = "Task · Calendar Picker test" };
                    window.Show(); window.Activate(); Pump();
                    var details = (StackPanel)view.FindName("CalendarEditorDetails");
                    var attendeePanel = (Border)view.FindName("CalendarEditorAttendees");
                    var actions = (Grid)view.FindName("CalendarEditorActions");
                    Assert.Equal(1, Grid.GetColumn(attendeePanel));
                    Assert.True(attendeePanel.TranslatePoint(new Point(), view).X > details.TranslatePoint(new Point(), view).X);
                    SaveFrame(view, "calendar-today-wide.png");
                    window.Width = 900; window.Height = 700; Pump();
                    Assert.Equal(0, Grid.GetColumn(attendeePanel)); Assert.Equal(1, Grid.GetRow(attendeePanel));
                    Assert.InRange(Math.Abs(details.ActualWidth - attendeePanel.ActualWidth), 0, 2);
                    var actionBounds = actions.TransformToAncestor(view).TransformBounds(new Rect(actions.RenderSize));
                    Assert.InRange(actionBounds.Bottom, 1, view.ActualHeight);
                    SaveFrame(view, "calendar-today-narrow.png");
                    window.Width = 1120; window.Height = 900; Pump();
                    Assert.Equal(1, Grid.GetColumn(attendeePanel));
                    // Exercise the custom DatePicker template through its native WPF parts.
                    var datePicker = Descendants<DatePicker>(view).Single(p =>
                        System.Windows.Automation.AutomationProperties.GetName(p) == "Дата события");
                    datePicker.BringIntoView(); Pump(); datePicker.ApplyTemplate();
                    Assert.IsType<System.Windows.Controls.Primitives.DatePickerTextBox>(datePicker.Template.FindName("PART_TextBox", datePicker));
                    var calendarButton = Assert.IsType<Button>(datePicker.Template.FindName("PART_Button", datePicker));
                    calendarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(datePicker.Template.FindName("PART_Popup", datePicker));
                    Assert.True(popup.IsOpen);
                    var originalDate = vm.Editor.Date;
                    var calendar = Assert.IsType<System.Windows.Controls.Calendar>(popup.Child);
                    calendar.SelectedDate = originalDate!.Value.AddDays(1); Pump();
                    Assert.Equal(originalDate.Value.AddDays(1), vm.Editor.Date);
                    popup.IsOpen = false; vm.Editor.Date = originalDate; Pump();
                    var allDay = Descendants<CheckBox>(view).Single(c =>
                        System.Windows.Automation.AutomationProperties.GetName(c) == "Событие на весь день");
                    var startTime = Descendants<TextBox>(view).Single(t =>
                        System.Windows.Automation.AutomationProperties.GetName(t) == "Время начала");
                    var originalTime = vm.Editor.StartTime;
                    allDay.IsChecked = true; Pump(); Assert.True(vm.Editor.IsAllDay); Assert.False(startTime.IsEnabled);
                    allDay.IsChecked = false; Pump(); Assert.True(startTime.IsEnabled); Assert.Equal(originalTime, startTime.Text);
                    var eventStatus = Descendants<ComboBox>(view).Single(c =>
                        System.Windows.Automation.AutomationProperties.GetName(c) == "Статус события");
                    eventStatus.BringIntoView(); Pump(); eventStatus.ApplyTemplate();
                    eventStatus.IsDropDownOpen = true; Pump();
                    Assert.True(Assert.IsType<System.Windows.Controls.Primitives.Popup>(eventStatus.Template.FindName("PART_Popup", eventStatus)).IsOpen);
                    eventStatus.IsDropDownOpen = false; Pump();
                    var picker = Descendants<EmployeePickerView>(view).Single(v => v.IsVisible);
                    Assert.Same(vm.Editor.AttendeePicker, picker.DataContext);
                    vm.Editor.IsSaving = true; Pump();
                    Assert.False(((TextBox)view.FindName("CalendarEditorTitle")).IsEnabled);
                    vm.Editor.IsSaving = false; Pump();
                    var search = (TextBox)picker.FindName("SearchBox"); search.BringIntoView(); Pump();
                    search.Text = "Иван"; await vm.Editor.AttendeePicker!.CurrentOperation; Pump();
                    var results = (ListBox)picker.FindName("ResultsList");
                    results.SelectedItem = Assert.Single(vm.Editor.AttendeePicker.Results); Pump();
                    var confirm = Descendants<Button>(picker).Single(b => new ButtonAutomationPeer(b).GetName() == "Подтвердить выбор сотрудника");
                    confirm.BringIntoView(); Pump();
                    confirm.Command.Execute(null); Pump(); Assert.Equal(Employee.Id, vm.Editor.AttendeePicker.SelectedUserAccountId);
                    SaveFrame(view, "calendar-employee-picker.png");
                    var add = Descendants<Button>(view).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "CalendarAddAttendee");
                    add.BringIntoView(); Pump(); Assert.True(add.IsEnabled); Assert.True(add.Focus());
                    await vm.Editor.AddAttendeeCommand.ExecuteAsync(); Pump(); Assert.Equal(Employee.Id, Assert.Single(vm.Editor.Attendees).Id);
                    SaveFrame(view, "calendar-added-employee.png");
                    var kind = Descendants<ComboBox>(view).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Тип участника");
                    kind.SelectedValue = false; Pump(); Assert.False(picker.IsVisible);
                    var uuid = Descendants<TextBox>(view).Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "CalendarAttendeeId");
                    Assert.True(uuid.IsVisible); uuid.Text = Employee.Id.ToString(); Pump();
                    await vm.Editor.AddAttendeeCommand.ExecuteAsync(); Assert.Equal(2, vm.Editor.Attendees.Count);
                    Assert.False(vm.Editor.Attendees[1].IsUser); SaveFrame(view, "calendar-contact.png");
                    kind.SelectedValue = true; Pump(); window.Width = 900; search.BringIntoView(); Pump();
                    Assert.True(picker.IsVisible); Assert.False(uuid.IsVisible); SaveFrame(view, "calendar-picker-narrow.png");
                    vm.UpdateCapabilities(["Calendar.Read", "CalendarEvent.Create"]); Pump();
                    Assert.False(picker.IsVisible); Assert.True(uuid.IsVisible); SaveFrame(view, "calendar-no-user-read.png");
                    vm.UpdateConnectivity(false); Pump(); Assert.False(add.IsEnabled);
                    done.SetResult();
                }
                catch (Exception error) { done.SetException(error); }
                finally { window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void SaveFrame(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_CALENDAR_PICKER_EVIDENCE") is not { Length: > 0 } directory) return;
        System.IO.Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        { context.DrawRectangle((Brush)view.FindResource("Task.Brush.Surface.Base"), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight)); context.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight)); }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
