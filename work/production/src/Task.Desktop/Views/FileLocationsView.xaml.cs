using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Views;

public partial class FileLocationsView : UserControl
{
    public FileLocationsView() => InitializeComponent();
    private void PickFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileLocationsViewModel { PathEditable: true } vm) return;
        var picker = new OpenFileDialog { Title = "Выберите файл", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) vm.RawPath = picker.FileName;
    }
    private void PickFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileLocationsViewModel { PathEditable: true } vm) return;
        var picker = new OpenFolderDialog { Title = "Выберите папку", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) vm.RawPath = picker.FolderName;
    }
    private async void CopyPath(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileLocationsViewModel vm || sender is not Button
            { CommandParameter: DesktopFileLocation { CanOpenOnDevice: true, IsEnabled: true, RawPath: { } path } location }) return;
        await CatalogPathClipboard.CopyAsync(path,
            () => IsVisible && DataContext == vm && vm.Items.Contains(location), vm.SetMessage);
    }
}
