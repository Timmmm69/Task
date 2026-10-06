using System.Windows;
using Task.Desktop.Modes;

namespace Task.Desktop;

public partial class ModeSelectorWindow : Window
{
    public ApplicationMode? SelectedMode { get; private set; }
    public ModeSelectorWindow() => InitializeComponent();
    private void OnPersonal(object sender, RoutedEventArgs e) => Select(ApplicationMode.Personal);
    private void OnCorporate(object sender, RoutedEventArgs e) => Select(ApplicationMode.Corporate);
    private void Select(ApplicationMode mode) { SelectedMode = mode; DialogResult = true; }
}
