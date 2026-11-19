using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.Work;

namespace Task.Desktop.Views;

public partial class CorporateContactsView : UserControl
{
    public CorporateContactsView() => InitializeComponent();

    private void OpenCreate(object sender, RoutedEventArgs e)
    {
        CreatePanel.Visibility = Visibility.Visible;
        FocusAfterLayout(CreateFirstName);
    }

    private void CloseCreate(object sender, RoutedEventArgs e)
    {
        CreatePanel.Visibility = Visibility.Collapsed;
        FocusAfterLayout(NewContactButton);
    }

    private void FocusAfterLayout(FrameworkElement element) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (element.IsVisible && element.IsEnabled) Keyboard.Focus(element);
        });

    private void OnContactSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DesktopContact contact) return;
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is DesktopContact previous && previous.Id == contact.Id) return;
        CreatePanel.Visibility = Visibility.Collapsed;
        DetailScroll.ScrollToTop();
    }
}
