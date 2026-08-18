using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Task.Desktop.Security;

/// <summary>
/// Credential vault of the desktop client: stores the session refresh token
/// in a single DPAPI-protected file and the access token in memory only.
///
/// Storage layout: one <see cref="ProtectedData"/> (CurrentUser) blob in
/// <c>%LOCALAPPDATA%\Task\credentials.bin</c> containing a serialized
/// <see cref="RefreshTokenEntry"/> with <c>version = 1</c>.
///
/// Fail-closed: if the file is missing, unreadable, does not decrypt or has an
/// unsupported version, <see cref="GetRefreshToken"/> returns <c>null</c> and
/// the application continues as "not signed in". A corrupt file is never
/// deleted automatically: it is renamed to <c>credentials.bin.corrupt-&lt;timestamp&gt;</c>
/// for diagnostics when the rename succeeds; a failed rename is ignored.
///
/// Thread safety: all members are guarded by a private lock. There is no cache:
/// every call re-reads the file from disk so that concurrent processes or
/// windows observe each other's writes.
/// </summary>
public sealed class DesktopCredentialVault
{
    private const int CurrentVersion = 1;
    private const string FileName = "credentials.bin";

    private readonly object _sync = new();
    private readonly string _filePath;
    private string? _accessToken;

    /// <summary>
    /// Creates a vault stored in the default application directory
    /// <c>%LOCALAPPDATA%\Task</c>.
    /// </summary>
    public DesktopCredentialVault()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Task"))
    {
    }

    /// <summary>
    /// Creates a vault stored in the given directory (used by tests to isolate
    /// the credential file from the real application directory).
    /// </summary>
    public DesktopCredentialVault(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        StorageDirectory = storageDirectory;
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    /// <summary>Directory that contains the credential file.</summary>
    public string StorageDirectory { get; }

    /// <summary>
    /// Persists the refresh token and its context. The whole payload is
    /// encrypted as a single DPAPI (CurrentUser) blob before it touches disk.
    /// </summary>
    public void SaveRefreshToken(string deviceId, string orgId, string login, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var entry = new RefreshTokenEntry(deviceId, orgId, login, refreshToken, DateTime.UtcNow, CurrentVersion);
        byte[] protectedBytes = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(entry),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        lock (_sync)
        {
            Directory.CreateDirectory(StorageDirectory);
            var tempPath = _filePath + ".tmp";
            File.WriteAllBytes(tempPath, protectedBytes);
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }

    /// <summary>
    /// Reads and decrypts the refresh token entry, or returns <c>null</c> when
    /// no entry exists or the stored file is corrupt (fail-closed). A corrupt
    /// file is isolated by renaming, never deleted.
    /// </summary>
    public RefreshTokenEntry? GetRefreshToken()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            byte[] protectedBytes;
            try
            {
                protectedBytes = File.ReadAllBytes(_filePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            byte[] payload;
            try
            {
                payload = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                IsolateCorruptFile();
                return null;
            }

            RefreshTokenEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<RefreshTokenEntry>(payload);
            }
            catch (JsonException)
            {
                IsolateCorruptFile();
                return null;
            }

            if (entry is null ||
                entry.Version != CurrentVersion ||
                string.IsNullOrWhiteSpace(entry.DeviceId) ||
                string.IsNullOrWhiteSpace(entry.OrgId) ||
                string.IsNullOrWhiteSpace(entry.Login) ||
                string.IsNullOrWhiteSpace(entry.RefreshToken))
            {
                IsolateCorruptFile();
                return null;
            }

            return entry;
        }
    }

    /// <summary>Keeps the access token in memory only; it never touches disk.</summary>
    public void SetAccessToken(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        lock (_sync)
        {
            _accessToken = accessToken;
        }
    }

    /// <summary>Returns the in-memory access token or <c>null</c> when not set.</summary>
    public string? GetAccessToken()
    {
        lock (_sync)
        {
            return _accessToken;
        }
    }

    /// <summary>
    /// Removes the persisted refresh token file and clears the in-memory
    /// access token. File deletion failures are swallowed: memory is always
    /// cleared and the caller continues as "not signed in".
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _accessToken = null;
            try
            {
                File.Delete(_filePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void IsolateCorruptFile()
    {
        try
        {
            var corruptPath = Path.Combine(
                StorageDirectory,
                $"{FileName}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            File.Move(_filePath, corruptPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Session credential entry persisted by <see cref="DesktopCredentialVault"/>.
/// Contains session tokens only — never passwords or key material.
/// </summary>
/// <param name="DeviceId">Identifier of the client device that owns the session.</param>
/// <param name="OrgId">Identifier of the organization the session belongs to.</param>
/// <param name="Login">User login the session was issued for.</param>
/// <param name="RefreshToken">Refresh token issued by the server.</param>
/// <param name="SavedAtUtc">UTC timestamp of the last save.</param>
/// <param name="Version">Payload format version; the vault rejects unknown versions.</param>
public sealed record RefreshTokenEntry(
    string DeviceId,
    string OrgId,
    string Login,
    string RefreshToken,
    DateTime SavedAtUtc,
    int Version);
