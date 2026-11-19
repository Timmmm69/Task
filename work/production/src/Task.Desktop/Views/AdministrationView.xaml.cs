using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Task.Desktop.Views;

public partial class AdministrationView : UserControl
{
    public AdministrationView() => InitializeComponent();

    private void OnEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) FocusWhenVisible(FirstNameField);
    }

    private void OnConfirmationVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (sender is not FrameworkElement { IsVisible: true } panel) return;
            panel.BringIntoView();
            var target = TransitionReasonField.IsVisible && TransitionReasonField.IsEnabled
                ? (Control)TransitionReasonField
                : ConfirmTransitionButton.IsEnabled ? ConfirmTransitionButton : CancelTransitionButton;
            target.Focus();
        }, DispatcherPriority.Input);
    }

    private void FocusWhenVisible(Control control)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (control.IsVisible && control.IsEnabled) control.Focus();
        }, DispatcherPriority.Input);
    }
}
