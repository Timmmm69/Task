using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.Personal;

namespace Task.Desktop.Views;

public partial class PersonalContactsView : UserControl
{
    public PersonalContactsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
        BodyScroll.SizeChanged += (_, _) => UpdateLayoutMode();
        IsVisibleChanged += (_, _) => { if (IsVisible) Model?.ActivateContactsBrowser(); };
        DataContextChanged += (_, _) => { if (IsVisible) Model?.ActivateContactsBrowser(); };
    }
    private PersonalWorkspaceViewModel? Model => DataContext as PersonalWorkspaceViewModel;
    private void UpdateLayoutMode()
    {
        if (BodyScroll.ActualHeight <= 0) return;
        var narrow = ActualWidth < 820;
        BrowserGrid.Height = narrow ? double.NaN : Math.Max(BodyScroll.ActualHeight, 380);
        DetailsScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(340);
        BrowserGrid.RowDefinitions[0].Height = narrow ? new GridLength(320) : new GridLength(1, GridUnitType.Star);
        DetailsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1); Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnReset(object sender, RoutedEventArgs e) => Model?.ResetContactFilters();
    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        await model.Hub.RefreshCommand.ExecuteAsync();
        model.ActivateContactsBrowser();
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        model.IsContactCreating = true;
        DetailsScroll.ScrollToTop(); NewFirstName.BringIntoView(); NewFirstName.Focus();
    }
    private void OnCloseCreate(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) model.IsContactCreating = false;
        ContactSearch.Focus();
    }
    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || !model.Hub.CreateContactCommand.CanExecute(null)) return;
        var existingIds = model.Hub.Contacts.Select(c => c.Id).ToHashSet();
        await model.Hub.CreateContactCommand.ExecuteAsync();
        if (model.Hub.Contacts.FirstOrDefault(c => !existingIds.Contains(c.Id)) is not { } created) return;
        model.IsContactCreating = false;
        model.ResetContactFilters();
        model.ContactSelection = model.VisibleContacts.Single(c => c.Id == created.Id);
        ContactsList.ScrollIntoView(model.ContactSelection);
        ContactsList.Focus();
    }
    private async void OnTrash(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || !model.TrashCommand.CanExecute(null)) return;
        if (PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить контакт в корзину?", "Контакт и его связи можно восстановить из корзины.", "В корзину")) await model.TrashCommand.ExecuteAsync();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { ContactSearch.Focus(); ContactSearch.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { OnNew(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { OnRefresh(sender, e); e.Handled = true; }
    }
    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Model?.HasContactSelection != true) return;
        Model.IsContactCreating = false;
        DetailsPanel.BringIntoView();
        DetailsPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        e.Handled = true;
    }
}
