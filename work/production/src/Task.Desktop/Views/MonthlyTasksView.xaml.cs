using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Task.Desktop.Infrastructure;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class MonthlyTasksView : UserControl
{
    private Window? _window;

    public MonthlyTasksView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null) _window.Activated += OnActivated;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null) _window.Activated -= OnActivated;
            _window = null;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
        };
    }

    private void OpenClickedRow(object sender, MouseButtonEventArgs e)
    {
        if (TaskRowAction.ResolveRow((ListBox)sender, e.OriginalSource as DependencyObject) is MonthlyTaskRow row && TryOpen(row))
            e.Handled = true;
    }

    private void OpenFocusedRow(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ((ListBox)sender).SelectedItem is MonthlyTaskRow row && TryOpen(row))
            e.Handled = true;
    }

    private bool TryOpen(MonthlyTaskRow row)
    {
        if (DataContext is not MonthlyTasksViewModel vm || !vm.OpenCommand.CanExecute(row)) return false;
        vm.OpenCommand.Execute(row);
        return true;
    }

    private void OnActivated(object? sender, EventArgs e) => RefreshUrgency();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _ = Dispatcher.BeginInvoke(RefreshUrgency);
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        _ = Dispatcher.BeginInvoke(RefreshUrgency);
    }

    private void RefreshUrgency()
    {
        if (IsVisible && DataContext is MonthlyTasksViewModel vm) vm.RefreshUrgency(force: true);
    }
}
