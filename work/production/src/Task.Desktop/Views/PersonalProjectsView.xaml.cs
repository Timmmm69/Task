using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Personal;
namespace Task.Desktop.Views;

public partial class PersonalProjectsView : System.Windows.Controls.UserControl
{
    public PersonalProjectsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateBrowserLayout();
        BodyScroll.SizeChanged += (_, _) => UpdateBrowserLayout();
    }
    private void UpdateBrowserLayout()
    {
        if (BodyScroll.ActualHeight <= 0) return;
        var narrow = ActualWidth < 900;
        // Give both virtualizing lists a finite viewport, even when a short
        // window needs to scroll the combined list/details workspace.
        BrowserGrid.Height = Math.Max(BodyScroll.ActualHeight, narrow ? 800 : 520);
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(1.3, GridUnitType.Star);
        DetailsRow.Height = narrow ? new GridLength(1.6, GridUnitType.Star) : new GridLength(0);
        BrowserGrid.RowDefinitions[0].Height = new GridLength(narrow ? 0.7 : 1, GridUnitType.Star);
        ProjectDescriptionReader.MaxHeight = narrow ? 90 : 130;
        ProjectInfoScroll.MaxHeight = narrow ? 230 : 310;
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1);
        Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (DataContext is PersonalPlanningViewModel model) model.ResetProjectFilters();
    }
    private void OnEditorKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is FrameworkElement { IsVisible: true, IsKeyboardFocusWithin: true }
            && Task.Desktop.Infrastructure.FormKeyboard.IsShortcut(e.Key, System.Windows.Input.Keyboard.Modifiers, e.IsRepeat, false))
        {
            e.Handled = true;
            OnSave(sender, e);
        }
    }
    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalPlanningViewModel model || !model.CancelProjectCommand.CanExecute(null)) return;
        if (!PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Закрыть редактор проекта?", "Несохранённые изменения проекта будут отменены.", "Не сохранять")) return;
        model.CancelProjectCommand.Execute(null);
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PersonalPlanningViewModel model || !model.SaveProjectCommand.CanExecute(null)) return;
        if (model.Lifecycle == "trashed" && !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить проект в корзину?", "Проект можно будет восстановить из корзины.", "В корзину")) return;
        model.SaveProjectCommand.Execute(null);
    }
}
