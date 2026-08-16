namespace Task.Application.Security;

/// <summary>
/// Stable canonical permission identifier in the Resource.Action form.
/// The identity/authorization persistence increment owns catalog seeding and grants.
/// </summary>
public sealed record PermissionCode
{
    private PermissionCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PermissionCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Permission code is required.", nameof(value));
        }

        var normalized = value.Trim();
        var separator = normalized.IndexOf('.');
        if (separator < 1 || separator != normalized.LastIndexOf('.') || separator == normalized.Length - 1)
        {
            throw new ArgumentException("Permission code must use the Resource.Action form.", nameof(value));
        }

        if (!normalized.All(static character => char.IsLetterOrDigit(character) || character is '.' or '_'))
        {
            throw new ArgumentException("Permission code contains unsupported characters.", nameof(value));
        }

        return new PermissionCode(normalized);
    }

    public override string ToString() => Value;
}
