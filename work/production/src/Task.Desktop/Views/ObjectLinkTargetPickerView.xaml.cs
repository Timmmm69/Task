using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class ObjectLinkTargetPickerView : UserControl
{
    public ObjectLinkTargetPickerView() => InitializeComponent();
    private ObjectLinkTargetPickerViewModel? Model => DataContext as ObjectLinkTargetPickerViewModel;
    private void OnUnloaded(object sender, RoutedEventArgs e) => Model?.Cancel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObjectLinkTargetPickerViewModel previous) previous.Cancel();
    }
    private void OnStatusChanged(object sender, DataTransferEventArgs e)
    {
        if (sender is not TextBlock status || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, () =>
        {
            if (IsVisible)
                (UIElementAutomationPeer.FromElement(status) ?? UIElementAutomationPeer.CreatePeerForElement(status))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { } model) return;
        if (e.Key == Key.Escape) { model.Cancel(); SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && model.Results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem); ResultsList.UpdateLayout();
            if (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ResultsList.IsKeyboardFocusWithin)
        { model.ConfirmSelection(); e.Handled = true; }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        { model.SearchCommand.Execute(null); e.Handled = true; }
    }
}
