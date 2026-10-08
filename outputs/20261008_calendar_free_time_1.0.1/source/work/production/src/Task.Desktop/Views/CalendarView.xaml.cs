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
                CalendarTimelineScroll.ScrollToVerticalOffset(viewModel.InitialTimelineOffset);
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
            CalendarTimelineScroll.ScrollToVerticalOffset(Math.Max(0, slot.Top - 60));
            CalendarTimelineScroll.Focus();
            _slotHighlightTimer.Stop(); _slotHighlightTimer.Start();
        });
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalendarViewModel viewModel) viewModel.SelectedItem = null;
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

    private void OnCalendarPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CalendarViewModel viewModel) return;
        if (e.Key == Key.Escape && viewModel.ShowFreeTime)
        {
            _ = viewModel.CloseFreeTimeCommand.ExecuteAsync(); CalendarTimelineScroll.Focus(); e.Handled = true; return;
        }
        if (e.Key == Key.Escape && viewModel.Editor is null && viewModel.SelectedItem is not null)
        {
            viewModel.SelectedItem = null;
            e.Handled = true;
            return;
        }
        if (e.Key != Key.F6 || viewModel.Editor is not null) return;
        if (CalendarTimelineScroll.IsKeyboardFocusWithin)
        {
            if (CalendarEditEventButton.IsVisible && CalendarEditEventButton.IsEnabled)
                CalendarEditEventButton.Focus();
            else
                CalendarDatePicker.Focus();
        }
        else
        {
            CalendarTimelineScroll.Focus();
        }
        e.Handled = true;
    }
}
