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

        // Let disabled action buttons finish transferring focus before scrolling.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (editor.IsVisible)
            {
                editor.BringIntoView();
            }
        }));
    }
}
