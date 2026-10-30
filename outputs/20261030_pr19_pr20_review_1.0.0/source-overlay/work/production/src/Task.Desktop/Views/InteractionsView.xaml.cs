using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

namespace Task.Desktop.Views;

public partial class InteractionsView : UserControl
{
    public InteractionsView() => InitializeComponent();

    private void WorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // This view is also embedded in company/contact cards: use its own width.
        var compact = e.NewSize.Width < 900;
        ListColumn.Width = new GridLength(compact ? 1 : 42, GridUnitType.Star);
        DetailColumn.Width = compact ? new GridLength(0) : new GridLength(58, GridUnitType.Star);
        Grid.SetColumn(DetailRegion, compact ? 0 : 1);
        Grid.SetRow(DetailRegion, compact ? 1 : 0);
        ListRegion.Margin = compact ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 16, 0);
    }
}

public sealed class InteractionSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is Guid id && values[1] is Guid selected && id == selected;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
