using System.Windows;
using Task.Desktop.ViewModels;

namespace Task.Desktop;

public partial class BootstrapWindow : Window
{
    public BootstrapWindow(BootstrapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
    }
}
