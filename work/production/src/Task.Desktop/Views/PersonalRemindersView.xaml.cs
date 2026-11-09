using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.Personal;

namespace Task.Desktop.Views;

public partial class PersonalRemindersView : System.Windows.Controls.UserControl
{
    public PersonalRemindersView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PersonalPlanningViewModel old) old.PropertyChanged -= OnModelChanged;
            if (e.NewValue is PersonalPlanningViewModel current) current.PropertyChanged += OnModelChanged;
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PersonalPlanningViewModel.HasNotificationSelection)) UpdateLayoutMode(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();
    private void UpdateLayoutMode()
    {
        if (BrowserGrid is null) return;
        var narrow = ActualWidth < 850;
        var selected = (DataContext as PersonalPlanningViewModel)?.HasNotificationSelection == true;
        var shortWindow = ActualHeight < 470;
        IntroPanel.Visibility = narrow || shortWindow ? Visibility.Collapsed : Visibility.Visible;
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(330);
        var singlePane = narrow && selected;
        DetailsRow.Height = new GridLength(0);
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1); Grid.SetRow(DetailsPanel, 0);
        ListRegion.Visibility = singlePane ? Visibility.Collapsed : Visibility.Visible;
        BackToList.Visibility = singlePane ? Visibility.Visible : Visibility.Collapsed;
        NotificationToolbar.Visibility = singlePane ? Visibility.Collapsed : Visibility.Visible;
        FiltersToggle.Visibility = shortWindow && !singlePane ? Visibility.Visible : Visibility.Collapsed;
        FiltersPanel.Visibility = !singlePane && (!shortWindow || FiltersToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        DetailHeading.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        BackToList.Margin = shortWindow ? new Thickness(0) : new Thickness(0, 0, 0, 10);
        DetailBody.Margin = shortWindow ? new Thickness(0, 8, 0, 8) : new Thickness(0, 16, 0, 12);
        DetailsPanel.Visibility = narrow && !selected ? Visibility.Collapsed : Visibility.Visible;
        DetailsPanel.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        DetailsPanel.Padding = shortWindow ? new Thickness(8) : narrow ? new Thickness(14) : new Thickness(20);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnFilterToggle(object sender, RoutedEventArgs e) => UpdateLayoutMode();
    private void OnBackToList(object sender, RoutedEventArgs e)
    { if (DataContext is PersonalPlanningViewModel model) { model.SelectedNotification = null; NotificationsList.Focus(); } }
    private void OnResetFilters(object sender, RoutedEventArgs e) => (DataContext as PersonalPlanningViewModel)?.ResetNotificationFilters();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PersonalPlanningViewModel model) return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        { CenterTabs.SelectedIndex = 0; if (ActualWidth < 850) model.SelectedNotification = null; NotificationSearch.Focus(); NotificationSearch.SelectAll(); e.Handled = true; }
        if (!NotificationSearch.IsKeyboardFocusWithin) return;
        if (e.Key == Key.Escape) { model.ResetNotificationFilters(); e.Handled = true; }
        else if (e.Key == Key.Down && model.VisibleNotifications.Count > 0)
        {
            NotificationsList.SelectedIndex = Math.Max(0, NotificationsList.SelectedIndex);
            NotificationsList.ScrollIntoView(NotificationsList.SelectedItem); NotificationsList.UpdateLayout();
            (NotificationsList.ItemContainerGenerator.ContainerFromItem(NotificationsList.SelectedItem) as ListBoxItem)?.Focus(); e.Handled = true;
        }
    }
    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PersonalPlanningViewModel model)
        { model.OpenNotificationCommand.Execute(null); e.Handled = true; }
    }
}
