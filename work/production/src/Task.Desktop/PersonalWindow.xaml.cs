using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using Task.Desktop.Modes;
using Task.Desktop.Personal;

namespace Task.Desktop;

public partial class PersonalWindow : Window
{
    public event Action? SwitchModeRequested;
    internal event Action? BackupRequested;
    internal event Action? RestoreRequested;
    private readonly PersonalApplicationModel _model;
    private readonly PersonalShellViewModel _shell;
    private bool _closed;
    private int _focusRegion;
    private readonly DispatcherTimer _schedulerTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PersonalWindowsNotifications? _windowsNotifications;
    private Notifications.WindowsToastPresenter? _toast;
    private bool _explicitExit;
    private bool? _autostartApplied;
    public PersonalWindow(PersonalApplicationModel model)
    {
        _model = model;
        _shell = new(model);
        if (model.Calendar is { } calendar)
            calendar.CreateTaskAtFreeTime = async (slot, token) =>
            {
                if (model.Tasks is not { } tasks || tasks.Editor is not null || !tasks.NewCommand.CanExecute(null)) return;
                Navigate("tasks");
                await tasks.NewCommand.ExecuteAsync(cancellationToken: token);
                tasks.Editor?.SeedCalendarSlot(slot);
            };
        InitializeComponent();
        DataContext = _shell;
        _shell.Palette.FocusRequested += OnPaletteFocus;
        _shell.Palette.Closed += OnPaletteClosed;
        _shell.InboxCaptureRequested += OnInboxCapture;
        _shell.PropertyChanged += OnRouteChanged;
        Loaded += async (_, _) =>
        {
            try
            {
                if (model.Tasks is { } tasks) await tasks.RefreshAsync();
                if (_closed || model.IsDisposed) return;
                if (model.Calendar is { } calendar) await calendar.ActivateAsync();
                if (_closed || model.IsDisposed) return;
                Reconcile(); _schedulerTimer.Start();
                if (Environment.GetCommandLineArgs().Contains("--background") && _windowsNotifications?.IsAvailable == true) Hide();
            }
            catch (OperationCanceledException) when (_closed || model.IsDisposed) { }
        };
        SourceInitialized += (_, _) =>
        {
            WindowsUxLayout.FitStartupWindowToPrimaryWorkArea(this);
            _windowsNotifications = new(this, () => { _explicitExit = true; Close(); }, "Task · Personal · работает в фоне");
            _toast = new(Dispatcher, ActivatePersonalNotification);
            _shell.BackgroundAvailable = _windowsNotifications.IsAvailable;
        };
        _schedulerTimer.Tick += (_, _) => Reconcile();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += (_, _) => { _closed = true; _schedulerTimer.Stop(); SystemEvents.PowerModeChanged -= OnPowerModeChanged; _toast?.Dispose(); _windowsNotifications?.Dispose(); _shell.PropertyChanged -= OnRouteChanged; _shell.Palette.FocusRequested -= OnPaletteFocus; _shell.Palette.Closed -= OnPaletteClosed; _shell.InboxCaptureRequested -= OnInboxCapture; _shell.Dispose(); };
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_explicitExit && !_model.IsDisposed && !Infrastructure.ViewState.IsCompletingClose(this) && _windowsNotifications?.IsAvailable == true)
        { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    internal void CloseForContextTransition() { _closed = true; _schedulerTimer.Stop(); _toast?.Dispose(); _explicitExit = true; Close(); }
    private void ActivatePersonalNotification(Guid id, string action)
    {
        if (_closed || _model.IsDisposed) return;
        try
        {
            if (action == "snooze") _model.Planning?.Snooze(id, useDefault: true);
            else if (action == "read") { _model.Store.MarkNotificationRead(id); _model.Planning?.Refresh(); }
            else { Show(); WindowState = WindowState.Normal; Activate(); _model.Planning?.OpenNotification(id); }
        }
        catch (Exception error) when (error is ArgumentException or PersonalTaskNotFoundException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        { _model.Planning?.ReportPresentationError("Действие не выполнено. Проверьте актуальность напоминания и повторите попытку."); }
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _ = Dispatcher.BeginInvoke(Reconcile);
    }
    private void Reconcile()
    {
        if (_model.IsDisposed) return;
        _model.Planning?.Reconcile();
        try
        {
            var preferences = _model.Store.WorkspaceNotificationPreferences();
            if (!Notifications.NotificationPresentationPolicy.Allows(preferences, DateTimeOffset.UtcNow)) _toast?.Clear();
            else _toast?.Reconcile(_model.Store.Notifications().Where(n => !n.IsRead).Select(n => n.Id).ToHashSet());
            var autostart = _model.Store.WorkspaceSettings().AutostartEnabled;
            if (_autostartApplied != autostart)
            {
                if (Notifications.WindowsAutostart.Apply(autostart)) _autostartApplied = autostart;
                else _model.Planning?.ReportPresentationError("Автозапуск не применён: проверьте доступ к настройкам Windows и расположение Task.");
            }
            foreach (var notification in _model.ClaimPresentations())
                _model.CompletePresentation(notification.Id, _toast?.Submit(new(notification.Id, notification.Title,
                    "Личное напоминание", notification.DueAt, _model.NotificationSound, CanSnooze: true)) == true);
        }
        catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException) { /* durable center is retained; retry next pass */ }
    }
    private void OnBackground(object sender, RoutedEventArgs e)
    {
        if (_windowsNotifications?.IsAvailable == true) Hide();
    }
    private void Navigate(string route) => _shell.SelectedSection = _shell.Sections.First(s => s.Route == route);
    private async void OnRouteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PersonalShellViewModel.SelectedSection) || _closed || _model.IsDisposed) return;
        try
        {
            // Project creation owns its refresh before opening the ordinary editor.
            if (_shell.IsTasks && !_shell.AddProjectTaskCommand.IsExecuting && _model.Tasks is { HasDrafts: false } tasks) await tasks.RefreshAsync();
            if (_closed || _model.IsDisposed) return;
            if (_shell.IsProjects || _shell.IsNotifications) _model.Planning?.Refresh();
            if (_shell.IsWorkspace) _model.Workspace?.Refresh();
            if (_shell.IsCalendar && _model.Calendar is { } calendar) await calendar.ActivateAsync();
        }
        catch (OperationCanceledException) when (_closed || _model.IsDisposed) { }
    }
    private FrameworkElement? _paletteReturnFocus;
    private void OpenPalette()
    {
        if (!_shell.Palette.IsOpen)
        {
            _paletteReturnFocus = Keyboard.FocusedElement as FrameworkElement;
            PaletteOverlay.Visibility = Visibility.Visible;
        }
        _shell.Palette.Open();
    }
    private void OnPaletteFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (_shell.Palette.IsOpen && !_closed) PaletteSurface.FocusQuery();
    });
    private void OnPaletteClosed()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        var target = _paletteReturnFocus; _paletteReturnFocus = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_closed) return;
            if (target is { IsVisible: true, IsEnabled: true }) target.Focus(); else SectionsList.Focus();
        });
    }
    private void OnPaletteBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PaletteOverlay)) _shell.Palette.Close();
    }
    private void OnPaletteSurface(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void OnInboxCapture() => Dispatcher.BeginInvoke(DispatcherPriority.Input,
        () => FocusCapture(TasksSurface));
    private static bool FocusCapture(DependencyObject root)
    {
        if (root is UIElement element && System.Windows.Automation.AutomationProperties.GetAutomationId(element) == "PersonalCaptureText") return element.Focus();
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FocusCapture(System.Windows.Media.VisualTreeHelper.GetChild(root, index))) return true;
        return false;
    }
    private void OnSearch(object sender, RoutedEventArgs e) => OpenPalette();
    private void OnNotifications(object sender, RoutedEventArgs e) => Navigate("notifications");
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { OpenPalette(); e.Handled = true; }
        else if (_shell.Palette.IsOpen) { if (e.Key == Key.Escape) { _shell.Palette.Close(); e.Handled = true; } return; }
        else if (e.Key == Key.Escape && _model.Tasks?.HasEditor == true) { TasksSurface.RequestCancel(); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Alt && _shell.IsTasks) { _model.Tasks?.NewCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F6 && _shell.CanNavigate)
        {
            _focusRegion = (_focusRegion + (Keyboard.Modifiers == ModifierKeys.Shift ? 2 : 1)) % 3;
            if (_focusRegion == 0) SectionsList.Focus();
            else (_focusRegion == 1 ? (UIElement)HeaderRegion : WorkspaceRegion).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
        }
    }
    private void OnSwitchMode(object sender, RoutedEventArgs e) => SwitchModeRequested?.Invoke();
    private void OnBackup(object sender, RoutedEventArgs e) => BackupRequested?.Invoke();
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
}
