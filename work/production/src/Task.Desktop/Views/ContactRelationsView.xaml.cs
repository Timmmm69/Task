using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Task.Desktop.Views;

public partial class ContactRelationsView : UserControl
{
    public ContactRelationsView() => InitializeComponent();

    private void OnRelationEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement editor || !editor.IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (editor.IsVisible)
            {
                editor.BringIntoView(new Rect(0, 0, editor.ActualWidth, 1));
            }
        }));
    }
}
