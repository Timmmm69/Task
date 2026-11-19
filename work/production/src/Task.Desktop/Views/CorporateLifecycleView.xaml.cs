using System.Windows.Controls;

namespace Task.Desktop.Views;

public partial class CorporateLifecycleView : UserControl
{
    public CorporateLifecycleView() => InitializeComponent();

    private void LifecycleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: { } selected } list) return;
        list.ScrollIntoView(selected);
        DetailScroll?.ScrollToTop();
    }
}
