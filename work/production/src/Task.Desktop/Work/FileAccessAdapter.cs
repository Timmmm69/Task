using System.Diagnostics;
using System.IO;

namespace Task.Desktop.Work;

public enum FileOpenStatus { Opened, NotFound, AccessDenied, Rejected, Failed }
public sealed record FileOpenResult(FileOpenStatus Status, string Message);

public interface IFileAccessAdapter
{
    FileOpenResult Open(string path);
}

public sealed class WindowsFileAccessAdapter : IFileAccessAdapter
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".lnk" };

    public FileOpenResult Open(string path)
    {
        path = path?.Trim() ?? string.Empty;
        if (path.Length is < 3 or > 4096 || path.IndexOfAny(['\r', '\n', '\0']) >= 0 || !Path.IsPathFullyQualified(path))
            return new(FileOpenStatus.Rejected, "Сервер вернул небезопасный путь.");
        if (BlockedExtensions.Contains(Path.GetExtension(path)))
            return new(FileOpenStatus.Rejected, "Запуск исполняемых файлов из каталога запрещён.");
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return new(FileOpenStatus.NotFound, "Файл или папка недоступны на этом компьютере.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new(FileOpenStatus.Opened, "Файл передан Windows для открытия.");
        }
        catch (UnauthorizedAccessException) { return new(FileOpenStatus.AccessDenied, "Windows запретила доступ к файлу."); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return new(FileOpenStatus.Failed, "Не удалось открыть файл зарегистрированным приложением Windows."); }
    }
}
