using Task.Infrastructure.Identity;

namespace Task.Tests;

public sealed class Argon2idPasswordHasherTests
{
    [Fact]
    public void Hash_UsesApprovedArgon2idParametersAndRandomSalt()
    {
        var hasher = new Argon2idPasswordHasher();

        var first = hasher.Hash("temporary-password-for-bootstrap", "bootstrap-pepper-value");
        var second = hasher.Hash("temporary-password-for-bootstrap", "bootstrap-pepper-value");

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=2$", first.Encoded, StringComparison.Ordinal);
        Assert.NotEqual(first.Encoded, second.Encoded);
        Assert.Equal("argon2id", first.Algorithm);
        Assert.Equal("{\"memoryKiB\":65536,\"iterations\":3,\"parallelism\":2,\"hashLength\":32,\"saltLength\":32,\"version\":19}", first.ParametersJson);
        Assert.DoesNotContain("temporary-password", first.Encoded, StringComparison.Ordinal);
        Assert.True(hasher.Verify("temporary-password-for-bootstrap", "bootstrap-pepper-value", first));
        Assert.False(hasher.Verify("wrong-password-for-bootstrap", "bootstrap-pepper-value", first));
        Assert.False(hasher.Verify("temporary-password-for-bootstrap", "wrong-bootstrap-pepper", first));
    }

    [Theory]
    [InlineData("", "valid-bootstrap-pepper")]
    [InlineData("valid-bootstrap-password", "")]
    public void Hash_RejectsEmptySecrets(string password, string pepper)
    {
        Assert.Throws<ArgumentException>(() => new Argon2idPasswordHasher().Hash(password, pepper));
    }
}
