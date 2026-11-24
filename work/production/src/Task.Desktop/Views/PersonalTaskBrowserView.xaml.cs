using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using Task.Desktop.Personal;
namespace Task.Desktop.Views;

public partial class PersonalTaskBrowserView : UserControl
{
    public PersonalTaskBrowserView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateInspectorLayout();
        Inspector.Expanded += (_, e) => { if (ReferenceEquals(e.OriginalSource, Inspector)) UpdateInspectorLayout(); };
        Inspector.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, Inspector)) UpdateInspectorLayout(); };
    }

    private void UpdateInspectorLayout()
    {
        var narrow = ActualWidth < 850;
        // A short, stacked layout needs one scroll surface so the full card can be reached.
        if (narrow)
            BindingOperations.ClearBinding(BrowserLayout, HeightProperty);
        else if (!BindingOperations.IsDataBound(BrowserLayout, HeightProperty))
            BrowserLayout.SetBinding(HeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = BrowserViewport });
        InspectorScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        InspectorColumn.Width = new GridLength(narrow ? 0 : 340);
        InspectorRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        BrowserGrid.RowDefinitions[0].Height = narrow ? new GridLength(220) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(Inspector, narrow ? 0 : 1);
        Grid.SetRow(Inspector, narrow ? 1 : 0);
        Inspector.Margin = narrow ? new Thickness(0, 12, 16, 0) : new Thickness(0);
    }
    internal void FocusSelection() { if (IsVisible && BrowserList.IsEnabled) BrowserList.Focus(); }
    internal void FocusDetails()
    {
        Inspector.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Inspector.BringIntoView(new Rect(0, 0, 1, 32));
    }
    private void OnReset(object sender, RoutedEventArgs e) { if (DataContext is PersonalTasksViewModel model && model.CanSelectTask) model.ResetViewFilters(); }
}
