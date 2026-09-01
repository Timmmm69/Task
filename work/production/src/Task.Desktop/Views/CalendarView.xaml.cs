using System.Windows.Controls;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class CalendarView : UserControl
{
    public CalendarView() => InitializeComponent();

    private void OnDaySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CalendarViewModel viewModel
            && sender is ListBox { SelectedItem: CalendarItemViewModel item })
        {
            viewModel.SelectedItem = item;
        }
    }
}
