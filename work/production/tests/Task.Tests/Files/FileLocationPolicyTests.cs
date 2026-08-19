using Task.Application.Files;

namespace Task.Tests.Files;

public sealed class FileLocationPolicyTests
{
    private static readonly string[] NoRoots = [];
    private static readonly string[] SingleRoot = [@"\\srv\share"];

    [Fact]
    public void ValidateUnc_NullPath_ReturnsEmpty()
    {
        var verdict = FileLocationPolicy.ValidateUnc(null!, NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.Empty, verdict.Error);
        Assert.Null(verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_EmptyString_ReturnsEmpty()
    {
        var verdict = FileLocationPolicy.ValidateUnc("", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.Empty, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_WhitespaceOnly_ReturnsEmpty()
    {
        var verdict = FileLocationPolicy.ValidateUnc("   ", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.Empty, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_DriveLetterPath_ReturnsNotUnc()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"C:\work\file.txt", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.NotUnc, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_RelativePath_ReturnsNotUnc()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"srv\share\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.NotUnc, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ContainsAtSign_ReturnsCredentialsInPath()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\user@srv\share\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.CredentialsInPath, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ContainsQuestionMark_ReturnsInvalidFormat()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file?query", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidFormat, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ContainsHash_ReturnsInvalidFormat()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file#fragment", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidFormat, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_EmptyServerSegment_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\\share\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_MissingShare_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_EmptyShare_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ServerWithSpace_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv name\share\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ServerWithColon_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv:na\share\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_ShareWithBracket_ReturnsInvalidSegment()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\sha[re\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.InvalidSegment, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_AdminShare_ReturnsAdminShare()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share$\file", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.AdminShare, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_AdminShareAtRoot_ReturnsAdminShare()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\C$", NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.AdminShare, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_DollarMidShare_IsAllowed()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\sha$re\file", NoRoots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_ExceedsMaxLength_ReturnsTooLong()
    {
        string path = @"\\srv\share\" + new string('a', 4096);

        var verdict = FileLocationPolicy.ValidateUnc(path, NoRoots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.TooLong, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_AtMaxLength_IsValid()
    {
        string path = @"\\srv\share\" + new string('a', 4084);

        var verdict = FileLocationPolicy.ValidateUnc(path, NoRoots);

        Assert.Equal(4096, path.Length);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_CustomMaxLength_EnforcesLimit()
    {
        var opts = new FileLocationOptions(maxLength: 30);
        string path = @"\\srv\share\some\longer\path\file.txt";

        var verdict = FileLocationPolicy.ValidateUnc(path, NoRoots, opts);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.TooLong, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_OptionsWithZeroMaxLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileLocationOptions(maxLength: 0));
    }

    [Fact]
    public void ValidateUnc_OutsideAllowedRoot_ReturnsNotAllowedRoot()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\other\file", SingleRoot);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.NotAllowedRoot, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_SimilarPrefixRoot_ReturnsNotAllowedRoot()
    {
        string[] roots = [@"\\srv\share"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share1x\file", roots);

        Assert.False(verdict.IsValid);
        Assert.Equal(FileLocationError.NotAllowedRoot, verdict.Error);
    }

    [Fact]
    public void ValidateUnc_InsideAllowedRoot_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\sub\file.txt", SingleRoot);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share\sub\file.txt", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_ExactRootMatch_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share", SingleRoot);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_RootWithTrailingSlashMatchesPath()
    {
        string[] roots = [@"\\srv\share\"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file", roots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_PathWithTrailingSlashMatchesRoot()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file\", SingleRoot);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_CaseInsensitiveRootMatch_IsValid()
    {
        string[] roots = [@"\\SRV\SHARE"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file", roots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_EmptyAllowedRoots_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file", NoRoots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_EmptyRootInList_IsIgnored()
    {
        string[] roots = ["", @"\\srv\share"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file", roots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_ValidUncPath_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\server\share\folder\file.txt", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Null(verdict.Error);
        Assert.Equal(@"\\server\share\folder\file.txt", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_ShareRootWithoutTrailingSlash_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_ShareRootWithTrailingSlash_Normalized()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_ForwardSlashes_NormalizedToBackslashes()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"//srv/share/folder/file", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share\folder\file", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_LeadingAndTrailingWhitespace_Trimmed()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"  \\srv\share\file  ", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share\file", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_MultipleTrailingSlashes_NormalizedToShareRoot()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\\\", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_SubDirectory_TrailingSlashTrimmed()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\dir\\", NoRoots);

        Assert.True(verdict.IsValid);
        Assert.Equal(@"\\srv\share\dir", verdict.NormalizedPath);
    }

    [Fact]
    public void ValidateUnc_LongValidPath_IsValid()
    {
        string path = @"\\srv\share\" + string.Join(@"\", Enumerable.Range(0, 50).Select(i => $"folder{i}"));

        var verdict = FileLocationPolicy.ValidateUnc(path, NoRoots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_DeepSubShare_IsValid()
    {
        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\a\b\c\d\file.txt", NoRoots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_MultipleRoots_MatchesSecond()
    {
        string[] roots = [@"\\srv1\share1", @"\\srv2\share2"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv2\share2\file", roots);

        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void ValidateUnc_MixedSlashesInRoot_Normalized()
    {
        string[] roots = [@"//srv/share"];

        var verdict = FileLocationPolicy.ValidateUnc(@"\\srv\share\file", roots);

        Assert.True(verdict.IsValid);
    }
}
