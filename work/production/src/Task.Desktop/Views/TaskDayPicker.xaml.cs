using System.Windows;
using System.Windows.Controls;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class TaskDayPicker : UserControl
{
    public TaskDayPicker()
    {
        InitializeComponent();
        // Manual input belongs to the explicit raw text field. The DatePicker supplies the calendar only.
        Picker.Loaded += (_, _) =>
        {
            if (Picker.Template.FindName("PART_TextBox", Picker) is TextBox text) text.IsReadOnly = true;
        };
    }
    private void Today(object sender, RoutedEventArgs e) { if (DataContext is TaskCardEditor card) card.SelectRelativeDay(0); }
    private void Tomorrow(object sender, RoutedEventArgs e) { if (DataContext is TaskCardEditor card) card.SelectRelativeDay(1); }
}
