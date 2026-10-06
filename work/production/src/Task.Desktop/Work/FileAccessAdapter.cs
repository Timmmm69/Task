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
        { ".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".lnk", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".url", ".reg", ".cpl", ".pif", ".psm1", ".msix", ".msp", ".application", ".appref-ms", ".msc", ".chm", ".jar", ".py", ".pyw", ".sh", ".rb", ".pl", ".vb", ".vba", ".dll", ".ocx", ".sys", ".gadget", ".scf", ".sct", ".desktop", ".psd1", ".ps1xml", ".msh", ".msh1", ".msh2", ".msixbundle", ".appx", ".appxbundle" };
    private readonly Action<string> _launch;
    public WindowsFileAccessAdapter(Action<string>? launch = null) => _launch = launch ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));

    public static bool IsAllowedPath(string? path)
    {
        if (path is null || path.Length is < 3 or > 4096 || path.IndexOfAny(['\r', '\n', '\0', '"', '<', '>', '|', '*', '?']) >= 0
            || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.", StringComparison.Ordinal)
            || path.StartsWith(@"\\?", StringComparison.Ordinal) || path.AsSpan(2).Contains(':')) return false;
        if (!(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\') && !path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && parts.Length < 2) return false;
        return !BlockedExtensions.Contains(Path.GetExtension(path)) && !parts.Any(p =>
            p.EndsWith(' ') || p.EndsWith('.') && p is not ("." or "..") ||
            System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    public FileOpenResult Open(string path)
    {
        path = path?.Trim() ?? string.Empty;
        if (!IsAllowedPath(path)) return new(FileOpenStatus.Rejected, "Открытие этого пути из каталога запрещено.");
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return new(FileOpenStatus.NotFound, "Файл или папка недоступны на этом компьютере.");
            // Reparse points can hide an executable target behind a harmless extension or folder.
            // Reject them, including ancestors, rather than executing a different target after validation.
            FileSystemInfo target = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            for (FileSystemInfo? current = target; current is not null; current = current is DirectoryInfo dir ? dir.Parent : ((FileInfo)current).Directory)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new(FileOpenStatus.Rejected, "Ссылки файловой системы из каталога не открываются.");
            if (target is FileInfo file) { using var access = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            _launch(path);
            return new(FileOpenStatus.Opened, "Файл передан Windows для открытия.");
        }
        catch (UnauthorizedAccessException) { return new(FileOpenStatus.AccessDenied, "Windows запретила доступ к файлу."); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException or System.Security.SecurityException)
        { return new(FileOpenStatus.Failed, "Не удалось открыть файл зарегистрированным приложением Windows."); }
    }
}
