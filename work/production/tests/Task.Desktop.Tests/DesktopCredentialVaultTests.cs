using System.IO;
using System.Security.Cryptography;
using System.Text;
using Task.Desktop.Security;

namespace Task.Desktop.Tests;

/// <summary>
/// Tests for <see cref="DesktopCredentialVault"/>: DPAPI persistence of the
/// refresh token, in-memory access token, fail-closed corruption handling and
/// guaranteed absence of plaintext secrets on disk.
///
/// DPAPI (CurrentUser) decrypts fine for the user running the tests; in an
/// environment without an interactive user profile these tests would need to
/// be skipped, but they are expected to run on the developer machine and on
/// the windows-latest CI runner.
/// </summary>
public class DesktopCredentialVaultTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "Task.Desktop.Tests", Guid.NewGuid().ToString("N"));

    private string VaultFilePath => Path.Combine(_directory, "credentials.bin");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void SaveThenGet_RoundTripsAllFields()
    {
        var vault = new DesktopCredentialVault(_directory);
        var before = DateTime.UtcNow.AddSeconds(-1);

        vault.SaveRefreshToken("device-1", "org-42", "ivan", "RT_9f8e7d6c5b4a");

        var entry = vault.GetRefreshToken();

        Assert.NotNull(entry);
        Assert.Equal("device-1", entry.DeviceId);
        Assert.Equal("org-42", entry.OrgId);
        Assert.Equal("ivan", entry.Login);
        Assert.Equal("RT_9f8e7d6c5b4a", entry.RefreshToken);
        Assert.True(entry.SavedAtUtc >= before && entry.SavedAtUtc <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(1, entry.Version);
    }

    [Fact]
    public void Get_WhenNoFileExists_ReturnsNull()
    {
        var vault = new DesktopCredentialVault(_directory);

        Assert.Null(vault.GetRefreshToken());
    }

    [Fact]
    public void EveryCall_ReReadsFileFromDisk_NoCache()
    {
        var first = new DesktopCredentialVault(_directory);
        var second = new DesktopCredentialVault(_directory);

        first.SaveRefreshToken("device-1", "org-1", "a", "TOKEN_A");

        Assert.Equal("TOKEN_A", second.GetRefreshToken()?.RefreshToken);

        first.SaveRefreshToken("device-1", "org-1", "a", "TOKEN_B");

        Assert.Equal("TOKEN_B", second.GetRefreshToken()?.RefreshToken);
    }

    [Fact]
    public void Save_OverwritesPreviousEntry()
    {
        var vault = new DesktopCredentialVault(_directory);

        vault.SaveRefreshToken("device-1", "org-1", "a", "OLD_TOKEN");
        vault.SaveRefreshToken("device-2", "org-2", "b", "NEW_TOKEN");

        var entry = vault.GetRefreshToken();

        Assert.NotNull(entry);
        Assert.Equal("device-2", entry.DeviceId);
        Assert.Equal("org-2", entry.OrgId);
        Assert.Equal("b", entry.Login);
        Assert.Equal("NEW_TOKEN", entry.RefreshToken);
    }

    [Fact]
    public void AccessToken_IsOnlyInMemory_NeverOnDisk()
    {
        var vault = new DesktopCredentialVault(_directory);

        vault.SetAccessToken("AT_plaintext_must_not_land_on_disk");

        Assert.Equal("AT_plaintext_must_not_land_on_disk", vault.GetAccessToken());
        Assert.False(File.Exists(VaultFilePath));

        vault.SaveRefreshToken("device-1", "org-1", "a", "RT_token");
        var bytes = File.ReadAllBytes(VaultFilePath);
        Assert.False(ContainsSequence(bytes, EncodeUtf8("AT_plaintext_must_not_land_on_disk")));
    }

    [Fact]
    public void Clear_RemovesFileAndAccessToken()
    {
        var vault = new DesktopCredentialVault(_directory);
        vault.SaveRefreshToken("device-1", "org-1", "a", "RT_token");
        vault.SetAccessToken("AT_token");

        vault.Clear();

        Assert.False(File.Exists(VaultFilePath));
        Assert.Null(vault.GetRefreshToken());
        Assert.Null(vault.GetAccessToken());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void CorruptFile_TamperedBytes_ReturnsNull_AndIsolatesFile()
    {
        var vault = new DesktopCredentialVault(_directory);
        vault.SaveRefreshToken("device-1", "org-1", "a", "RT_token");
        var tampered = File.ReadAllBytes(VaultFilePath);
        for (var i = 0; i < tampered.Length; i++)
        {
            tampered[i] = (byte)(tampered[i] ^ 0xFF);
        }
        File.WriteAllBytes(VaultFilePath, tampered);

        var entry = vault.GetRefreshToken();

        Assert.Null(entry);
        Assert.False(File.Exists(VaultFilePath));
        Assert.Single(Directory.GetFiles(_directory, "credentials.bin.corrupt-*"));
    }

    [Fact]
    public void CorruptFile_WrongVersion_ReturnsNull_AndIsolatesFile()
    {
        var vault = new DesktopCredentialVault(_directory);
        vault.SaveRefreshToken("device-1", "org-1", "a", "RT_token");

        var payload = ProtectedData.Unprotect(
            File.ReadAllBytes(VaultFilePath),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        var entry = System.Text.Json.JsonSerializer.Deserialize<RefreshTokenEntry>(payload)!;
        var wrongVersion = ProtectedData.Protect(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry with { Version = 999 }),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(VaultFilePath, wrongVersion);

        Assert.Null(vault.GetRefreshToken());
        Assert.False(File.Exists(VaultFilePath));
        Assert.Single(Directory.GetFiles(_directory, "credentials.bin.corrupt-*"));
    }

    [Fact]
    public void EncryptedFile_DoesNotContainPlaintextTokenOrIdentity()
    {
        var vault = new DesktopCredentialVault(_directory);
        const string token = "RT_0gHb3kL9wXq7ZnY5CuM2pR8sVdGfJ1tN4eK6oA0xBcDiElFhSmoPyUiDrTwZaQj";
        const string login = "ivan_petrov_42";

        vault.SaveRefreshToken("device-abc-123", "org-xyz-789", login, token);

        var bytes = File.ReadAllBytes(VaultFilePath);

        Assert.False(ContainsSequence(bytes, EncodeUtf8(token)));
        Assert.False(ContainsSequence(bytes, EncodeUtf8(login)));
        Assert.False(ContainsSequence(bytes, EncodeUtf8("device-abc-123")));
        Assert.False(ContainsSequence(bytes, EncodeUtf8("org-xyz-789")));

        var decoded = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain(token, decoded);
        Assert.DoesNotContain(login, decoded);
    }

    [Fact]
    public void Get_WhenDirectoryMissing_ReturnsNull()
    {
        var vault = new DesktopCredentialVault(_directory);

        Assert.Null(vault.GetRefreshToken());
    }

    private static byte[] EncodeUtf8(string text) => Encoding.UTF8.GetBytes(text);

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}