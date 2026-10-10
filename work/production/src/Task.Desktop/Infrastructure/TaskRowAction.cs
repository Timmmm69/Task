using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Task.Desktop.Infrastructure;

/// <summary>Resolve the clicked row, never the potentially stale list selection.</summary>
public static class TaskRowAction
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(TaskRowAction), new PropertyMetadata(null, Attach));
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ListBox list) return;
        list.PreviewMouseLeftButtonDown -= OnMouse;
        if (e.NewValue is not null) list.PreviewMouseLeftButtonDown += OnMouse;
    }
    internal static object? ResolveRow(ListBox list, DependencyObject? source)
    {
        for (var node = source; node is not null && node != list; node = Parent(node))
        {
            if (node is ListBoxItem row)
                return ItemsControl.ItemsControlFromItemContainer(row) == list && row.IsEnabled ? row.DataContext : null;
            if (node is ButtonBase or TextBoxBase or Selector or ScrollBar or Thumb or Hyperlink
                || node is UIElement { Focusable: true }) return null;
        }
        return null;
    }
    private static DependencyObject? Parent(DependencyObject node) => node is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    private static void OnMouse(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left || sender is not ListBox { IsEnabled: true } list) return;
        if (ResolveRow(list, e.OriginalSource as DependencyObject) is not { } row || GetCommand(list) is not { } command
            || !command.CanExecute(row)) return;
        e.Handled = true;
        command.Execute(row);
    }
}
