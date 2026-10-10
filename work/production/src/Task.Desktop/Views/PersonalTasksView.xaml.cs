using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;
using Task.Desktop.Personal;
namespace Task.Desktop.Views;

public partial class PersonalTasksView : UserControl
{
    public PersonalTasksView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PersonalTasksViewModel old) old.PropertyChanged -= OnModelChanged;
            if (e.NewValue is PersonalTasksViewModel model) model.PropertyChanged += OnModelChanged;
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PersonalTasksViewModel.HasEditor) && sender is PersonalTasksViewModel model)
            Dispatcher.BeginInvoke(() => { if (!IsVisible) return; if (model.HasEditor) TitleInput.Focus(); else if (TasksList.IsEnabled) TasksList.Focus(); });
        if (e.PropertyName == nameof(PersonalTasksViewModel.DetailsExpanded) && sender is PersonalTasksViewModel { DetailsExpanded: true })
            Dispatcher.BeginInvoke(() => { if (IsVisible) DetailsExpander.Focus(); });
    }
    private void OnCancelEditor(object sender, RoutedEventArgs e) => RequestCancel();
    internal void RequestCancel()
    {
        if (DataContext is not PersonalTasksViewModel model || !model.CancelCommand.CanExecute(null)) return;
        if (model.Editor?.HasUnsavedChanges == true && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Отменить изменения?", "Введённые изменения задачи не будут сохранены.", "Не сохранять")) return;
        model.CancelCommand.Execute(null);
    }
}
