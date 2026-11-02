using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Task.Desktop.Infrastructure;

/// <summary>Presentation-only action; TextBox.Clear preserves its Text binding and undo history.</summary>
public static class SearchFieldCommands
{
    public static RoutedUICommand Clear { get; } = new("Очистить поиск", nameof(Clear), typeof(SearchFieldCommands));

    static SearchFieldCommands()
    {
        CommandManager.RegisterClassCommandBinding(typeof(TextBox), new CommandBinding(Clear,
            (sender, args) => { ((TextBox)sender).Clear(); ((TextBox)sender).Focus(); args.Handled = true; },
            (sender, args) =>
            {
                var input = (TextBox)sender;
                args.CanExecute = input.IsEnabled && !input.IsReadOnly && input.Text.Length > 0;
                args.Handled = true;
            }));
        EventManager.RegisterClassHandler(typeof(TextBox), TextBox.TextChangedEvent,
            new TextChangedEventHandler((_, _) => CommandManager.InvalidateRequerySuggested()));
    }
}
