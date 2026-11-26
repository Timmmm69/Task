using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Calendar;

public static class CalendarThoughtMenu
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(CalendarThoughtMenu), new PropertyMetadata(null, OnChanged));

    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement cell) return;
        cell.ContextMenuOpening -= OnOpening;
        if (e.NewValue is not ICommand command) { cell.ContextMenu = null; return; }
        var item = new MenuItem { Header = "Записать мысль", Command = command };
        AutomationProperties.SetAutomationId(item, "CalendarRecordThought");
        item.SetBinding(MenuItem.CommandParameterProperty, new Binding("DataContext") { Source = cell });
        cell.ContextMenu = new ContextMenu { Items = { item } };
        cell.ContextMenuOpening += OnOpening;
    }

    private static void OnOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement cell && e.OriginalSource is FrameworkElement source
            && source.DataContext is CalendarItemViewModel or CalendarTimelineItemViewModel)
            e.Handled = true;
    }
}
