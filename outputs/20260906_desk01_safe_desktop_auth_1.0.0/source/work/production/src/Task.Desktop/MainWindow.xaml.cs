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
        if (ShouldCancelClose(
            _authenticationTransitionClose,
            editor?.HasUnsavedChanges == true || hasCalendarEditor,
            () =>
        {
            var message = editor?.IsBusy == true
                ? "Сохранение ещё выполняется. Закрытие отменит ожидание ответа. Закрыть Task?"
                : hasCalendarEditor
                    ? "Форма события календаря открыта. Закрыть Task без сохранения?"
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
}
