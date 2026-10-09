using System.IO;
using System.Windows;
using System.Windows.Threading;
using Task.Desktop.Personal;
using Task.Desktop.Work;

namespace Task.Desktop;

public partial class MainWindow
{
    private PersonalWindowsNotifications? _tray;
    private Notifications.WindowsToastPresenter? _toast;
    private Notifications.CorporateNotificationAgent? _notificationAgent;
    private Notifications.NotificationPresentationJournal? _notificationJournal;
    private readonly DispatcherTimer _notificationTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource _notificationPass = new();
    private bool _notificationClosed, _explicitExit, _notificationBusy;
    private Work.DesktopWorkApiClient? _notificationClient;
    private bool? _autostartApplied;
    internal void ConfigureNotifications(DesktopWorkApiClient client, string dataDirectory, string context)
    {
        _notificationClient = client;
        SourceInitialized += (_, _) =>
        {
            _tray = new(this, () => { _explicitExit = true; Close(); }, "Task · Corporate · работает в фоне");
            _toast = new(Dispatcher, ActivateCorporateNotification);
            try
            {
                _notificationJournal = new(Path.Combine(dataDirectory, "notification-presentations"), context);
                var hub = _viewModel.WorkHub!;
                _notificationAgent = new(client, _toast, _notificationJournal,
                    () => hub.BackgroundNotificationAccess, () => hub.BackgroundSettingsAccess, hub.ApplyBackgroundNotifications, canSnooze: () => hub.CanManageCorporateReminders);
                hub.NotificationAccessChanged += OnNotificationAccessChanged;
                hub.UserSettingsApplied += ApplyAutostart;
                _notificationTimer.Tick += OnNotificationTick;
                _notificationTimer.Start();
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
            { _viewModel.ReportNotificationError("Windows-уведомления недоступны: проверьте доступ к локальному журналу. Центр уведомлений доступен."); }
        };
        Loaded += OnNotificationTick;
        Loaded += (_, _) => { if (Environment.GetCommandLineArgs().Contains("--background") && _tray?.IsAvailable == true) Hide(); };
    }
    private async void OnNotificationTick(object? sender, EventArgs e)
    {
        if (_notificationClosed || _notificationBusy || _notificationAgent is null || _notificationClient is null) return;
        _notificationBusy = true;
        var ct = _notificationPass.Token;
        try
        {
            await _notificationAgent.RunPassAsync(ct);
            if (!_notificationClosed && _viewModel.WorkHub?.BackgroundSettingsAccess == true)
            {
                var settings = await _notificationClient.GetUserSettingsAsync(ct);
                if (!_notificationClosed && !ct.IsCancellationRequested && settings is DesktopWorkResult<DesktopUserSettings>.Succeeded ok) ApplyAutostart(ok.Value);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        { _viewModel.ReportNotificationError("Не удалось показать Windows-уведомление. Проверьте локальные настройки; история доступна в центре."); }
        finally { _notificationBusy = false; }
    }
    private void ApplyAutostart(DesktopUserSettings settings)
    {
        if (_autostartApplied == settings.AutostartEnabled) return;
        if (!Notifications.WindowsAutostart.Apply(settings.AutostartEnabled))
            _viewModel.ReportNotificationError("Автозапуск не применён: проверьте доступ к настройкам Windows и расположение Task.");
        else _autostartApplied = settings.AutostartEnabled;
    }
    private void OnNotificationAccessChanged()
    {
        if (_viewModel.WorkHub?.BackgroundNotificationAccess != true || _viewModel.WorkHub.BackgroundSettingsAccess != true)
        {
            _notificationPass.Cancel(); _notificationPass.Dispose(); _notificationPass = new();
            _toast?.Clear();
        }
    }
    private async void ActivateCorporateNotification(Guid id, string action)
    {
        if (_notificationClosed || _notificationClient is null || _viewModel.WorkHub is not { BackgroundNotificationAccess: true } hub) return;
        var ct = _notificationPass.Token;
        try
        {
            // Re-read with server authorization. Never act on toast text or a cached source.
            var result = await _notificationClient.GetNotificationAsync(id, ct);
            if (_notificationClosed || ct.IsCancellationRequested || !hub.BackgroundNotificationAccess) return;
            if (result is not DesktopWorkResult<DesktopNotification>.Succeeded ok)
            { _toast?.Clear(); return; }
            if (action == "read")
            {
                await hub.MarkReadCommand.ExecuteAsync(ok.Value, ct);
                return;
            }
            if (action == "snooze") { await hub.SnoozeNotificationCommand.ExecuteAsync(ok.Value, ct); return; }
            Show(); WindowState = WindowState.Normal; Activate();
            await hub.OpenNotificationSourceCommand.ExecuteAsync(ok.Value, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private void DisposeNotifications()
    {
        if (_notificationClosed) return;
        _notificationClosed = true; _notificationTimer.Stop(); _notificationTimer.Tick -= OnNotificationTick;
        Loaded -= OnNotificationTick;
        if (_viewModel.WorkHub is { } hub) { hub.NotificationAccessChanged -= OnNotificationAccessChanged; hub.UserSettingsApplied -= ApplyAutostart; }
        _notificationPass.Cancel(); _notificationAgent?.Dispose(); _toast?.Dispose(); _tray?.Dispose();
        _notificationJournal?.Dispose(); _notificationPass.Dispose();
    }
    internal void StopNotificationDelivery() => DisposeNotifications();
}
