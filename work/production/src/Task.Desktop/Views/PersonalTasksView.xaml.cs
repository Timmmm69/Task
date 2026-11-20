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
            if (e.OldValue is PersonalTasksViewModel old)
            {
                old.PropertyChanged -= OnModelChanged;
                old.SavedTaskOpened -= OnSavedTaskOpened;
            }
            if (e.NewValue is PersonalTasksViewModel model)
            {
                model.PropertyChanged += OnModelChanged;
                model.SavedTaskOpened += OnSavedTaskOpened;
            }
        };
    }
    private void OnSavedTaskOpened() => Dispatcher.BeginInvoke(() =>
    {
        if (TaskBrowserSurface.IsVisible) TaskBrowserSurface.FocusDetails();
    });

    private bool _focusListAfterEditor;
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PersonalTasksViewModel.HasEditor) && sender is PersonalTasksViewModel model)
        {
            _focusListAfterEditor = !model.HasEditor;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsVisible) return;
                if (model.HasEditor) TitleInput.Focus();
                else ReturnToTaskList(model);
            });
        }
        if (e.PropertyName == nameof(PersonalTasksViewModel.IsBusy) && _focusListAfterEditor
            && sender is PersonalTasksViewModel { IsBusy: false, HasEditor: false } ready)
            Dispatcher.BeginInvoke(() => ReturnToTaskList(ready));
        if (e.PropertyName == nameof(PersonalTasksViewModel.DetailsExpanded) && sender is PersonalTasksViewModel { DetailsExpanded: true })
            Dispatcher.BeginInvoke(() => { if (!IsVisible) return; if (DataContext is PersonalTasksViewModel { IsTasks: true }) TaskBrowserSurface.FocusDetails(); else DetailsExpander.Focus(); });
    }
    private void ReturnToTaskList(PersonalTasksViewModel model)
    {
        // Save closes the editor before reloading the list. Wait until the refreshed row can receive focus.
        if (!_focusListAfterEditor || !IsVisible || model.IsBusy || model.HasEditor) return;
        _focusListAfterEditor = false;
        if (model.IsInbox) InboxSurface.FocusSelection();
        else if (model.IsTasks) TaskBrowserSurface.FocusSelection();
        else if (model.IsToday) TodaySurface.FocusSelection();
        else TasksList.Focus();
    }
    private void OnCancelEditor(object sender, RoutedEventArgs e) => RequestCancel();
    internal void RequestCancel()
    {
        if (DataContext is not PersonalTasksViewModel model || !model.CancelCommand.CanExecute(null)) return;
        if (model.Editor?.HasUnsavedChanges == true && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Отменить изменения?", "Введённые изменения задачи не будут сохранены.", "Не сохранять")) return;
        model.CancelCommand.Execute(null);
    }
}
