using System.Windows;
using Task.Desktop.ViewModels;

namespace Task.Desktop;

public partial class BootstrapWindow : Window
{
    public event Action? SwitchModeRequested;
    private void OnSwitchMode(object sender, RoutedEventArgs e) => SwitchModeRequested?.Invoke();
    public BootstrapWindow(BootstrapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
    }
}
