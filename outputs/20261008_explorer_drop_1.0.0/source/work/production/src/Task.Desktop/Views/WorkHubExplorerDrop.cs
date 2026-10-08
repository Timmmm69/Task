using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Views;

public partial class WorkHubView
{
    private ExplorerDropOverlay? _explorerOverlay, _folderOverlay;
    private IDataObject? _explorerData;
    private string[]? _explorerPaths;
    private bool _hasSupportedExplorerPath;
    private int _dragRevision;

    private string[]? ExplorerPaths(DragEventArgs e)
    {
        if (ReferenceEquals(e.Data, _explorerData)) return _explorerPaths;
        _explorerData = e.Data;
        try { _explorerPaths = e.Data.GetDataPresent(DataFormats.FileDrop, false) ? e.Data.GetData(DataFormats.FileDrop, false) as string[] : null; }
        catch { _explorerPaths = null; }
        _hasSupportedExplorerPath = _explorerPaths is { Length: > 0 and <= CatalogPathAddition.MaximumBatch }
            && _explorerPaths.Any(p => CatalogPathAddition.Normalize(p) is not null);
        return _explorerPaths;
    }

    private (Guid? Parent, bool Valid, TreeViewItem? Container) ExplorerTarget(DragEventArgs e)
    {
        if (DataContext is not WorkHubViewModel vm) return (null, false, null);
        var container = NodeContainer(e.OriginalSource);
        if (container?.DataContext is CatalogNodeViewModel node)
            return (node.Id, node.IsFolder && vm.CanDropCatalogPaths(node.Id), container);
        var root = e.OriginalSource is DependencyObject source && IsWithin(source, CatalogRootDropButton);
        var parent = root ? null : vm.CurrentCatalogFolder;
        return (parent, vm.CanDropCatalogPaths(parent), null);
    }

    private static bool IsWithin(DependencyObject source, DependencyObject target)
    {
        for (DependencyObject? current = source; current is not null; current = current is System.Windows.Media.Visual
            ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, target)) return true;
        return false;
    }

    private void ExplorerDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false))
        {
            if (e.Data.GetDataPresent(typeof(CatalogNodeViewModel))) return;
            _dragRevision++; e.Effects = DragDropEffects.None; e.Handled = true;
            ShowExplorerOverlay(ref _explorerOverlay, CatalogDropArea, false, "Поддерживаются только файлы и папки из Explorer.");
            RemoveOverlay(ref _folderOverlay); return;
        }
        _dragRevision++;
        var paths = ExplorerPaths(e); var target = ExplorerTarget(e);
        var accepted = target.Valid && _hasSupportedExplorerPath
            && (e.AllowedEffects & (DragDropEffects.Link | DragDropEffects.Copy)) != 0;
        // Copy effect is OLE feedback only. The application registers a reference; it copies no bytes.
        e.Effects = accepted ? (e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy) : DragDropEffects.None;
        e.Handled = true;
        ShowExplorerOverlay(ref _explorerOverlay, CatalogDropArea, accepted,
            accepted ? "Добавить ссылки: " + (target.Container?.DataContext is CatalogNodeViewModel n ? n.Name : "текущая папка")
                : "Добавление недоступно: проверьте цель, подключение и права (до 1000 элементов).");
        if (target.Container is { } container) ShowExplorerOverlay(ref _folderOverlay, container, accepted, "");
        else RemoveOverlay(ref _folderOverlay);
    }

    private static void ShowExplorerOverlay(ref ExplorerDropOverlay? overlay, UIElement target, bool accepted, string message)
    {
        if (overlay is not null && !ReferenceEquals(overlay.AdornedElement, target)) RemoveOverlay(ref overlay);
        if (overlay is null && AdornerLayer.GetAdornerLayer(target) is { } layer)
        { overlay = new(target) { IsHitTestVisible = false }; layer.Add(overlay); }
        if (overlay is null || overlay.Accepted == accepted && overlay.Message == message) return;
        overlay.Accepted = accepted; overlay.Message = message; overlay.InvalidateVisual();
    }
    private static void RemoveOverlay(ref ExplorerDropOverlay? overlay)
    {
        if (overlay is not null) AdornerLayer.GetAdornerLayer(overlay.AdornedElement)?.Remove(overlay);
        overlay = null;
    }
    private void ClearExplorerDrop()
    { _dragRevision++; RemoveOverlay(ref _explorerOverlay); RemoveOverlay(ref _folderOverlay); _explorerData = null; _explorerPaths = null; _hasSupportedExplorerPath = false; }
    private void ExplorerDragLeave(object sender, DragEventArgs e)
    {
        var revision = _dragRevision;
        // A sibling DragOver in the same dispatch invalidates this cleanup. Cancellation
        // and a real leave clear even if the cursor is still inside the area (Escape).
        Dispatcher.BeginInvoke(() => { if (revision == _dragRevision) ClearExplorerDrop(); }, System.Windows.Threading.DispatcherPriority.Background);
    }
    private async void ExplorerDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false))
        {
            if (!e.Data.GetDataPresent(typeof(CatalogNodeViewModel))) { e.Effects = DragDropEffects.None; e.Handled = true; }
            ClearExplorerDrop(); return;
        }
        var paths = ExplorerPaths(e); var target = ExplorerTarget(e);
        var accepted = target.Valid && _hasSupportedExplorerPath
            && (e.AllowedEffects & (DragDropEffects.Link | DragDropEffects.Copy)) != 0;
        e.Effects = accepted ? (e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy) : DragDropEffects.None;
        e.Handled = true; ClearExplorerDrop();
        if (accepted && paths is not null && DataContext is WorkHubViewModel vm)
            await vm.AddCatalogPathsAsync(paths, target.Parent);
    }
    private async void AddPickedFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkHubViewModel { CanAddCatalogPaths: true } vm) return;
        var dialog = new OpenFileDialog { Title = "Добавить файлы в каталог", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await vm.AddCatalogPathsAsync(dialog.FileNames, vm.CurrentCatalogFolder);
    }
    private async void AddPickedFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkHubViewModel { CanAddCatalogPaths: true } vm) return;
        var dialog = new OpenFolderDialog { Title = "Добавить ссылки на папки", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await vm.AddCatalogPathsAsync(dialog.FolderNames, vm.CurrentCatalogFolder);
    }
}
