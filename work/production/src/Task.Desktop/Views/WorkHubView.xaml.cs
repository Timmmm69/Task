using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class WorkHubView : UserControl
{
    public WorkHubView() { InitializeComponent(); Unloaded += (_, _) => ClearExplorerDrop(); DataContextChanged += (_, _) => ClearExplorerDrop(); }

    private Point _dragStart;
    private CatalogNodeViewModel? _dragNode;
    private static TreeViewItem? NodeContainer(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null && element is not TreeViewItem)
        {
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return element as TreeViewItem;
    }
    private void CatalogSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WorkHubViewModel vm && e.NewValue is CatalogNodeViewModel node) vm.SelectedCatalogItem = node;
    }

    private async void CatalogSearchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is WorkHubViewModel { IsCatalogSearchActive: true } vm &&
            CatalogResults.SelectedItem is CatalogNodeViewModel node && vm.SelectedCatalogItem != node)
            await vm.SelectCatalogItemAsync(node.Id);
    }

    private void ClearCatalogSearch(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkHubViewModel vm) vm.CatalogSearchQuery = string.Empty;
    }

    private void CatalogSearchShortcut(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == "/" && CatalogSearchBox is { IsVisible: true, IsEnabled: true } &&
            Keyboard.FocusedElement is not TextBoxBase)
            e.Handled = CatalogSearchBox.Focus();
    }

    private void ShowCatalogCreation(object sender, RoutedEventArgs e)
    {
        CatalogCreationPanel.Visibility = Visibility.Visible;
        CatalogInspector.ScrollToTop();
        if (!CatalogPickFile.Focus()) CatalogNameInput.Focus();
    }

    private void HideCatalogCreation(object sender, RoutedEventArgs e)
    {
        CatalogCreationPanel.Visibility = Visibility.Collapsed;
        if (!CatalogCreateButton.Focus()) CatalogSearchBox.Focus();
    }

    private void CatalogDetailsUpdated(object sender, DataTransferEventArgs e)
    {
        CatalogInspector?.ScrollToTop();
    }
    private void CatalogMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragNode = NodeContainer(e.OriginalSource)?.DataContext as CatalogNodeViewModel;
    }
    private void CatalogMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode is not { } node || DataContext is not WorkHubViewModel vm) return;
        var offset = e.GetPosition(this) - _dragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragNode = null;
        if (vm.CatalogMoveDestinations.Any(d => vm.CanMoveCatalogItem(node.Id, d.Id)))
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CatalogNodeViewModel), node), DragDropEffects.Move);
    }
    private CatalogMoveRequest? DropRequest(DragEventArgs e, bool root)
    {
        if (DataContext is not WorkHubViewModel vm || e.Data.GetData(typeof(CatalogNodeViewModel)) is not CatalogNodeViewModel node) return null;
        var target = NodeContainer(e.OriginalSource)?.DataContext as CatalogNodeViewModel;
        if (!root && target is not { IsFolder: true }) return null;
        Guid? parentId = root ? null : target!.Id;
        return vm.CanMoveCatalogItem(node.Id, parentId) ? new(node.Id, parentId) : null;
    }
    private void CatalogDragOver(object sender, DragEventArgs e) => SetDropEffect(e, false);
    private void CatalogRootDragOver(object sender, DragEventArgs e) => SetDropEffect(e, true);
    private void SetDropEffect(DragEventArgs e, bool root)
    {
        e.Effects = DropRequest(e, root) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }
    private void CatalogDrop(object sender, DragEventArgs e) => ExecuteDrop(e, false);
    private void CatalogRootDrop(object sender, DragEventArgs e) => ExecuteDrop(e, true);
    private void ExecuteDrop(DragEventArgs e, bool root)
    {
        if (DropRequest(e, root) is { } request && DataContext is WorkHubViewModel vm) vm.MoveCatalogItemCommand.Execute(request);
        e.Handled = true;
    }
}
