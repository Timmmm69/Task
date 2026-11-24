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
    private ContextMenu? _folderMenu;

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
            if (_folderMenu is not null) _folderMenu.IsOpen = false;
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
        var list = (ListBox)sender;
        var row = ReferenceEquals(e.OriginalSource, list) ? list.SelectedItem : TaskRowAction.ResolveRow(list, e.OriginalSource as DependencyObject);
        if (e.Key == Key.Enter && row is MonthlyTaskRow task && TryOpen(task))
            e.Handled = true;
    }

    private async void OpenFolders(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MonthlyTaskRow row } button || DataContext is not MonthlyTasksViewModel vm) return;
        if (_folderMenu is not null) _folderMenu.IsOpen = false;
        var request = new MonthlyFolderRequest(row);
        if (!vm.FolderCommand.CanExecute(request)) return;
        button.IsEnabled = false;
        try
        {
            if (!await vm.FolderCommand.ExecuteAsync(request) || !button.IsVisible || !ReferenceEquals(button.DataContext, row)
                || !vm.FolderCommand.CanExecute(request) || vm.FolderChoices.Count == 0) return;
            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                MaxHeight = 320
            };
            foreach (var folder in vm.FolderChoices)
                menu.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = folder.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 },
                    Command = vm.FolderCommand,
                    CommandParameter = new MonthlyFolderRequest(row, folder.Id)
                });
            _folderMenu = menu;
            void CloseIfUnavailable(object? _, EventArgs args)
            {
                if (!vm.FolderCommand.CanExecute(request)) menu.IsOpen = false;
            }
            vm.FolderCommand.CanExecuteChanged += CloseIfUnavailable;
            menu.Closed += (_, _) =>
            {
                vm.FolderCommand.CanExecuteChanged -= CloseIfUnavailable;
                if (ReferenceEquals(_folderMenu, menu)) _folderMenu = null;
            };
            menu.IsOpen = true;
        }
        finally { button.IsEnabled = true; }
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
