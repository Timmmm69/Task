using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Task.Application.Inventory;
using Async = System.Threading.Tasks;

namespace Task.Desktop.Inventory;

public partial class InventoryView : UserControl
{
    private const string Page = "https://inventory.vorava.local/materials.html";
    private const string Resource = "Task.Desktop.Inventory.MaterialInventory.html";
    private const int MaxMessageLength = 32 * 1024 * 1024;
    private static readonly Regex ExportName = new(
        @"\A(?:sklad-backup-\d{4}-\d{2}-\d{2}\.json|sklad-ostatki-\d{4}-\d{2}-\d{2}\.csv)\z",
        RegexOptions.CultureInvariant);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _starting;
    private bool _closed;

    public InventoryView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsVisible) await OpenAsync();
    }

    private async void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded || !IsVisible || _closed) return;
        if (Browser.CoreWebView2 is { Source: Page } core)
            core.PostWebMessageAsJson("""{"refresh":true}""");
        else
            await OpenAsync();
    }

    private async Async.Task OpenAsync()
    {
        if (_starting || _closed || Browser.CoreWebView2 is not null || DataContext is not IDesktopInventoryClient) return;
        _starting = true;
        try
        {
            var root = Environment.GetEnvironmentVariable("TASK_DESKTOP_DATA_DIRECTORY");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Task");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "InventoryWebView"));
            if (_closed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (_closed) return;

            var core = Browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 is unavailable.");
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, args) => args.Cancel = !string.Equals(args.Uri, Page, StringComparison.Ordinal);
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += ServePage;
            core.WebMessageReceived += ReceiveMessage;
            Unavailable.Visibility = Visibility.Collapsed;
            core.Navigate(Page);
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or COMException or InvalidOperationException
            or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (_closed) return;
            Unavailable.Text = error is WebView2RuntimeNotFoundException
                ? "Для вкладки «Материалы» нужен Microsoft Edge WebView2 Runtime."
                : "Не удалось открыть вкладку «Материалы». Откройте её ещё раз.";
            Unavailable.Visibility = Visibility.Visible;
        }
        finally { _starting = false; }
    }

    private static void ServePage(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core) return;
        var stream = e.ResourceContext == CoreWebView2WebResourceContext.Document &&
            e.Request.Method == "GET" && e.Request.Uri == Page
            ? typeof(InventoryView).Assembly.GetManifestResourceStream(Resource)
            : null;
        e.Response = stream is null
            ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Cache-Control: no-store")
            : core.Environment.CreateWebResourceResponse(stream, 200, "OK",
                "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
    }

    private async void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_closed || sender is not CoreWebView2 { Source: Page } core ||
            e.Source != Page || DataContext is not IDesktopInventoryClient client)
            return;

        var json = e.WebMessageAsJson;
        if (json.Length > MaxMessageLength) return;
        BrowserRequest? request;
        try { request = JsonSerializer.Deserialize<BrowserRequest>(json, InventoryJson.Options); }
        catch (JsonException) { return; }
        if (request is null || request.Id < 1) return;

        object response;
        try
        {
            var token = _lifetime.Token;
            object result = request switch
            {
                { Type: "read" } => State(await client.ReadAsync(token), client),
                { Type: "apply", Command: { } command } => State(await client.ApplyAsync(command, token), client),
                { Type: "managers" } when client.IsCorporate => await client.ManagersAsync(token),
                { Type: "save", Filename: { } name, Content: { } text } => await SaveAsync(client, name, text, token),
                _ => throw new InventoryValidationException("Неизвестная команда склада."),
            };
            response = new { request.Id, result };
        }
        catch (OperationCanceledException) { return; }
        catch (InventoryConflictException error) { response = new { request.Id, error = error.Message, reload = true }; }
        catch (InventoryAccessException error) { response = new { request.Id, error = error.Message, reload = true }; }
        catch (InventoryValidationException error) { response = new { request.Id, error = error.Message }; }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            response = new { request.Id, error = "Не удалось прочитать или сохранить данные склада." };
        }
        if (!_closed && core.Source == Page)
            core.PostWebMessageAsJson(JsonSerializer.Serialize(response, InventoryJson.Options));
    }

    private static object State(InventorySnapshot snapshot, IDesktopInventoryClient client)
        => new { snapshot, corporate = client.IsCorporate };

    private async Async.Task<bool> SaveAsync(IDesktopInventoryClient client, string filename, string content, CancellationToken token)
    {
        if (!ExportName.IsMatch(filename)) throw new InventoryValidationException("Недопустимый тип файла.");
        if (!(await client.ReadAsync(token)).CanManage)
            throw new InventoryAccessException("Право выгружать данные склада недоступно.");
        if (_closed || !IsVisible) return false;
        var filter = filename.EndsWith(".json", StringComparison.Ordinal)
            ? "Копия склада (*.json)|*.json" : "Остатки склада (*.csv)|*.csv";
        var picker = new SaveFileDialog { Filter = filter, FileName = filename };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return false;
        await File.WriteAllTextAsync(picker.FileName, content, token);
        return true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Browser.Dispose();
    }

    private sealed record BrowserRequest(int Id, string Type, InventoryCommand? Command = null,
        string? Filename = null, string? Content = null);
}
