namespace Task.Application.Files;

/// <summary>
/// Error codes returned by <see cref="FileLocationPolicy"/> when a UNC path fails validation.
/// </summary>
public enum FileLocationError
{
    Empty,
    NotUnc,
    CredentialsInPath,
    AdminShare,
    InvalidSegment,
    TooLong,
    NotAllowedRoot,
    InvalidFormat,
}

/// <summary>
/// Validation result for a UNC file location.
/// </summary>
public sealed record FileLocationVerdict(
    bool IsValid,
    FileLocationError? Error,
    string? NormalizedPath);

/// <summary>
/// Options controlling UNC path validation.
/// </summary>
public sealed class FileLocationOptions
{
    public const int DefaultMaxLength = 4096;

    public FileLocationOptions(int maxLength = DefaultMaxLength)
    {
        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength),
                "Maximum path length must be at least 1.");
        }

        MaxLength = maxLength;
    }

    public int MaxLength { get; }
}

/// <summary>
/// Pure (no I/O) policy that validates UNC file locations against structural rules
/// and an optional allowlist of trusted roots.
/// </summary>
public static class FileLocationPolicy
{
    private static readonly char[] ForbiddenChars = ['[', ']', ':', '"', '|', '<', '>'];

    /// <summary>
    /// Validates a UNC path. Returns a verdict with error details and the normalized path
    /// when the path is non-empty.
    /// </summary>
    public static FileLocationVerdict ValidateUnc(
        string path,
        IReadOnlyList<string> allowedRoots,
        FileLocationOptions? options = null)
    {
        options ??= new FileLocationOptions();

        if (string.IsNullOrWhiteSpace(path))
        {
            return new FileLocationVerdict(false, FileLocationError.Empty, null);
        }

        string normalized = path.Trim().Replace('/', '\\');

        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new FileLocationVerdict(false, FileLocationError.NotUnc, normalized);
        }

        if (normalized.Contains('@'))
        {
            return new FileLocationVerdict(false, FileLocationError.CredentialsInPath, normalized);
        }

        if (normalized.Contains('?') || normalized.Contains('#'))
        {
            return new FileLocationVerdict(false, FileLocationError.InvalidFormat, normalized);
        }

        string rest = normalized.Substring(2);
        string[] segments = rest.Split('\\');
        string server = segments[0];

        if (server.Length == 0)
        {
            return new FileLocationVerdict(false, FileLocationError.InvalidSegment, normalized);
        }

        if (segments.Length < 2 || segments[1].Length == 0)
        {
            return new FileLocationVerdict(false, FileLocationError.InvalidSegment, normalized);
        }

        string share = segments[1];

        if (HasForbiddenChars(server) || HasForbiddenChars(share))
        {
            return new FileLocationVerdict(false, FileLocationError.InvalidSegment, normalized);
        }

        if (share.EndsWith('$'))
        {
            return new FileLocationVerdict(false, FileLocationError.AdminShare, normalized);
        }

        if (normalized.Length > options.MaxLength)
        {
            return new FileLocationVerdict(false, FileLocationError.TooLong, normalized);
        }

        string normalizedWithTrailing = NormalizeTrailing(normalized, server, share);

        if (allowedRoots is not null && allowedRoots.Count > 0)
        {
            if (!MatchesAnyRoot(normalizedWithTrailing, allowedRoots))
            {
                return new FileLocationVerdict(false, FileLocationError.NotAllowedRoot, normalizedWithTrailing);
            }
        }

        return new FileLocationVerdict(true, null, normalizedWithTrailing);
    }

    private static bool HasForbiddenChars(string segment)
    {
        foreach (char c in segment)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }

            foreach (char forbidden in ForbiddenChars)
            {
                if (c == forbidden)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeTrailing(string normalized, string server, string share)
    {
        int rootEnd = 2 + server.Length + 1 + share.Length;

        if (normalized.Length > rootEnd)
        {
            return normalized.TrimEnd('\\');
        }

        return normalized;
    }

    private static bool MatchesAnyRoot(string normalizedPath, IReadOnlyList<string> allowedRoots)
    {
        foreach (string rawRoot in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(rawRoot))
            {
                continue;
            }

            string root = rawRoot.Trim().Replace('/', '\\').TrimEnd('\\');

            if (root.Length == 0)
            {
                continue;
            }

            if (normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedPath.Length == root.Length)
                {
                    return true;
                }

                if (normalizedPath[root.Length] == '\\')
                {
                    return true;
                }
            }
        }

        return false;
    }
}
