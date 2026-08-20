namespace Task.Application.Security;

/// <summary>
/// Application-facing password hashing contract. The pepper value is an implementation
/// detail of the concrete adapter and never crosses this boundary; only the encoded hash
/// and its algorithm parameters are exposed for persistence.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Creates a fresh salted hash for the given password, ready to be persisted.
    /// </summary>
    PasswordHashRecord HashPassword(string password);

    /// <summary>
    /// Verifies a password against a stored hash record. Returns false for any mismatch or
    /// malformed record; never throws for invalid stored content.
    /// </summary>
    bool VerifyPassword(string password, PasswordHashRecord stored);

    /// <summary>
    /// Precomputed hash of a fixed constant password, used for timing-equivalent verification
    /// when the account does not exist (see <see cref="LoginService"/>). The concrete adapter
    /// computes it once at construction time; the default implementation throws because only a
    /// concrete adapter knows how to hash. Hashers used in the login flow must override it.
    /// </summary>
    PasswordHashRecord DummyPasswordHash
    {
        get => throw new NotSupportedException(
            "Password hasher adapters must provide DummyPasswordHash for unknown-account verification.");
    }
}

/// <summary>
/// Persisted password hash as it appears in the database: the encoded value and the JSON
/// algorithm parameters. The hashing algorithm itself is fixed by the schema
/// (password_algorithm = 'argon2id') and is not part of the record.
/// </summary>
public sealed record PasswordHashRecord(string Hash, string Parameters);