using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.Personal;

namespace Task.Desktop.Views;

public partial class PersonalSearchView : UserControl
{
    public PersonalSearchView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PersonalSearchViewModel old) old.PropertyChanged -= OnModelChanged;
            if (e.NewValue is PersonalSearchViewModel current) current.PropertyChanged += OnModelChanged;
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PersonalSearchViewModel.HasSelection)) UpdateLayoutMode(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();
    private void UpdateLayoutMode()
    {
        if (BrowserGrid is null) return;
        var narrow = ActualWidth < 820;
        IntroPanel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        var selected = (DataContext as PersonalSearchViewModel)?.HasSelection == true;
        DetailType.Visibility = DetailMeta.Visibility = DetailDivider.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        DetailTitle.TextWrapping = narrow ? TextWrapping.NoWrap : TextWrapping.Wrap;
        DetailTitle.TextTrimming = narrow ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        DetailTitle.FontSize = narrow ? 16 : 20;
        DetailTitle.Margin = narrow ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        DetailContext.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(310);
        DetailsRow.Height = narrow && selected ? new GridLength(160) : new GridLength(0);
        Grid.SetColumn(DetailsPanel, narrow ? 0 : 1); Grid.SetRow(DetailsPanel, narrow ? 1 : 0);
        DetailsPanel.Visibility = narrow && !selected ? Visibility.Collapsed : Visibility.Visible;
        DetailsPanel.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        DetailsPanel.Padding = narrow ? new Thickness(14) : new Thickness(20);
        ListRegion.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 20, 0);
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PersonalSearchViewModel model) return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { QueryInput.Focus(); QueryInput.SelectAll(); e.Handled = true; }
        if (!QueryInput.IsKeyboardFocusWithin) return;
        if (e.Key == Key.Enter) { model.SubmitCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { model.ClearCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Down && model.Results.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem); ResultsList.UpdateLayout();
            (ResultsList.ItemContainerGenerator.ContainerFromItem(ResultsList.SelectedItem) as ListBoxItem)?.Focus(); e.Handled = true;
        }
    }
    private void OnListKey(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && DataContext is PersonalSearchViewModel model) { model.OpenCommand.Execute(null); e.Handled = true; } }
}
