using System.Windows;
using Task.Desktop.Personal;
namespace Task.Desktop.Views;

public partial class PersonalProjectsView : System.Windows.Controls.UserControl
{
    public PersonalProjectsView() => InitializeComponent();
    private void OnEditorKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is FrameworkElement { IsVisible: true, IsKeyboardFocusWithin: true }
            && Task.Desktop.Infrastructure.FormKeyboard.IsShortcut(e.Key, System.Windows.Input.Keyboard.Modifiers, e.IsRepeat, false))
        {
            e.Handled = true;
            OnSave(sender, e);
        }
    }
    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalPlanningViewModel model || !model.CancelProjectCommand.CanExecute(null)) return;
        if (!PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Закрыть редактор проекта?", "Несохранённые изменения проекта будут отменены.", "Не сохранять")) return;
        model.CancelProjectCommand.Execute(null);
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalPlanningViewModel model || !model.SaveProjectCommand.CanExecute(null)) return;
        if (model.Lifecycle == "trashed" && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить проект в корзину?", "Проект можно будет восстановить из корзины.", "В корзину")) return;
        model.SaveProjectCommand.Execute(null);
    }
}
