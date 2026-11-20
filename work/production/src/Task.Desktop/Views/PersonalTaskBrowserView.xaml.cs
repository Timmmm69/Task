using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        BrowserLayout.MinHeight = narrow ? 440 : 0;
        InspectorColumn.Width = new GridLength(narrow ? 0 : 340);
        InspectorRow.Height = !narrow ? new GridLength(0)
            : Inspector.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        BrowserGrid.RowDefinitions[0].Height = new GridLength(narrow && Inspector.IsExpanded ? 0.85 : 1, GridUnitType.Star);
        Grid.SetColumn(Inspector, narrow ? 0 : 1);
        Grid.SetRow(Inspector, narrow ? 1 : 0);
        Inspector.Margin = narrow ? new Thickness(0, 12, 16, 0) : new Thickness(0);
    }
    internal void FocusSelection() { if (IsVisible && BrowserList.IsEnabled) BrowserList.Focus(); }
    internal void FocusDetails()
    {
        Inspector.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        BrowserViewport.ScrollToBottom();
    }
    private void OnReset(object sender, RoutedEventArgs e) { if (DataContext is PersonalTasksViewModel model && model.CanSelectTask) model.ResetViewFilters(); }
}
