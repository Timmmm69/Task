using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Calendar;

namespace Task.Desktop.Views;

public partial class CalendarView : UserControl
{
    private readonly DispatcherTimer _currentTimeTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _slotHighlightTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private DateOnly? _revealedMonthDate;

    public CalendarView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is CalendarViewModel old) old.FreeTimeChosen -= OnFreeTimeChosen;
            if (IsLoaded && e.NewValue is CalendarViewModel current) current.FreeTimeChosen += OnFreeTimeChosen;
        };
        _slotHighlightTimer.Tick += (_, _) =>
        {
            _slotHighlightTimer.Stop();
            if (DataContext is CalendarViewModel current) current.ShowFreeTimeHighlight = false;
        };
        _currentTimeTimer.Tick += (_, _) =>
        {
            if (DataContext is CalendarViewModel viewModel) viewModel.RefreshCurrentTime();
        };
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DataContext is CalendarViewModel viewModel)
            {
                viewModel.FreeTimeChosen -= OnFreeTimeChosen;
                viewModel.FreeTimeChosen += OnFreeTimeChosen;
                if (viewModel.IsPersonal) PersonalCalendarSurface.ScrollToTime(viewModel.InitialTimelineOffset);
                else CalendarTimelineScroll.ScrollToVerticalOffset(viewModel.InitialTimelineOffset);
                viewModel.RefreshCurrentTime();
            }
            _currentTimeTimer.Start();
        });
        Unloaded += (_, _) =>
        {
            _currentTimeTimer.Stop(); _slotHighlightTimer.Stop();
            if (DataContext is CalendarViewModel viewModel) { viewModel.FreeTimeChosen -= OnFreeTimeChosen; viewModel.ShowFreeTimeHighlight = false; }
        };
    }

    private void OnFreeTimeChosen(FreeTimeSlot slot)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DataContext is CalendarViewModel { IsPersonal: true })
                PersonalCalendarSurface.ScrollToTime(Math.Max(0, slot.Top - 60), focus: true);
            else
            {
                CalendarTimelineScroll.ScrollToVerticalOffset(Math.Max(0, slot.Top - 60));
                CalendarTimelineScroll.Focus();
            }
            _slotHighlightTimer.Stop(); _slotHighlightTimer.Start();
        });
    }

    private void OnMonthVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) _revealedMonthDate = null;
    }

    private void OnMonthDateLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CalendarDayViewModel { IsSelectedDate: true } day } button
            || _revealedMonthDate == day.Date)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!button.IsVisible || !day.IsSelectedDate) return;
            _revealedMonthDate = day.Date;
            var rowTop = Math.Max(0, button.TranslatePoint(new Point(), CalendarMonthItems).Y - 4);
            var viewportTop = CalendarMonthScroll.VerticalOffset;
            const double dateAndFirstEntryHeight = 60;
            if (rowTop < viewportTop || rowTop + dateAndFirstEntryHeight > viewportTop + CalendarMonthScroll.ViewportHeight)
                CalendarMonthScroll.ScrollToVerticalOffset(rowTop);
        });
    }

    private void OnSelectDate(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalendarViewModel viewModel && sender is Button { DataContext: CalendarDayViewModel day })
            viewModel.SelectVisibleDate(day);
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CalendarViewModel viewModel) return;
        var restoreFocus = CalendarInspector.IsKeyboardFocusWithin;
        viewModel.SelectedItem = null;
        if (restoreFocus) FocusSchedule(viewModel);
    }

    private void FocusSchedule(CalendarViewModel viewModel)
    {
        if (viewModel.IsPersonal) PersonalCalendarSurface.FocusTimeline();
        else if (viewModel.IsMonthMode) CalendarMonthScroll.Focus();
        else CalendarTimelineScroll.Focus();
    }

    private void OnEditorTitleIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox title)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, title.Focus);
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not CalendarViewModel viewModel
            || !viewModel.CancelEditorCommand.CanExecute(null)) return;
        _ = viewModel.CancelEditorCommand.ExecuteAsync();
        e.Handled = true;
    }

    public void CyclePersonalFocus() => PersonalCalendarSurface.CycleFocus();

    private void OnCalendarPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CalendarViewModel viewModel) return;
        if (e.Key == Key.Escape && viewModel.ShowFreeTime)
        {
            _ = viewModel.CloseFreeTimeCommand.ExecuteAsync();
            FocusSchedule(viewModel);
            e.Handled = true; return;
        }
        if (e.Key == Key.Escape && viewModel.Editor is null && viewModel.SelectedItem is not null)
        {
            OnClearSelection(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.F6 || viewModel.Editor is not null) return;
        if (viewModel.IsPersonal) { PersonalCalendarSurface.CycleFocus(); e.Handled = true; return; }
        var schedule = viewModel.IsMonthMode ? CalendarMonthScroll : CalendarTimelineScroll;
        if (schedule.IsKeyboardFocusWithin)
        {
            if (CalendarEditEventButton.IsVisible && CalendarEditEventButton.IsEnabled)
                CalendarEditEventButton.Focus();
            else
                CalendarDatePicker.Focus();
        }
        else
        {
            schedule.Focus();
        }
        e.Handled = true;
    }
}
