using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Personal;
namespace Task.Desktop.Views;

public partial class PersonalWorkspaceView : UserControl
{
    public event RoutedEventHandler? BackupRequested;
    public event RoutedEventHandler? RestoreRequested;
    public event RoutedEventHandler? BackgroundRequested;
    public PersonalWorkspaceView() => InitializeComponent();
    private void OnBackup(object sender, RoutedEventArgs e) => BackupRequested?.Invoke(this, e);
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, e);
    private void OnBackground(object sender, RoutedEventArgs e) => BackgroundRequested?.Invoke(this, e);
    private async void OnTrash(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalWorkspaceViewModel model || !model.TrashCommand.CanExecute(null)) return;
        if ((model.Selected?.ObjectType != "catalog_item" || model.Hub.ConfirmCatalogDelete) && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить запись в корзину?", "Запись можно восстановить. Файлы на диске останутся.", "В корзину")) return;
        await model.TrashCommand.ExecuteAsync();
    }
    private async void OnPurge(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalWorkspaceViewModel model || !model.PurgeCommand.CanExecute(null)) return;
        if (PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Удалить запись навсегда?", "Запись и её связи будут удалены без возможности восстановления. Файлы на диске останутся.", "Удалить навсегда")) await model.PurgeCommand.ExecuteAsync();
    }
}
