using Task.Application;

namespace Task.Desktop.Work;

/// <summary>Uses catalog metadata and the current device's authorized locations. Disk and SMB checks run on demand.</summary>
internal sealed class TaskDocumentAccess(IDesktopWorkApiClient catalog, IDesktopObjectLinksClient links, WindowsFileAccessAdapter? windows = null)
{
    private readonly WindowsFileAccessAdapter _windows = windows ?? new();
    private readonly CatalogPathAddition _paths = new(catalog);

    internal async System.Threading.Tasks.Task<CatalogPathResult> AddPathAsync(string path, Func<bool> canWrite, CancellationToken token)
    {
        var snapshot = await catalog.GetCatalogAsync(token);
        if (snapshot is not DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded current)
            return new(1, "", null, false, "Не удалось прочитать каталог. Проверьте подключение и права.");
        var results = await _paths.AddAsync([path], null, canWrite, current.Value, token, reuseAcrossFolders: true);
        return results[0];
    }

    internal async System.Threading.Tasks.Task<string> OpenAsync(Guid task, Guid target, bool folder, CancellationToken token)
    {
        if (await ReadTargetsAsync(task, token) is not { } targets || !targets.Contains(target))
            return "Связь больше недоступна. Проверьте права и обновите данные.";
        var path = await ResolveAsync(target, token);
        if (path is null) return "На этом компьютере нет доступного расположения файла или папки.";
        return (await System.Threading.Tasks.Task.Run(() => _windows.OpenReference(path, folder, token), token)).Message;
    }

    internal async System.Threading.Tasks.Task<IReadOnlyList<TaskFolderReference>> FindAvailableFoldersAsync(
        Guid task, IReadOnlyList<TaskFolderReference> candidates, CancellationToken token)
    {
        if (await ReadTargetsAsync(task, token) is not { } targets) return [];
        var available = new List<TaskFolderReference>();
        foreach (var folder in candidates.Where(folder => targets.Contains(folder.Id)))
        {
            var path = await ResolveAsync(folder.Id, token);
            if (path is not null && (await System.Threading.Tasks.Task.Run(() => _windows.CheckFolder(path, token), token)).Status == FileOpenStatus.Opened)
                available.Add(folder);
        }
        return available;
    }

    private async System.Threading.Tasks.Task<HashSet<Guid>?> ReadTargetsAsync(Guid task, CancellationToken token)
    {
        var result = await links.GetLinksAsync(task, token);
        return result is DesktopWorkResult<DesktopObjectLinks>.Succeeded current
            ? current.Value.Items.Where(link => link.SourceObjectId == task && link.LinkType == "task_file")
                .Select(link => link.TargetObjectId).ToHashSet()
            : null;
    }

    private async System.Threading.Tasks.Task<string?> ResolveAsync(Guid target, CancellationToken token)
    {
        var result = await catalog.ResolveLocationAsync(target, token);
        token.ThrowIfCancellationRequested();
        return result is DesktopWorkResult<DesktopFileLocation?>.Succeeded { Value: { CanOpenOnDevice: true, IsEnabled: true, RawPath: { Length: > 0 } path } }
            ? path : null;
    }
}
