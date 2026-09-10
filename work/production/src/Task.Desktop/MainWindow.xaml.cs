using System.Windows;
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
