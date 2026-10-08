using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using Task.Desktop.ViewModels;

namespace Task.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _authenticationTransitionClose;
    private bool _modeTransitionClose;
    public event Action? SwitchModeRequested;
    private FrameworkElement? _overlayReturnFocus;
    private readonly Dictionary<Guid, IInputElement?> _completionReturnFocus = [];
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Palette.FocusRequested += OnPaletteFocusRequested;
        viewModel.Palette.Closed += OnPaletteClosed;
        viewModel.InboxCaptureRequested += OnInboxCaptureRequested;
        SourceInitialized += (_, _) => WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
        if (viewModel.Tasks is not null)
        {
            viewModel.Tasks.PropertyChanged += OnTasksPropertyChanged;
            viewModel.Tasks.CompletionPresented += OnCompletionPresented;
            viewModel.Tasks.CompletionPresentationEnded += OnCompletionPresentationEnded;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_modeTransitionClose)
        {
            base.OnClosing(e);
            return;
        }
        var viewModel = DataContext as MainWindowViewModel;
        var editor = viewModel?.Tasks?.Editor;
        var hasCalendarEditor = viewModel?.Calendar?.Editor is not null;
        var hasProjectEditor = viewModel?.Projects?.Editor is not null;
        if (ShouldCancelClose(
            _authenticationTransitionClose,
            editor?.HasUnsavedChanges == true || hasCalendarEditor || hasProjectEditor,
            () =>
        {
            var message = editor?.IsBusy == true
                ? "Сохранение ещё выполняется. Закрытие отменит ожидание ответа. Закрыть Task?"
                : hasCalendarEditor
                    ? "Форма события календаря открыта. Закрыть Task без сохранения?"
                    : hasProjectEditor
                        ? "Форма проекта открыта. Закрыть Task без сохранения?"
                    : "В форме задачи есть несохранённые изменения. Закрыть Task без сохранения?";
            return MessageBox.Show(message, "Несохранённые изменения", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    internal void CloseForAuthenticationTransition()
    {
        _authenticationTransitionClose = true;
        Close();
    }

    internal void CloseAfterModeSwitch()
    {
        _modeTransitionClose = true;
        Close();
    }

    private void OnSwitchMode(object sender, RoutedEventArgs e) => SwitchModeRequested?.Invoke();

    internal static bool ShouldCancelClose(
        bool authenticationTransition,
        bool hasUnsavedChanges,
        Func<bool> confirmClose) =>
        !authenticationTransition && hasUnsavedChanges && !confirmClose();

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel.Tasks is { } tasks)
        {
            tasks.PropertyChanged -= OnTasksPropertyChanged;
            tasks.CompletionPresented -= OnCompletionPresented;
            tasks.CompletionPresentationEnded -= OnCompletionPresentationEnded;
        }

        _viewModel.Palette.Close();
        _viewModel.Palette.FocusRequested -= OnPaletteFocusRequested;
        _viewModel.Palette.Closed -= OnPaletteClosed;
        _viewModel.InboxCaptureRequested -= OnInboxCaptureRequested;
        base.OnClosed(e);
    }

    private void OnCompletionPresented(Guid taskId)
    {
        var focused = Keyboard.FocusedElement;
        if (focused is null || ReferenceEquals(focused, this) || TasksList.IsKeyboardFocusWithin
            || focused is DependencyObject element && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == "ConfirmTransitionButton")
            _completionReturnFocus[taskId] = focused;
    }

    private void OnCompletionPresentationEnded(Guid taskId)
    {
        if (!_completionReturnFocus.Remove(taskId, out var origin)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsActive || !TasksList.IsVisible) return;
            // A user who moved to another control during the pulse keeps their focus.
            var focused = Keyboard.FocusedElement;
            if (!ShouldRestoreCompletionFocus(focused, origin, this)) return;
            if (TasksList.SelectedItem is { } selected)
            {
                TasksList.ScrollIntoView(selected);
                if (TasksList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem row) row.Focus();
                else TasksList.Focus();
            }
            else TasksRefreshButton.Focus();
        });
    }

    internal static bool ShouldRestoreCompletionFocus(IInputElement? focused, IInputElement? origin, IInputElement window) =>
        focused is null || ReferenceEquals(focused, origin) || ReferenceEquals(focused, window);

    private void OnTasksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TasksViewModel.IsActive) && sender is TasksViewModel { IsActive: false })
            _completionReturnFocus.Clear();
        if (e.PropertyName != nameof(TasksViewModel.Editor)
            || sender is not TasksViewModel { Editor: not null })
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TaskTitleTextBox.Focus());
    }

    private void OnTasksListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        TaskInspectorExpander.IsExpanded = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => TaskDetailsArea.Focus());
        e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandlePaletteShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (GlobalSearchOverlay.Visibility == Visibility.Visible)
            {
                CloseGlobalSearch();
                e.Handled = true;
                return;
            }
            if (NotificationOverlay.Visibility == Visibility.Visible)
            {
                CloseNotifications();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None
            && DataContext is MainWindowViewModel { Inbox.HasConversion: true } viewModel
            && viewModel.Inbox!.CancelConversionCommand.CanExecute(null))
        {
            viewModel.Inbox.CancelConversionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (GlobalSearchOverlay.Visibility == Visibility.Visible) return;
        if (e.Key != Key.F6 || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        FrameworkElement[] regions = [NavigationListBox, HeaderRegion, ContentRegion];
        var current = Array.FindIndex(regions, region => IsWithin(focused, region));

        for (var offset = 1; offset <= regions.Length; offset++)
        {
            var next = regions[(current + offset + regions.Length) % regions.Length];
            var focusedNext = ReferenceEquals(next, ContentRegion)
                ? FocusContentRegion()
                : ReferenceEquals(next, NavigationListBox)
                    ? NavigationListBox.Focus() || FocusFirstKeyboardTarget(NavigationListBox)
                    : FocusHeaderRegion();
            if (focusedNext)
            {
                e.Handled = true;
                return;
            }
        }
    }

    internal bool HandlePaletteShortcut(Key key, ModifierKeys modifiers)
    {
        if (key == Key.K && modifiers == ModifierKeys.Control) { OpenGlobalSearch(); return true; }
        return false;
    }
    private void OnOpenGlobalSearch(object sender, RoutedEventArgs e) => OpenGlobalSearch();

    private void OpenGlobalSearch()
    {
        if (DataContext is not MainWindowViewModel model) return;
        if (!model.Palette.IsOpen)
        {
            _overlayReturnFocus = Keyboard.FocusedElement as FrameworkElement ?? GlobalSearchButton;
            NotificationOverlay.Visibility = Visibility.Collapsed;
            GlobalSearchOverlay.Visibility = Visibility.Visible;
        }
        model.Palette.Open();
    }

    private void CloseGlobalSearch() => (DataContext as MainWindowViewModel)?.Palette.Close();
    private void OnPaletteClosed()
    {
        GlobalSearchOverlay.Visibility = Visibility.Collapsed;
        RestoreOverlayFocus(GlobalSearchButton);
    }
    private void OnPaletteFocusRequested() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if ((DataContext as MainWindowViewModel)?.Palette.IsOpen == true) PaletteSurface.FocusQuery();
    });
    private void OnInboxCaptureRequested() => Dispatcher.BeginInvoke(DispatcherPriority.Input,
        () => FocusAutomationTarget(ContentRegion, "InboxCaptureTextBox"));
    private static bool FocusAutomationTarget(DependencyObject root, string id)
    {
        if (root is UIElement element && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == id)
            return element.Focus();
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FocusAutomationTarget(System.Windows.Media.VisualTreeHelper.GetChild(root, index), id)) return true;
        return false;
    }
    private void OnGlobalSearchBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, GlobalSearchOverlay)) CloseGlobalSearch();
    }
    private void OnOverlaySurfaceClick(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private async void OnOpenNotifications(object sender, RoutedEventArgs e)
    {
        if (NotificationOverlay.Visibility == Visibility.Visible)
        {
            CloseNotifications();
            return;
        }
        if (DataContext is not MainWindowViewModel { WorkHub: { CanReadNotifications: true } } viewModel) return;
        CloseGlobalSearch();
        _overlayReturnFocus = Keyboard.FocusedElement as FrameworkElement ?? NotificationsButton;
        NotificationOverlay.Visibility = Visibility.Visible;
        await viewModel.WorkHub.EnsureNotificationsAsync();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => NotificationCenterList.Focus());
    }

    private void OnCloseNotifications(object sender, RoutedEventArgs e) => CloseNotifications();

    private void CloseNotifications()
    {
        NotificationOverlay.Visibility = Visibility.Collapsed;
        RestoreOverlayFocus(NotificationsButton);
    }

    private void OnNotificationBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, NotificationOverlay)) CloseNotifications();
    }

    private void OnNotificationItemAction(object sender, RoutedEventArgs e) => CloseNotifications();

    private void RestoreOverlayFocus(FrameworkElement fallback)
    {
        var target = _overlayReturnFocus;
        _overlayReturnFocus = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsVisible) return;
            if (target is { IsVisible: true, IsEnabled: true } && PresentationSource.FromVisual(target) == PresentationSource.FromVisual(this)) target.Focus();
            else fallback.Focus();
        });
    }

    private bool FocusContentRegion()
    {
        if (TasksList.IsVisible && TasksList.IsEnabled && TasksList.Focus())
        {
            return true;
        }

        return FocusFirstKeyboardTarget(ContentRegion);
    }

    private bool FocusHeaderRegion()
    {
        if (TasksRefreshButton.IsVisible && TasksRefreshButton.IsEnabled && TasksRefreshButton.Focus())
        {
            return true;
        }

        return FocusFirstKeyboardTarget(HeaderRegion);
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject region)
    {
        for (var current = element; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, region))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private static bool FocusFirstKeyboardTarget(DependencyObject root)
    {
        if (root is UIElement
            {
                Focusable: true,
                IsEnabled: true,
                IsVisible: true
            } target
            && KeyboardNavigation.GetIsTabStop(target)
            && target.Focus())
        {
            return true;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (FocusFirstKeyboardTarget(System.Windows.Media.VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }
}
