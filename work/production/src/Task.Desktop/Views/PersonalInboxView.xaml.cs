using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.Personal;

namespace Task.Desktop.Views;

public partial class PersonalInboxView : UserControl
{
    public PersonalInboxView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 800;
            InspectorColumn.Width = new GridLength(narrow ? 0 : 340);
            InspectorRow.Height = narrow ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
            BrowserGrid.RowDefinitions[0].Height = new GridLength(narrow ? 0.8 : 1, GridUnitType.Star);
            Inspector.Padding = new Thickness(narrow ? 12 : 20);
            Grid.SetColumn(Inspector, narrow ? 0 : 1);
            Grid.SetRow(Inspector, narrow ? 1 : 0);
            Inspector.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
            ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
        };
    }
    internal void FocusSelection()
    {
        if (!IsVisible || !InboxList.IsEnabled) return;
        if (InboxList.SelectedItem is { } item)
        {
            InboxList.ScrollIntoView(item);
            InboxList.UpdateLayout();
            if (InboxList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row) { row.Focus(); return; }
        }
        InboxList.Focus();
    }
    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (DataContext is PersonalTasksViewModel model) model.InboxSearchText = "";
        SearchInput.Focus();
    }
    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat
            || DataContext is not PersonalTasksViewModel model || !model.EditCommand.CanExecute(null)) return;
        e.Handled = true;
        await model.EditCommand.ExecuteAsync();
    }
}
