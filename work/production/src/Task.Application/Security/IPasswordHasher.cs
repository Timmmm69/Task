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
}

/// <summary>
/// Persisted password hash as it appears in the database: the encoded value and the JSON
/// algorithm parameters. The hashing algorithm itself is fixed by the schema
/// (password_algorithm = 'argon2id') and is not part of the record.
/// </summary>
public sealed record PasswordHashRecord(string Hash, string Parameters);