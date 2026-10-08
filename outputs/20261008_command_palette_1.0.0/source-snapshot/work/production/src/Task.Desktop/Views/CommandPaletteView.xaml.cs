using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView() => InitializeComponent();
    internal void FocusQuery() { QueryField.Focus(); QueryField.SelectAll(); }
    private void OnClose(object sender, RoutedEventArgs e) => (DataContext as CommandPaletteViewModel)?.Close();
    private void OnActivate(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, e.OriginalSource as DependencyObject) is ListBoxItem)
            (DataContext as CommandPaletteViewModel)?.ExecuteSelected();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel palette) return;
        switch (e.Key)
        {
            case Key.Escape: palette.Close(); break;
            case Key.Down: palette.MoveSelection(1); break;
            case Key.Up: palette.MoveSelection(-1); break;
            case Key.Enter when QueryField.IsKeyboardFocusWithin || EntriesList.IsKeyboardFocusWithin:
                palette.ExecuteSelected(); break;
            default: return;
        }
        if (palette.Selected is { } entry) EntriesList.ScrollIntoView(entry);
        e.Handled = true;
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Announce after the selection/position text binding has caught up.
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, () =>
        {
            if (IsVisible)
                (UIElementAutomationPeer.FromElement(SelectionStatus) ?? UIElementAutomationPeer.CreatePeerForElement(SelectionStatus))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
}
