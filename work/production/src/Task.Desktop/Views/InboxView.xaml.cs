using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class InboxView : UserControl
{
    public InboxView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InboxViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is InboxViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is InboxViewModel viewModel) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InboxViewModel.Conversion) || sender is not InboxViewModel { Conversion: not null }) return;
        Dispatcher.BeginInvoke(() =>
        {
            InboxConversionTitleBox.Focus();
            InboxConversionTitleBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnInboxListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not InboxViewModel viewModel
            || !viewModel.ConvertCommand.CanExecute(viewModel.SelectedItem)) return;
        viewModel.ConvertCommand.Execute(viewModel.SelectedItem);
        e.Handled = true;
    }
}
