using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Task.Desktop.Modes;

public enum ApplicationMode { Personal, Corporate }

/// <summary>Non-secret preference, independent of credentials and server configuration.</summary>
public sealed class ApplicationModePreference(string directory)
{
    private readonly string _path = Path.Combine(directory, "application-preferences.json");
    private sealed record Preference(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("mode")] string Mode);

    public ApplicationMode? Load()
    {
        try
        {
            var preference = JsonSerializer.Deserialize<Preference>(File.ReadAllBytes(_path));
            return preference?.Version == 1 ? preference.Mode switch
            {
                "Personal" => ApplicationMode.Personal,
                "Corporate" => ApplicationMode.Corporate,
                _ => null,
            } : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(ApplicationMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Directory.CreateDirectory(directory);
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Preference(1, mode.ToString())));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal interface IApplicationExecutionContext : IDisposable
{
    ApplicationMode Mode { get; }
    global::System.Threading.Tasks.Task StartAsync();
    global::System.Threading.Tasks.Task<bool> PrepareSwitchAsync();
}

/// <summary>Only the chosen factory runs. No corporate services exist during Personal startup.</summary>
internal sealed class ApplicationModeLifecycle(
    ApplicationModePreference preference,
    Func<ApplicationMode, IApplicationExecutionContext> createContext) : IDisposable
{
    private bool _transitioning;
    private bool _disposed;
    public IApplicationExecutionContext? Current { get; private set; }
    public bool IsTransitioning => _transitioning;

    public async global::System.Threading.Tasks.Task<bool> RestoreAsync()
    {
        var mode = preference.Load();
        return mode.HasValue && await SwitchAsync(mode.Value);
    }

    public async global::System.Threading.Tasks.Task<bool> SwitchAsync(ApplicationMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (_transitioning) return false;
        if (Current?.Mode == mode) return true;
        _transitioning = true;
        IApplicationExecutionContext next;
        try
        {
            if (Current is not null && !await Current.PrepareSwitchAsync()) return false;
            if (_disposed) return false;
            // A failed preference write leaves the old context and its drafts intact.
            next = createContext(mode);
            try { preference.Save(mode); }
            catch { next.Dispose(); throw; }
            Current?.Dispose();
            Current = next;
        }
        finally { _transitioning = false; }
        // Startup may be waiting for a server. Switching away must remain possible;
        // disposing that context cancels its startup before another context runs.
        await next.StartAsync();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Current?.Dispose();
        Current = null;
    }
}
