using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Personal;

namespace Task.Desktop.Views;

public partial class PersonalLifecycleView : UserControl
{
    public PersonalLifecycleView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
        BodyScroll.SizeChanged += (_, _) => UpdateLayoutMode();
    }
    private PersonalWorkspaceViewModel? Model => DataContext as PersonalWorkspaceViewModel;
    private void UpdateLayoutMode()
    {
        if (BodyScroll.ActualHeight <= 0) return;
        var narrow = ActualWidth < 820;
        BrowserGrid.Height = narrow ? double.NaN : Math.Max(BodyScroll.ActualHeight, 300);
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(330);
        BrowserGrid.RowDefinitions[0].Height = narrow ? new GridLength(340) : new GridLength(1, GridUnitType.Star);
        DetailsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        DetailsScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1); Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnReset(object sender, RoutedEventArgs e) => Model?.ResetLifecycleFilters();
    private async void OnTrash(object sender, RoutedEventArgs e)
    {
        if (Model is not { Selected: { } selected } model || !model.TrashCommand.CanExecute(null)) return;
        if ((selected.ObjectType != "catalog_item" || model.Hub.ConfirmCatalogDelete) &&
            !PersonalConfirmationDialog.Confirm(Window.GetWindow(this), "Переместить в корзину?", $"«{selected.Title}»\nЗапись можно восстановить. Файлы на диске останутся.", "В корзину")) return;
        if (model.Selected == selected && model.TrashCommand.CanExecute(null)) await model.TrashCommand.ExecuteAsync();
    }
    private async void OnPurge(object sender, RoutedEventArgs e)
    {
        if (Model is not { Selected: { } selected } model || !model.PurgeCommand.CanExecute(null)) return;
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Owner = owner,
            Title = "Удалить навсегда?",
            Width = 490,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
            FontFamily = owner.FontFamily,
            FontSize = 14,
            Name = "LifecycleDeleteConfirmation",
            MaxHeight = Math.Max(360, owner.ActualHeight)
        };
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "Удалить навсегда?", Style = (Style)FindResource("Personal.Airbnb.Heading") });
        body.Children.Add(new TextBlock { Text = selected.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 12) });
        body.Children.Add(new TextBlock { Text = "Запись и её связи в приложении нельзя будет восстановить. Файлы на диске останутся.", TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true, IsDefault = true, Style = (Style)FindResource("Personal.Airbnb.Compact"), Margin = new Thickness(0, 0, 10, 0) };
        var confirm = new Button { Content = "Удалить навсегда", Style = (Style)FindResource("Management.Danger"), Name = "ConfirmDelete" };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm); body.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Loaded += (_, _) => cancel.Focus();
        if (dialog.ShowDialog() == true && model.Selected == selected && model.PurgeCommand.CanExecute(null)) await model.PurgeCommand.ExecuteAsync();
    }
}
