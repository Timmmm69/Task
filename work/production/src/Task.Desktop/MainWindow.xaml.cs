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
    private FrameworkElement? _overlayReturnFocus;

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
        if (viewModel.Tasks is not null)
        {
            viewModel.Tasks.PropertyChanged += OnTasksPropertyChanged;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
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

    internal static bool ShouldCancelClose(
        bool authenticationTransition,
        bool hasUnsavedChanges,
        Func<bool> confirmClose) =>
        !authenticationTransition && hasUnsavedChanges && !confirmClose();

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel { Tasks: not null } viewModel)
        {
            viewModel.Tasks.PropertyChanged -= OnTasksPropertyChanged;
        }

        base.OnClosed(e);
    }

    private void OnTasksPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenGlobalSearch();
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
                    : FocusFirstKeyboardTarget(next);
            if (focusedNext)
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnOpenGlobalSearch(object sender, RoutedEventArgs e) => OpenGlobalSearch();

    private void OpenGlobalSearch()
    {
        if (DataContext is not MainWindowViewModel { WorkHub: { CanSearch: true } }) return;
        _overlayReturnFocus = Keyboard.FocusedElement as FrameworkElement ?? GlobalSearchButton;
        NotificationOverlay.Visibility = Visibility.Collapsed;
        GlobalSearchOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            GlobalSearchOverlayTextBox.Focus();
            GlobalSearchOverlayTextBox.SelectAll();
        });
    }

    private void OnCloseGlobalSearch(object sender, RoutedEventArgs e) => CloseGlobalSearch();

    private void CloseGlobalSearch()
    {
        GlobalSearchOverlay.Visibility = Visibility.Collapsed;
        RestoreOverlayFocus(GlobalSearchButton);
    }

    private void OnGlobalSearchBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, GlobalSearchOverlay)) CloseGlobalSearch();
    }

    private void OnOverlaySurfaceClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnGlobalSearchTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseGlobalSearch();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Down or Key.Up) || GlobalSearchResultsList.Items.Count == 0) return;
        GlobalSearchResultsList.SelectedIndex = e.Key == Key.Down ? 0 : GlobalSearchResultsList.Items.Count - 1;
        GlobalSearchResultsList.ScrollIntoView(GlobalSearchResultsList.SelectedItem);
        GlobalSearchResultsList.Focus();
        if (GlobalSearchResultsList.ItemContainerGenerator.ContainerFromItem(GlobalSearchResultsList.SelectedItem) is ListBoxItem item) item.Focus();
        e.Handled = true;
    }

    private void OnGlobalSearchResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelectedSearchResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseGlobalSearch();
            e.Handled = true;
        }
    }

    private void OnSearchResultActivate(object sender, MouseButtonEventArgs e) => ActivateSelectedSearchResult();

    private void ActivateSelectedSearchResult()
    {
        if (DataContext is not MainWindowViewModel { WorkHub: { } workHub } || workHub.SelectedSearchHit is null) return;
        if (workHub.OpenSearchResultCommand.CanExecute(workHub.SelectedSearchHit))
            workHub.OpenSearchResultCommand.Execute(workHub.SelectedSearchHit);
        CloseGlobalSearch();
    }

    private void OnShowAllSearchResults(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        CloseGlobalSearch();
        viewModel.SelectedSection = viewModel.Sections.First(section => section.Route == "search");
    }

    private async void OnOpenNotifications(object sender, RoutedEventArgs e)
    {
        if (NotificationOverlay.Visibility == Visibility.Visible)
        {
            CloseNotifications();
            return;
        }
        if (DataContext is not MainWindowViewModel { WorkHub: { CanReadNotifications: true } } viewModel) return;
        _overlayReturnFocus = Keyboard.FocusedElement as FrameworkElement ?? NotificationsButton;
        GlobalSearchOverlay.Visibility = Visibility.Collapsed;
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
            if (target is { IsVisible: true, IsEnabled: true }) target.Focus();
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
