using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Task.Desktop.Work;

internal static class CatalogPathClipboard
{
    internal static bool IsAvailable(string? path) => path is { Length: > 0 } &&
        Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path));

    internal static async System.Threading.Tasks.Task CopyAsync(string path, Func<bool> stillSelected, Action<string> feedback)
    {
        var available = await System.Threading.Tasks.Task.Run(() => IsAvailable(path));
        if (!stillSelected()) return;
        if (!available) { feedback("Путь больше недоступен."); return; }

        try { Clipboard.SetText(path); feedback("Путь скопирован в буфер обмена."); }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        { feedback("Не удалось скопировать путь. Повторите попытку."); }
    }
}
