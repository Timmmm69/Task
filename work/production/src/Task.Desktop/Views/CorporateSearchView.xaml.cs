using System.Windows.Controls;
using System.Windows.Input;

namespace Task.Desktop.Views;

public partial class CorporateSearchView : UserControl
{
    public CorporateSearchView() => InitializeComponent();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Oem2 && Keyboard.Modifiers == ModifierKeys.None && !QueryInput.IsKeyboardFocusWithin)
            e.Handled = QueryInput.Focus();
    }
}
