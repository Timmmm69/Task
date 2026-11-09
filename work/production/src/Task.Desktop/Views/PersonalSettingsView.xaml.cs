using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Task.Desktop.Views;

public partial class PersonalSettingsView : UserControl
{
    public event RoutedEventHandler? BackupRequested;
    public event RoutedEventHandler? RestoreRequested;
    public event RoutedEventHandler? BackgroundRequested;
    public PersonalSettingsView()
    {
        InitializeComponent();
        // Conversion errors leave the previous numeric model value intact. Do not save that
        // previous value while an invalid draft is still displayed in the form.
        Validation.AddErrorHandler(this, (_, _) => Dispatcher.BeginInvoke(UpdateInputValidation));
        SettingsTabs.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateInputValidation);
    }
    private void UpdateInputValidation()
    {
        var invalid = HasInvalidInput(this);
        // The two sections have independent persistence commands and validation scopes.
        if (SettingsTabs.SelectedIndex == 0) SaveProfile.IsEnabled = !invalid;
        if (SettingsTabs.SelectedIndex == 1) SaveNotifications.IsEnabled = !invalid;
        InputError.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
    }
    private static bool HasInvalidInput(DependencyObject parent)
    {
        if (Validation.GetHasError(parent)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (HasInvalidInput(VisualTreeHelper.GetChild(parent, i))) return true;
        return false;
    }
    private void OnBackup(object sender, RoutedEventArgs e) => BackupRequested?.Invoke(this, e);
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, e);
    private void OnBackground(object sender, RoutedEventArgs e) => BackgroundRequested?.Invoke(this, e);
}
