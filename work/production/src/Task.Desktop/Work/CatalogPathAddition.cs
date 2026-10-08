using System.IO;
using Task.Application.Files;

namespace Task.Desktop.Work;

public sealed record CatalogPathResult(int Index, string Name, Guid? ItemId, bool Added, string Message);

/// <summary>Shared picker/drop application flow over existing catalog and location commands.
/// Stores metadata only; never enumerates directories or opens file contents.</summary>
public sealed class CatalogPathAddition(IDesktopWorkApiClient client, Func<string, FileAttributes>? readAttributes = null)
{
    public const int MaximumBatch = 1000;
    private readonly Dictionary<(Guid? Parent, string Path), CatalogPathResult> _attempts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string? Normalize(string? raw)
    {
        if (raw is null || !WindowsFileAccessAdapter.IsAllowedPath(raw) || raw.Any(char.IsControl)
            || raw.Split('\\').Any(p => p is "." or "..")) return null;
        if (raw.StartsWith(@"\\"))
        {
            var verdict = FileLocationPolicy.ValidateUnc(raw, []);
            return verdict.IsValid ? verdict.NormalizedPath : null;
        }
        return raw.Length > 3 ? raw.TrimEnd('\\') : raw;
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<CatalogPathResult>> AddAsync(
        IReadOnlyList<string> paths, Guid? parent, Func<bool> canWrite,
        IReadOnlyList<DesktopCatalogItem> catalog, CancellationToken ct = default)
    {
        if (paths.Count is < 1 or > MaximumBatch)
            return [new(0, "Выбор", null, false, $"Выберите от 1 до {MaximumBatch} элементов.")];
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(true))
            return [new(0, "Выбор", null, false, "Добавление уже выполняется.")];
        try
        {
            var results = new List<CatalogPathResult>();
            var connectionLost = false;
            for (var index = 0; index < paths.Count; index++)
            {
                var path = Normalize(paths[index]);
                var name = path is null ? $"Элемент {index + 1}" : Path.GetFileName(path.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = "Папка";
                CatalogPathResult Fail(string message, Guid? id = null) => new(index + 1, name, id, false, message);
                if (connectionLost || ct.IsCancellationRequested || !canWrite()) { results.Add(Fail("Добавление требует подключения и разрешений.")); continue; }
                if (path is null) { results.Add(Fail("Неподдерживаемый путь.")); continue; }
                var key = (parent, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.Unicode.GetBytes(path.ToUpperInvariant()))));
                if (_attempts.TryGetValue(key, out var previous))
                { results.Add(previous with { Index = index + 1, Added = false, Message = previous.Added ? "Уже добавлен." : previous.Message }); continue; }
                if (_attempts.Count >= 10000) { results.Add(Fail("Достигнут лимит текущей сессии добавления.")); continue; }
                Guid? createdId = null;
                try
                {
                    DesktopLocationDraft? draft = new("local_path", path, 0, true, true);
                    if (path.StartsWith(@"\\") && client is IDesktopFileLocationsClient locations)
                    {
                        var resources = await locations.GetNetworkResourcesAsync(ct).ConfigureAwait(true);
                        var resource = resources is DesktopWorkResult<IReadOnlyList<Task.Desktop.Administration.DesktopNetworkResource>>.Succeeded read
                            ? read.Value.Where(r => r.Status == "active" && r.RootPath is not null && FileLocationPolicy.ValidateUnc(path, [r.RootPath]).IsValid)
                                .OrderByDescending(r => r.RootPath!.Length).FirstOrDefault() : null;
                        if (resource is null) { results.Add(Fail("Нет доступного разрешённого сетевого ресурса.")); continue; }
                        draft = draft with { LocationType = "unc_path", NetworkResourceId = resource.Id };
                    }
                    // Authorize UNC roots before any SMB metadata access. Attributes may
                    // involve SMB: keep this I/O off the dispatcher, without reading bytes.
                    var attributes = await System.Threading.Tasks.Task.Run(() => (readAttributes ?? File.GetAttributes)(path), ct).ConfigureAwait(true);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { results.Add(Fail("Ссылки файловой системы не поддерживаются.")); continue; }
                    var type = (attributes & FileAttributes.Directory) != 0 ? "folder_reference" : "file_reference";
                    // Compare only locations visible and usable by the current user/device.
                    var duplicate = false;
                    foreach (var item in catalog.Where(i => i.ParentId == parent && i.ItemType == type && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        IReadOnlyList<DesktopFileLocation> candidates = [];
                        if (client is IDesktopFileLocationsClient existingLocations)
                        {
                            var readLocations = await existingLocations.GetLocationsAsync(item.Id, ct).ConfigureAwait(true);
                            if (readLocations is not DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded confirmed)
                                throw new IOException("Cannot confirm existing locations.");
                            candidates = confirmed.Value;
                        }
                        else
                        {
                            var resolved = await client.ResolveLocationAsync(item.Id, ct).ConfigureAwait(true);
                            if (resolved is DesktopWorkResult<DesktopFileLocation?>.Succeeded { Value: { } location }) candidates = [location];
                            else if (resolved is not DesktopWorkResult<DesktopFileLocation?>.Succeeded) throw new IOException("Cannot confirm existing location.");
                        }
                        if (candidates.Any(l => l.CanOpenOnDevice && l.IsEnabled && string.Equals(Normalize(l.RawPath), path, StringComparison.OrdinalIgnoreCase)))
                        { duplicate = true; createdId = item.Id; break; }
                    }
                    if (duplicate) { results.Add(Fail("Уже добавлен.", createdId)); continue; }
                    if (!canWrite() || ct.IsCancellationRequested) { results.Add(Fail("Добавление требует подключения и разрешений.")); continue; }
                    // Retain uncertain outcomes. Never replay a create with a fresh idempotency key.
                    _attempts[key] = Fail("Результат не подтверждён. Обновите каталог перед дальнейшими действиями.");
                    var created = await client.CreateCatalogItemAsync(name, type, null, parent, ct).ConfigureAwait(true);
                    if (created is not DesktopWorkResult<DesktopCatalogItem>.Succeeded success)
                    {
                        var failure = Fail(Error(created)); results.Add(failure);
                        if (created is DesktopWorkResult<DesktopCatalogItem>.ServerUnavailable or DesktopWorkResult<DesktopCatalogItem>.MalformedResponse)
                        { _attempts[key] = failure; connectionLost = true; }
                        else _attempts.Remove(key);
                        if (created is DesktopWorkResult<DesktopCatalogItem>.AuthenticationFailure) connectionLost = true;
                        continue;
                    }
                    createdId = success.Value.Id;
                    var partial = Fail("Запись создана, путь не подтверждён. Используйте редактор расположений этой записи.", createdId);
                    _attempts[key] = partial;
                    if (!canWrite() || ct.IsCancellationRequested) { results.Add(partial); continue; }
                    var saved = client is IDesktopFileLocationsClient locationClient
                        ? await locationClient.CreateLocationAsync(createdId.Value, success.Value.Version, draft, ct).ConfigureAwait(true)
                        : await client.AddLocationAsync(createdId.Value, success.Value.Version, path, ct).ConfigureAwait(true);
                    var result = saved is DesktopWorkResult<bool>.Succeeded
                        ? new CatalogPathResult(index + 1, name, createdId, true, "Добавлен.")
                        : partial with { Message = partial.Message + " " + Error(saved) };
                    _attempts[key] = result; results.Add(result);
                    if (saved is DesktopWorkResult<bool>.ServerUnavailable or DesktopWorkResult<bool>.AuthenticationFailure or DesktopWorkResult<bool>.MalformedResponse) connectionLost = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OperationCanceledException or System.Security.SecurityException)
                {
                    var result = _attempts.GetValueOrDefault(key) ?? Fail("Windows не предоставила доступ к элементу.", createdId);
                    results.Add(result);
                }
                catch
                {
                    results.Add(_attempts.GetValueOrDefault(key) ?? Fail("Не удалось завершить добавление.", createdId));
                    if (_attempts.ContainsKey(key)) connectionLost = true;
                }
            }
            return results;
        }
        finally { _gate.Release(); }
    }

    private static string Error<T>(DesktopWorkResult<T> result) => result switch
    {
        DesktopWorkResult<T>.Forbidden => "Недостаточно прав.",
        DesktopWorkResult<T>.AuthenticationFailure => "Сессия завершена.",
        DesktopWorkResult<T>.NotFound => "Цель больше недоступна.",
        DesktopWorkResult<T>.Conflict => "Данные изменились; обновите каталог.",
        DesktopWorkResult<T>.ValidationFailure => "Путь или запись отклонены сервером.",
        _ => "Результат не подтверждён; проверьте каталог.",
    };
}
