using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using Task.Desktop.ViewModels;
using Task.Desktop.Modes;

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
    private FrameworkElement? _transitionReturnFocus;
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
        if (viewModel.Projects is { } projects)
            projects.ConfirmAction = (title, message, action) =>
                Personal.PersonalConfirmationDialog.Confirm(this, title, message, action);
        viewModel.Palette.FocusRequested += OnPaletteFocusRequested;
        viewModel.Palette.Closed += OnPaletteClosed;
        viewModel.InboxCaptureRequested += OnInboxCaptureRequested;
        SourceInitialized += (_, _) => WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
        if (viewModel.Tasks is not null)
        {
            viewModel.Tasks.PropertyChanged += OnTasksPropertyChanged;
            viewModel.Tasks.RowDetailsRequested += OnRowDetailsRequested;
            viewModel.Tasks.CompletionPresented += OnCompletionPresented;
            viewModel.Tasks.CompletionPresentationEnded += OnCompletionPresentationEnded;
        }
    }

    private bool _closeApproved;
    private bool _closingInProgress;
    internal Func<IReadOnlyList<string>, bool>? ConfirmCloseOverride { get; set; }
    internal Action? OperationPendingOverride { get; set; }
    internal Func<bool>? TrayAvailableOverride { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closingInProgress = true;
        try { CloseCore(e); }
        finally { _closingInProgress = false; }
    }

    private void CloseCore(CancelEventArgs e)
    {
        // Mandatory authentication teardown and an already prepared mode switch
        // must not retain access to the old context through a user confirmation.
        if (_authenticationTransitionClose || _modeTransitionClose)
        {
            base.OnClosing(e);
            return;
        }
        if (!_explicitExit && !_closeApproved && (TrayAvailableOverride?.Invoke() ?? _tray?.IsAvailable == true))
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
            return;
        }
        var canClose = CorporateCloseGuard.CanClose(
            () => _authenticationTransitionClose || _modeTransitionClose ? [] : ModeSwitchGuard.Inspect(_viewModel), _closeApproved,
            ConfirmCloseOverride ?? ConfirmDiscardOnClose, OperationPendingOverride ?? ShowPendingOperation);
        // A modal dialog pumps messages. A mandatory transition raised inside it
        // joins this close rather than recursively calling WPF's Close().
        if (_authenticationTransitionClose || _modeTransitionClose)
        {
            base.OnClosing(e);
            return;
        }
        if (!canClose)
        {
            _closeApproved = false;
            _explicitExit = false;
            if (Infrastructure.ViewState.IsCompletingClose(this)) IsEnabled = true;
            e.Cancel = true;
            return;
        }
        _closeApproved = true;
        base.OnClosing(e);
        // ViewState cancels once to flush local UI preferences and calls Close again.
        // Other cancellation must not authorize a later, unrelated user close.
        if (e.Cancel && (!Infrastructure.ViewState.IsClosePending(this)
            || Infrastructure.ViewState.IsCompletingClose(this)))
        {
            _closeApproved = false;
            _explicitExit = false;
            if (Infrastructure.ViewState.IsCompletingClose(this)) IsEnabled = true;
        }
    }

    private bool ConfirmDiscardOnClose(IReadOnlyList<string> names) => MessageBox.Show(this,
        $"Несохранённые изменения ({names.Count}):\n• {string.Join("\n• ", names)}\n\nПри выходе эти данные будут потеряны. Выйти без сохранения?",
        "Несохранённые изменения", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowPendingOperation() => MessageBox.Show(this,
        "Результат операции ещё не подтверждён. Дождитесь ответа сервера или проверьте исходный запрос в открытой форме, затем повторите выход.",
        "Выход из Task", MessageBoxButton.OK, MessageBoxImage.Information);

    internal void RequestExit()
    {
        if (_closingInProgress) return;
        _explicitExit = true;
        Close();
    }

    internal void CloseForAuthenticationTransition()
    {
        StopNotificationDelivery();
        _authenticationTransitionClose = true;
        if (!_closingInProgress) Close();
    }

    internal void CloseAfterModeSwitch()
    {
        StopNotificationDelivery();
        _modeTransitionClose = true;
        if (!_closingInProgress) Close();
    }

    private void OnSwitchMode(object sender, RoutedEventArgs e) => SwitchModeRequested?.Invoke();

    internal static bool ShouldCancelClose(
        bool authenticationTransition,
        bool hasUnsavedChanges,
        Func<bool> confirmClose) =>
        !authenticationTransition && hasUnsavedChanges && !confirmClose();

    protected override void OnClosed(EventArgs e)
    {
        DisposeNotifications();
        if (_viewModel.Tasks is { } tasks)
        {
            tasks.PropertyChanged -= OnTasksPropertyChanged;
            tasks.RowDetailsRequested -= OnRowDetailsRequested;
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
        if (e.PropertyName == nameof(TasksViewModel.IsTransitionConfirmationVisible)
            && sender is TasksViewModel tasks)
        {
            if (tasks.IsTransitionConfirmationVisible && TaskDetailsArea.IsKeyboardFocusWithin)
            {
                _transitionReturnFocus = Keyboard.FocusedElement as FrameworkElement;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (tasks.IsActive && tasks.IsTransitionConfirmationVisible
                        && TaskDetailsArea.IsVisible && TaskDetailsArea.IsKeyboardFocusWithin)
                        FocusAutomationTarget(TaskDetailsArea, "ConfirmTransitionButton");
                });
            }
            else if (!tasks.IsTransitionConfirmationVisible && _transitionReturnFocus is { } origin)
            {
                _transitionReturnFocus = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (!tasks.IsActive || tasks.IsTransitionConfirmationVisible || tasks.Editor is not null
                        || !TaskDetailsArea.IsVisible || !TaskDetailsArea.IsKeyboardFocusWithin) return;
                    // The originating action may disappear after a status/filter change.
                    if (origin.IsVisible && origin.IsEnabled) { origin.BringIntoView(); origin.Focus(); }
                    else
                    {
                        if (TasksList.SelectedItem is { } selected) TasksList.ScrollIntoView(selected);
                        TasksList.Focus();
                    }
                });
            }
        }
        if (e.PropertyName == nameof(TasksViewModel.IsActive) && sender is TasksViewModel { IsActive: false })
        {
            _completionReturnFocus.Clear();
            _transitionReturnFocus = null;
        }
        if (e.PropertyName == nameof(TasksViewModel.Editor) && sender is TasksViewModel { Editor: null })
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (TasksList.IsVisible && TasksList.IsEnabled) TasksList.Focus(); });
        if (e.PropertyName != nameof(TasksViewModel.Editor)
            || sender is not TasksViewModel { Editor: not null })
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TaskTitleTextBox.Focus());
    }

    private void OnRowDetailsRequested()
    {
        TaskInspectorExpander.IsExpanded = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TaskDetailsArea.Focus());
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
        if (root is FrameworkElement element && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == id)
        {
            if (!element.IsVisible || !element.IsEnabled) return false;
            element.BringIntoView();
            return element.Focus();
        }
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
