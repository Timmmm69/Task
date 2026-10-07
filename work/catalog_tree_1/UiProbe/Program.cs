using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.Personal;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        var data = Path.Combine(evidence, "personal-ui-data-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Task.Desktop;component/Resources/Theme.xaml") });
            app.Resources["Task.BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            using var store = new PersonalTaskStore(new PersonalDataPaths(data));
            var a = store.CreateCatalog("Рабочие документы", "virtual_folder", null);
            var nested = store.CreateCatalog("Договоры", "virtual_folder", null, a.Id);
            var b = store.CreateCatalog("Входящие", "virtual_folder", null);
            var file = store.CreateCatalog("Договор поставки.docx", "file_reference", null, nested.Id);
            using var vm = new WorkHubViewModel(new PersonalWorkClient(store), ["FileCatalog.Read", "FileCatalog.Create", "FileCatalog.Update", "FileReference.Open", "FileLocation.Update"], personal: true);
            vm.Activate(WorkHubArea.Catalog);
            var view = new WorkHubView { DataContext = vm, Width = 1100, Height = 760, Background = (Brush)app.FindResource("Task.Brush.Surface.Base"), Foreground = (Brush)app.FindResource("Task.Brush.Text.Primary") };
            view.Measure(new Size(1100, 760)); view.Arrange(new Rect(0, 0, 1100, 760)); Pump(view);
            var tree = Find<TreeView>(view, "CatalogList");
            Require(tree.Items.Count == 2, "WPF roots");
            var first = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(vm.CatalogRoots.Single(n => n.Id == a.Id));
            first.IsExpanded = true; Pump(view);
            var child = (TreeViewItem)first.ItemContainerGenerator.ContainerFromIndex(0); child.IsExpanded = true; Pump(view);
            var leaf = (TreeViewItem)child.ItemContainerGenerator.ContainerFromIndex(0); leaf.IsSelected = true; Pump(view);
            Require(vm.SelectedCatalogItem?.Id == file.Id, "WPF nested selection -> ViewModel");
            Require(vm.CatalogRoots.Single(n => n.Id == a.Id).IsExpanded && vm.CatalogRoots.Single(n => n.Id == a.Id).Children[0].IsExpanded, "Two-way expansion");
            var destination = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(vm.CatalogRoots.Single(n => n.Id == b.Id));
            Drop(destination, vm.SelectedCatalogItem!, DragDrop.DragOverEvent);
            Drop(destination, vm.SelectedCatalogItem!, DragDrop.DropEvent); Pump(view);
            Require(vm.SelectedCatalogItem?.ParentId == b.Id && vm.SelectedCatalogItem.Version == 2, "Nested WPF drop -> storage move and fresh version");
            var rootButton = Find<Button>(view, "CatalogRootButton");
            Drop(rootButton, vm.SelectedCatalogItem!, DragDrop.DropEvent); Pump(view);
            Require(vm.SelectedCatalogItem?.ParentId is null && vm.SelectedCatalogItem?.Version == 3, "Root WPF drop -> storage move");
            Require(vm.SelectCatalogItemAsync(nested.Id).GetAwaiter().GetResult(), "Reveal folder"); Pump(view);
            vm.NewItemName = "Новый договор";
            Find<Button>(view, "CreateCatalogItemButton").Command.Execute(null); Pump(view);
            Require(vm.SelectedCatalogItem?.ParentId == nested.Id, "WPF create button -> selected folder");
            rootButton.Command.Execute(null); Pump(view);
            Require(vm.SelectedCatalogItem is null, "WPF root button clears nested selection");
            vm.NewItemName = "Корневая папка";
            Find<Button>(view, "CreateCatalogFolderButton").Command.Execute(null); Pump(view);
            Require(vm.SelectedCatalogItem is { ParentId: null, IsFolder: true }, "WPF create folder in root");
            Require(vm.SelectCatalogItemAsync(file.Id).GetAwaiter().GetResult(), "Reveal moved file"); Pump(view);
            var bitmap = new RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            using var output = File.Create(Path.Combine(evidence, "catalog-tree-wpf.png"));
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(output);
            Console.WriteLine("PASS: WPF tree, nested selection, expansion, drag/drop to folder, drop to root, create in folder, version refresh, rendered screenshot.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS: " + label); }
    private static void Pump(FrameworkElement view) { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && AutomationProperties.GetAutomationId(match) == id) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { try { return Find<T>(VisualTreeHelper.GetChild(root, i), id); } catch (KeyNotFoundException) { } }
        throw new KeyNotFoundException(id);
    }
    private static void Drop(UIElement target, CatalogNodeViewModel source, RoutedEvent routedEvent)
    {
        var data = new DataObject(typeof(CatalogNodeViewModel), source);
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(1, 1)], null)!;
        args.RoutedEvent = routedEvent; target.RaiseEvent(args);
        if (routedEvent == DragDrop.DragOverEvent) Require(args.Effects == DragDropEffects.Move, "WPF drop feedback");
    }
}
