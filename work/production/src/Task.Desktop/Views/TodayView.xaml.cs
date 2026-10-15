using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class TodayView : UserControl
{
    private TodayViewModel? _observedViewModel;
    private UIElement? _actionFocus;

    public TodayView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel();
        Unloaded += (_, _) => ObserveViewModel(detach: true);
        Loaded += (_, _) => ObserveViewModel();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _observedViewModel?.CancelTransitionCommand.CanExecute(null) == true)
            {
                _observedViewModel.CancelTransitionCommand.Execute(null);
                e.Handled = true;
            }
        };
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DataContext is TodayViewModel viewModel)
                TimelineScrollViewer.ScrollToVerticalOffset(viewModel.InitialTimelineOffset);
        });
    }

    private void ObserveViewModel(bool detach = false)
    {
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= OnQuickActionChanged;
        _observedViewModel = detach ? null : DataContext as TodayViewModel;
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged += OnQuickActionChanged;
    }

    private void OnQuickActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observedViewModel is not { } vm) return;
        if (e.PropertyName == nameof(TodayViewModel.IsActionBusy) && vm.IsActionBusy && IsKeyboardFocusWithin)
            _actionFocus ??= Keyboard.FocusedElement as UIElement;
        if (e.PropertyName == nameof(TodayViewModel.IsTransitionConfirmationVisible)
            && vm.IsTransitionConfirmationVisible && IsKeyboardFocusWithin)
        {
            _actionFocus ??= Keyboard.FocusedElement as UIElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (vm.IsTransitionConfirmationVisible && TodayConfirmTransitionButton.IsEnabled)
                { TodayConfirmTransitionButton.BringIntoView(); TodayConfirmTransitionButton.Focus(); }
            });
        }
        if (e.PropertyName is nameof(TodayViewModel.IsActionBusy) or nameof(TodayViewModel.IsTransitionConfirmationVisible)
            && !vm.IsActionBusy && !vm.IsTransitionConfirmationVisible && _actionFocus is { } previous)
        {
            _actionFocus = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!IsVisible || (Keyboard.FocusedElement is UIElement focused && focused.IsVisible && focused.IsEnabled
                    && !IsKeyboardFocusWithin)) return;
                var target = previous.IsVisible && previous.IsEnabled ? previous
                    : TodayOpenSelectedButton.IsVisible && TodayOpenSelectedButton.IsEnabled ? TodayOpenSelectedButton
                    : (UIElement)TimelineScrollViewer;
                target.Focus();
            });
        }
    }
}
