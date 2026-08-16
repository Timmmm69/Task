using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Task.Infrastructure.Identity;

public sealed class Argon2idPasswordHasher
{
    public const int MemoryKiB = 65_536;
    public const int Iterations = 3;
    public const int Parallelism = 2;
    public const int SaltLength = 32;
    public const int HashLength = 32;

    public PasswordHash Hash(string password, string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var pepperBytes = Encoding.UTF8.GetBytes(pepper);
        var input = new byte[passwordBytes.Length + pepperBytes.Length + 1];
        var hash = new byte[HashLength];

        try
        {
            Buffer.BlockCopy(passwordBytes, 0, input, 0, passwordBytes.Length);
            Buffer.BlockCopy(pepperBytes, 0, input, passwordBytes.Length + 1, pepperBytes.Length);
            Generate(input, salt, hash);

            return new PasswordHash(
                $"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}",
                "argon2id",
                $$"""{"memoryKiB":{{MemoryKiB}},"iterations":{{Iterations}},"parallelism":{{Parallelism}},"hashLength":{{HashLength}},"saltLength":{{SaltLength}},"version":19}""");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(pepperBytes);
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public bool Verify(string password, string pepper, PasswordHash stored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        ArgumentNullException.ThrowIfNull(stored);
        if (!string.Equals(stored.Algorithm, "argon2id", StringComparison.Ordinal) ||
            !TryDecode(stored.Encoded, out var salt, out var expectedHash))
        {
            return false;
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var pepperBytes = Encoding.UTF8.GetBytes(pepper);
        var input = new byte[passwordBytes.Length + pepperBytes.Length + 1];
        var actualHash = new byte[HashLength];
        try
        {
            Buffer.BlockCopy(passwordBytes, 0, input, 0, passwordBytes.Length);
            Buffer.BlockCopy(pepperBytes, 0, input, passwordBytes.Length + 1, pepperBytes.Length);
            Generate(input, salt, actualHash);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(pepperBytes);
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(actualHash);
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    private static void Generate(byte[] input, byte[] salt, byte[] output)
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithSalt(salt)
            .WithMemoryAsKB(MemoryKiB)
            .WithIterations(Iterations)
            .WithParallelism(Parallelism)
            .Build();
        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);
        generator.GenerateBytes(input, output);
    }

    private static bool TryDecode(string encoded, out byte[] salt, out byte[] hash)
    {
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" ||
            parts[2] != "v=19" || parts[3] != "m=65536,t=3,p=2")
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
            if (salt.Length != SaltLength || hash.Length != HashLength)
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(hash);
                salt = Array.Empty<byte>();
                hash = Array.Empty<byte>();
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);
            salt = Array.Empty<byte>();
            hash = Array.Empty<byte>();
            return false;
        }
    }
}

public sealed record PasswordHash(string Encoded, string Algorithm, string ParametersJson);
