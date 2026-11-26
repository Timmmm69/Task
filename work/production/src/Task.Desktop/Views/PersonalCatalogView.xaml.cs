using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.Personal;
using Task.Desktop.Work;

namespace Task.Desktop.Views;

public partial class PersonalCatalogView : UserControl
{
    public PersonalCatalogView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
        BodyScroll.SizeChanged += (_, _) => UpdateLayoutMode();
        IsVisibleChanged += (_, _) => { if (IsVisible) Model?.ActivateCatalogBrowser(); };
        DataContextChanged += (_, _) => { if (IsVisible) Model?.ActivateCatalogBrowser(); };
    }
    private PersonalWorkspaceViewModel? Model => DataContext as PersonalWorkspaceViewModel;
    private void UpdateLayoutMode()
    {
        if (BodyScroll.ActualHeight <= 0) return;
        var narrow = ActualWidth < 880;
        BrowserGrid.Height = Math.Max(BodyScroll.ActualHeight, narrow ? 940 : 460);
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        BrowserGrid.RowDefinitions[0].Height = narrow ? new GridLength(360) : new GridLength(1, GridUnitType.Star);
        DetailsRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1); Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnReset(object sender, RoutedEventArgs e) => Model?.ResetCatalogFilters();
    private void OnRoot(object sender, RoutedEventArgs e) => Model?.BrowseCatalogFolder(null);
    private void OnUp(object sender, RoutedEventArgs e) => Model?.GoCatalogUp();
    private void OnEnterFolder(object sender, RoutedEventArgs e)
    {
        if (Model?.CatalogSelection is { IsFolder: true } row) Model.BrowseCatalogFolder(row.Id);
    }
    private async void OnCopyPath(object sender, RoutedEventArgs e)
    {
        var model = Model;
        if (model?.CatalogSelection is not { CopyAllowed: true, Path: { Length: > 0 } path } row) return;
        await Work.CatalogPathClipboard.CopyAsync(path,
            () => IsVisible && Model == model && model.CatalogSelection is { CopyAllowed: true } current && current.Id == row.Id && current.Path == path, model.SetCatalogMessage);
    }
    private void ActivateSelection()
    {
        if (Model is not { } model) return;
        if (model.CatalogSelection is { IsFolder: true } folder) model.BrowseCatalogFolder(folder.Id);
        else if (model.Hub.OpenFileCommand.CanExecute(null)) model.Hub.OpenFileCommand.Execute(null);
    }
    private void OnActivate(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(CatalogList, source) is ListBoxItem)
        { ActivateSelection(); e.Handled = true; }
    }
    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ActivateSelection(); e.Handled = true; }
        if (e.Key == Key.Back && Model?.CanGoCatalogUp == true) { Model.GoCatalogUp(); e.Handled = true; }
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        CreateSection.IsExpanded = true;
        DetailsPanel.BringIntoView(); NewName.BringIntoView(); NewName.Focus();
    }
    private async void OnAddFile(object sender, RoutedEventArgs e)
    {
        if (Model?.Hub is not { CanAddCatalogPaths: true } hub) return;
        var dialog = new OpenFileDialog { Title = "Добавить ссылки на файлы", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await hub.AddCatalogPathsAsync(dialog.FileNames, hub.CurrentCatalogFolder);
    }
    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        if (Model?.Hub is not { CanAddCatalogPaths: true } hub) return;
        var dialog = new OpenFolderDialog { Title = "Добавить ссылки на папки", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await hub.AddCatalogPathsAsync(dialog.FolderNames, hub.CurrentCatalogFolder);
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Model?.Hub.CanAddCatalogPaths == true && e.Data.GetDataPresent(DataFormats.FileDrop)
            && (e.AllowedEffects & (DragDropEffects.Link | DragDropEffects.Copy)) != 0
            ? e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Model?.Hub is not { CanAddCatalogPaths: true } hub || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await hub.AddCatalogPathsAsync(paths, hub.CurrentCatalogFolder);
    }
    private async void OnTrash(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || !model.TrashCommand.CanExecute(null)) return;
        if (model.Hub.ConfirmCatalogDelete && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить запись в корзину?", "Запись можно восстановить. Файлы на диске останутся.", "В корзину")) return;
        await model.TrashCommand.ExecuteAsync();
    }
}
