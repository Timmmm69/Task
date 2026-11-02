using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

/// <summary>Personal presentation only; scheduling and editor commands stay in CalendarViewModel.</summary>
public partial class PersonalCalendarView : UserControl
{
    public static readonly RoutedEvent OpenTaskRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(OpenTaskRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PersonalCalendarView));
    public static readonly RoutedEvent OpenRemindersRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(OpenRemindersRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PersonalCalendarView));
    public event RoutedEventHandler OpenTaskRequested { add => AddHandler(OpenTaskRequestedEvent, value); remove => RemoveHandler(OpenTaskRequestedEvent, value); }
    public event RoutedEventHandler OpenRemindersRequested { add => AddHandler(OpenRemindersRequestedEvent, value); remove => RemoveHandler(OpenRemindersRequestedEvent, value); }

    public static readonly DependencyProperty DayItemsProperty = DependencyProperty.Register(nameof(DayItems), typeof(IReadOnlyList<CalendarItemViewModel>), typeof(PersonalCalendarView), new PropertyMetadata(Array.Empty<CalendarItemViewModel>()));
    public static readonly DependencyProperty DayCaptionProperty = DependencyProperty.Register(nameof(DayCaption), typeof(string), typeof(PersonalCalendarView), new PropertyMetadata("Записи дня"));
    public IReadOnlyList<CalendarItemViewModel> DayItems { get => (IReadOnlyList<CalendarItemViewModel>)GetValue(DayItemsProperty); private set => SetValue(DayItemsProperty, value); }
    public string DayCaption { get => (string)GetValue(DayCaptionProperty); private set => SetValue(DayCaptionProperty, value); }
    private CalendarViewModel? _subscribed;
    private CalendarViewModel? Model => DataContext as CalendarViewModel;

    public PersonalCalendarView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Subscribe(); RefreshPresentation(); ScrollToTime(Model?.InitialTimelineOffset ?? 552); };
        Unloaded += (_, _) => Unsubscribe();
        DataContextChanged += (_, _) => { Unsubscribe(); if (IsLoaded) Subscribe(); RefreshPresentation(); };
    }

    private void Subscribe()
    {
        if (_subscribed == Model) return;
        Unsubscribe();
        _subscribed = Model;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnModelChanged;
    }
    private void Unsubscribe() { if (_subscribed is not null) _subscribed.PropertyChanged -= OnModelChanged; _subscribed = null; }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalendarViewModel.Days) or nameof(CalendarViewModel.SelectedItem) or nameof(CalendarViewModel.SelectedDate) or nameof(CalendarViewModel.ViewMode))
        {
            RefreshPresentation();
            if (e.PropertyName == nameof(CalendarViewModel.SelectedItem)) DetailsScroll.ScrollToTop();
        }
    }

    private void RefreshPresentation()
    {
        if (Body is null || Model is not { } vm) return;
        var date = DateOnly.FromDateTime(vm.SelectedDate ?? DateTime.Today);
        DayCaption = date.ToString("dddd, d MMMM", CultureInfo.GetCultureInfo("ru-RU"));
        DayItems = vm.Days.FirstOrDefault(d => d.Date == date)?.Items ?? [];
        DayAgenda.Visibility = vm.HasSelectedItem ? Visibility.Collapsed : Visibility.Visible;
        var lanes = vm.Days.SelectMany(d => d.TimelineItems).Select(i => i.TimelineLaneCount).DefaultIfEmpty(1).Max();
        TimelineGrid.MinWidth = vm.ViewMode == CalendarViewMode.Day ? lanes > 3 ? 58 + lanes * 150 : 0 : 850;
        UpdateLayoutMode();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();
    private void UpdateLayoutMode()
    {
        if (DetailsColumn is null) return;
        Page.Height = Math.Max(560, ActualHeight - 32);
        var stacked = ActualWidth < 1120;
        var compact = Body.ActualHeight < 420;
        DetailsColumn.Width = new GridLength(stacked ? 0 : 300);
        DetailsRow.Height = stacked ? new GridLength(compact ? 0.5 : 0.8, GridUnitType.Star) : new GridLength(0);
        AllDaySection.MaxHeight = compact ? 32 : 100;
        Grid.SetRow(DetailsPane, stacked ? 1 : 0);
        Grid.SetColumn(DetailsPane, stacked ? 0 : 1);
        DetailsPane.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(18, 0, 0, 0);
    }

    public void ScrollToTime(double offset, bool focus = false)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { TimelineScroll.ScrollToVerticalOffset(offset); if (focus) TimelineScroll.Focus(); });
    }
    public void CycleFocus()
    {
        if (TimelineScroll.IsKeyboardFocusWithin)
        {
            if (Model?.HasSelectedItem == true) BackToDayButton.Focus();
            else DetailsScroll.Focus();
        }
        else TimelineScroll.Focus();
    }
    public void FocusTimeline() => TimelineScroll.Focus();
    private void OnChooseDay(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CalendarDayViewModel day } && Model is { } vm)
        {
            vm.SelectedItem = null;
            vm.SelectedDate = day.Date.ToDateTime(TimeOnly.MinValue);
            RefreshPresentation();
        }
    }
    private async void OnOpenDay(object sender, RoutedEventArgs e)
    {
        OnChooseDay(sender, e);
        if (Model is { } vm) await vm.DayModeCommand.ExecuteAsync();
    }
    private void OnClearSelection(object sender, RoutedEventArgs e) { if (Model is { } vm) vm.SelectedItem = null; }
    private void OnOpenTask(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenTaskRequestedEvent));
    private void OnOpenReminders(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(OpenRemindersRequestedEvent));
}

public sealed class PersonalCalendarSelectedDateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is DateOnly date && values[1] is DateTime selected && date == DateOnly.FromDateTime(selected);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class PersonalCalendarValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (parameter as string) switch
    {
        "Preview" => value is IEnumerable<CalendarItemViewModel> items ? items.Take(3).ToArray() : [],
        "OverflowText" => value is int n ? $"Ещё {Math.Max(0, n - 3)} →" : "",
        "OverflowVisibility" => value is int n && n > 3 ? Visibility.Visible : Visibility.Collapsed,
        "Symbol" => value is true ? "○" : "□",
        "StatusSymbol" => (value as string) switch { "completed" => "✓ ", "cancelled" => "× ", _ => "" },
        "Conflict" => value is true ? "↔ " : "",
        "WeekOverflowText" => value is IEnumerable<CalendarTimelineItemViewModel> timeline ? $"Ещё {timeline.Count(i => i.TimelineLaneCount > 2 && i.TimelineLane > 0)} →" : "",
        "WeekOverflowVisibility" => value is IEnumerable<CalendarTimelineItemViewModel> rows && rows.Any(i => i.TimelineLaneCount > 2 && i.TimelineLane > 0) ? Visibility.Visible : Visibility.Collapsed,
        _ => value,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Busy week clusters keep one readable lane, with an explicit day overflow action.
/// Day mode retains every existing lane and allows horizontal scrolling.</summary>
public sealed class PersonalCalendarLaneConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values[0] is not double width || values[1] is not int lane || values[2] is not int count) return DependencyProperty.UnsetValue;
        var collapsed = values[3] is CalendarViewMode.Week && count > 2;
        if (parameter as string == "Visibility") return collapsed && lane > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (collapsed) { count = 1; lane = 0; }
        var column = Math.Max(0, width - 4) / Math.Max(1, count);
        return parameter as string == "Left" ? 2 + lane * column : Math.Max(0, column - 3);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PersonalCalendarLabelOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 3 && values[0] is double offset && values[1] is double top && values[2] is double height
            ? Math.Clamp(offset - top, 0, Math.Max(0, height - 38)) : 0d;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PersonalCalendarPeriodConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not CalendarItemViewModel item) return "";
        if (item.Source.StartAtUtc is not { } start || item.Source.EndAtUtc is not { } end || values[1] is not string zoneId) return item.TimeText;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        return localStart.Date == localEnd.Date ? item.TimeText
            : $"{localStart.ToString("d MMMM, HH:mm", CultureInfo.GetCultureInfo("ru-RU"))} —\n{localEnd.ToString("d MMMM, HH:mm", CultureInfo.GetCultureInfo("ru-RU"))}";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
