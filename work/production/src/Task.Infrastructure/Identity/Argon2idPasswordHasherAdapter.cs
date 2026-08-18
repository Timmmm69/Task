using Task.Application.Security;

namespace Task.Infrastructure.Identity;

/// <summary>
/// Adapts Argon2idPasswordHasher to the application-facing IPasswordHasher contract. The
/// pepper is fixed at construction time and never crosses the application boundary; the
/// adapter only exchanges encoded hashes and their JSON parameters.
/// </summary>
public sealed class Argon2idPasswordHasherAdapter : IPasswordHasher
{
    private const string Argon2idAlgorithm = "argon2id";

    private readonly Argon2idPasswordHasher _inner;
    private readonly string _pepper;

    public Argon2idPasswordHasherAdapter(string pepper, Argon2idPasswordHasher? inner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        _inner = inner ?? new Argon2idPasswordHasher();
        _pepper = pepper;
    }

    public PasswordHashRecord HashPassword(string password)
    {
        var hash = _inner.Hash(password, _pepper);
        return new PasswordHashRecord(hash.Encoded, hash.ParametersJson);
    }

    public bool VerifyPassword(string password, PasswordHashRecord stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return _inner.Verify(
            password,
            _pepper,
            new PasswordHash(stored.Hash, Argon2idAlgorithm, stored.Parameters));
    }
}