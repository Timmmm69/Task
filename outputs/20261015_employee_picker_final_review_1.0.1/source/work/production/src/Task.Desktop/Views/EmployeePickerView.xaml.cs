using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class EmployeePickerView : UserControl
{
    public EmployeePickerView() => InitializeComponent();
    private EmployeePickerViewModel? Model => DataContext as EmployeePickerViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Model is { State: EmployeePickerState.Idle } model) model.SearchCommand.Execute(null);
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => Model?.Cancel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EmployeePickerViewModel previous) previous.Cancel();
        if (IsLoaded && Model is { State: EmployeePickerState.Idle } model) model.SearchCommand.Execute(null);
    }
    private void OnCancelClick(object sender, RoutedEventArgs e) => SearchBox.Focus();
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { } model) return;
        if (e.Key == Key.Escape)
        {
            model.Cancel(); SearchBox.Focus(); e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && model.Results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            ResultsList.UpdateLayout();
            if (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ResultsList.IsKeyboardFocusWithin)
        {
            model.ConfirmSelection(); e.Handled = true;
        }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            model.SearchCommand.Execute(null); e.Handled = true;
        }
    }
}
