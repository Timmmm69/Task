using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Task.Desktop.Personal;

internal sealed record PersonalBackupMetadata(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string AppVersion,
    [property: JsonRequired] DateTimeOffset CreatedAtUtc,
    [property: JsonRequired] string Sha256);

/// <summary>Personal-only ZIP allowlist; no directory enumeration or Corporate dependencies.</summary>
internal sealed class PersonalBackupService(PersonalDataPaths paths, PersonalStoreOwnership ownership, Action<string>? fault = null)
{
    internal const int FormatVersion = 1;
    internal const long MaxSnapshotBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Sidecars = ["-wal", "-shm", "-journal"];
    private string PendingPath => Path.Combine(paths.DirectoryPath, "restore-pending.json");
    private sealed record PendingRestore(string SafetyPath, bool HadDatabase, string? PreviousMarkerPath = null);
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Hash(stream); }
    private void CheckOwnership() => ownership.Verify(paths);
    private static void Cleanup(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* .tmp is never a valid package */ }
    }
    private static void Flush(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Flush(true);
    }
    internal PersonalBackupMetadata Backup(PersonalTaskStore store, string destination)
    {
        CheckOwnership();
        destination = Path.GetFullPath(destination);
        if (!destination.EndsWith(".taskbackup", StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(paths.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Сохраните .taskbackup вне папки Personal.");
        var snapshot = Path.Combine(paths.DirectoryPath, $"snapshot-{Guid.NewGuid():N}.tmp");
        var package = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            fault?.Invoke("snapshot");
            store.Snapshot(snapshot);
            var version = PersonalDatabaseValidation.ValidateFile(snapshot);
            var metadata = new PersonalBackupMetadata("Task.Personal", FormatVersion, version,
                typeof(PersonalBackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0", DateTimeOffset.UtcNow, HashFile(snapshot));
            fault?.Invoke("package-write");
            using (var output = new FileStream(package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var entry = archive.CreateEntry("metadata.json").Open()) JsonSerializer.Serialize(entry, metadata, JsonOptions);
                    using (var entry = archive.CreateEntry("personal.db", CompressionLevel.Optimal).Open())
                    using (var input = File.OpenRead(snapshot)) input.CopyTo(entry);
                }
                output.Flush(true);
            }
            fault?.Invoke("package-complete");
            File.Move(package, destination, overwrite: true);
            return metadata;
        }
        finally { Cleanup(snapshot); Cleanup(package); }
    }

    // Validation order: format, metadata, SHA, supported format/schema, extraction, integrity.
    private string Prepare(string package)
    {
        CheckOwnership();
        using var input = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count != 2 || archive.Entries.Count(e => e.FullName == "metadata.json") != 1
            || archive.Entries.Count(e => e.FullName == "personal.db") != 1)
            throw new InvalidDataException("Неверный формат Personal backup.");
        var metadataEntry = archive.GetEntry("metadata.json")!;
        var databaseEntry = archive.GetEntry("personal.db")!;
        if (metadataEntry.Length is < 1 or > 4096 || databaseEntry.Length is < 100 or > MaxSnapshotBytes)
            throw new InvalidDataException("Неверный размер Personal backup.");
        PersonalBackupMetadata metadata;
        using (var entry = metadataEntry.Open())
            metadata = JsonSerializer.Deserialize<PersonalBackupMetadata>(entry, JsonOptions) ?? throw new InvalidDataException("Отсутствует metadata.");
        if (metadata.Kind != "Task.Personal" || string.IsNullOrWhiteSpace(metadata.AppVersion)
            || metadata.CreatedAtUtc == default || metadata.CreatedAtUtc.Offset != TimeSpan.Zero
            || metadata.Sha256 is null || metadata.Sha256.Length != 64 || !metadata.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Неверная metadata Personal backup.");
        using (var entry = databaseEntry.Open())
            if (!string.Equals(Hash(entry), metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 не совпадает. Текущая база не изменена.");
        if (metadata.FormatVersion != FormatVersion) throw new InvalidDataException("Неподдерживаемая версия backup.");
        if (metadata.SchemaVersion < 1 || metadata.SchemaVersion > PersonalDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("Неподдерживаемая версия схемы backup. Обновите Task.");
        var temporary = Path.Combine(paths.DirectoryPath, $"restore-{Guid.NewGuid():N}.tmp");
        try
        {
            fault?.Invoke("restore-temp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var entry = databaseEntry.Open(); entry.CopyTo(output); output.Flush(true);
            }
            if (PersonalDatabaseValidation.ValidateFile(temporary) != metadata.SchemaVersion)
                throw new InvalidDataException("Версия схемы не соответствует metadata.");
            return temporary;
        }
        catch { Cleanup(temporary); throw; }
    }

    internal string Restore(string package, Action close, Action reopen)
    {
        var temporary = Prepare(package); // No close/mutation before every validation succeeds.
        var activated = false;
        var closed = false;
        var markerWritten = false;
        var safety = Path.Combine(paths.DirectoryPath, "recovery", $"restore-{Guid.NewGuid():N}", "tasks.db");
        var hadDatabase = File.Exists(paths.DatabasePath);
        byte[]? previousMarker = null;
        string? previousMarkerPath = null;
        try
        {
            if (File.Exists(PendingPath)) previousMarker = File.ReadAllBytes(PendingPath);
            close(); closed = true;
            Directory.CreateDirectory(Path.GetDirectoryName(safety)!);
            // Copy closed files only. Keep all sidecars for diagnosis and crash rollback.
            if (hadDatabase) { File.Copy(paths.DatabasePath, safety); Flush(safety); }
            foreach (var suffix in Sidecars)
                if (File.Exists(paths.DatabasePath + suffix)) { File.Copy(paths.DatabasePath + suffix, safety + suffix); Flush(safety + suffix); }
            if (previousMarker is not null)
            {
                previousMarkerPath = Path.Combine(Path.GetDirectoryName(safety)!, "previous-restore-pending.json");
                File.WriteAllBytes(previousMarkerPath, previousMarker); Flush(previousMarkerPath);
            }
            WritePending(new(safety, hadDatabase, previousMarkerPath));
            markerWritten = true;
            fault?.Invoke("before-activation");
            if (hadDatabase) File.Replace(temporary, paths.DatabasePath, null);
            else File.Move(temporary, paths.DatabasePath);
            activated = true;
            fault?.Invoke("after-activation");
            foreach (var suffix in Sidecars) File.Delete(paths.DatabasePath + suffix);
            // The pending marker must remain through verification, so reopening explicitly permits it.
            reopen();
            fault?.Invoke("startup-verified");
            File.Delete(PendingPath);
            return safety;
        }
        catch (Exception original)
        {
            try
            {
                if (activated)
                {
                    close();
                    Rollback(new(safety, hadDatabase, previousMarkerPath));
                }
                if (closed && previousMarker is null) reopen();
                if (markerWritten && !activated)
                {
                    if (previousMarker is null) File.Delete(PendingPath);
                    else RestoreMarker(previousMarkerPath!);
                }
            }
            catch (Exception recovery) { throw new AggregateException("Восстановление не завершено. Текущая база и safety copy сохранены; откройте recovery.", original, recovery); }
            throw;
        }
        finally { Cleanup(temporary); }
    }

    private void WritePending(PendingRestore pending)
    {
        var temporary = PendingPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(pending)); Flush(temporary);
            File.Move(temporary, PendingPath, overwrite: true);
        }
        finally { Cleanup(temporary); }
    }
    private void Rollback(PendingRestore pending)
    {
        var failed = Path.Combine(Path.GetDirectoryName(pending.SafetyPath)!, $"failed-{Guid.NewGuid():N}.db");
        if (pending.HadDatabase)
        {
            var temporary = pending.SafetyPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(pending.SafetyPath, temporary); Flush(temporary);
                if (File.Exists(paths.DatabasePath)) File.Replace(temporary, paths.DatabasePath, failed);
                else File.Move(temporary, paths.DatabasePath);
            }
            finally { Cleanup(temporary); }
        }
        else if (File.Exists(paths.DatabasePath)) File.Move(paths.DatabasePath, failed);
        foreach (var suffix in Sidecars)
        {
            if (File.Exists(paths.DatabasePath + suffix)) File.Move(paths.DatabasePath + suffix, failed + suffix);
            if (File.Exists(pending.SafetyPath + suffix)) File.Copy(pending.SafetyPath + suffix, paths.DatabasePath + suffix);
        }
        if (pending.PreviousMarkerPath is null) File.Delete(PendingPath);
        else RestoreMarker(pending.PreviousMarkerPath);
    }
    private void RestoreMarker(string previous)
    {
        var temporary = PendingPath + ".tmp";
        try { File.Copy(previous, temporary, overwrite: true); Flush(temporary); File.Move(temporary, PendingPath, overwrite: true); }
        finally { Cleanup(temporary); }
    }
    internal bool HasPendingRestore => File.Exists(PendingPath);
    internal void RecoverSafetyCopy()
    {
        CheckOwnership();
        var pending = JsonSerializer.Deserialize<PendingRestore>(File.ReadAllBytes(PendingPath)) ?? throw new InvalidDataException("Invalid recovery marker.");
        var recoveryRoot = Path.Combine(paths.DirectoryPath, "recovery") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(pending.SafetyPath).StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid recovery path.");
        if (pending.PreviousMarkerPath is { } previous && !Path.GetFullPath(previous).StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid previous recovery marker path.");
        Rollback(pending);
    }
}
