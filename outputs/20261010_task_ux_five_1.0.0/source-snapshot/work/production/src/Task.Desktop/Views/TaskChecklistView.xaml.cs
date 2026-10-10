using System.Windows.Controls;
namespace Task.Desktop.Views;

public partial class TaskChecklistView : UserControl
{
    public TaskChecklistView() => InitializeComponent();
    private void OnToggle(object sender, System.Windows.RoutedEventArgs e)
    {
        // WPF toggles before invoking the command; immediately restore confirmed data.
        if (sender is CheckBox checkbox) checkbox.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
    }
}
