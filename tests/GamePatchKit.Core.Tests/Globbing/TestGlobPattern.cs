using GamePatchKit.Core.Globbing;

namespace GamePatchKit.Core.Tests.Globbing;

public class TestGlobPattern
{
    public static IEnumerable<object[]> MatchCases()
    {
        // pattern, path, expectedMatch
        yield return new object[] { "**/*.json", "a.json", true };
        yield return new object[] { "**/*.json", "dir/a.json", true };
        yield return new object[] { "**/*.json", "dir/sub/a.json", true };
        yield return new object[] { "a/**/b.json", "a/b.json", true };
        yield return new object[] { "a/**/b.json", "a/x/b.json", true };
        yield return new object[] { "a/**/b.json", "a/x/y/b.json", true };
        yield return new object[] { "a/**/b.json", "other/b.json", false };
        yield return new object[] { "*.json", "a.json", true };
        yield return new object[] { "*.json", "dir/a.json", false };
        yield return new object[] { "core/*.json", "core/a.json", true };
        yield return new object[] { "core/*.json", "core/sub/a.json", false };
        yield return new object[] { "data/File.json", "data/file.json", false };
        yield return new object[] { "data/file.json", "data/file.json", true };
        yield return new object[] { "**", "anything/at/all.txt", true };
        yield return new object[] { "**", "top.txt", true };
    }

    public static IEnumerable<object[]> HiddenSegmentCases()
    {
        yield return new object[] { "**/*.json", ".config/a.json", false };
        yield return new object[] { "*.json", ".a.json", false };
        yield return new object[] { ".*.json", ".a.json", true };
        yield return new object[] { ".config/**/*.json", ".config/a.json", true };
        yield return new object[] { ".config/**/*.json", ".config/.nested/a.json", false };
    }

    public static IEnumerable<object[]> RejectedPatterns()
    {
        yield return new object[] { "", GlobErrorCodes.Empty };
        yield return new object[] { "/absolute/*.json", GlobErrorCodes.Absolute };
        yield return new object[] { "a\\b*.json", GlobErrorCodes.ContainsBackslash };
        yield return new object[] { "a//b*.json", GlobErrorCodes.EmptySegment };
        yield return new object[] { "./a*.json", GlobErrorCodes.DotSegment };
        yield return new object[] { "../a*.json", GlobErrorCodes.DotSegment };
        yield return new object[] { "foo**bar/*.json", GlobErrorCodes.PartialRecursive };
        yield return new object[] { "a/**bar", GlobErrorCodes.PartialRecursive };
        yield return new object[] { "!negated/*.json", GlobErrorCodes.Negation };
        yield return new object[] { "a?.json", GlobErrorCodes.DisallowedCharacter };
        yield return new object[] { "a[bc].json", GlobErrorCodes.DisallowedCharacter };
        yield return new object[] { "a{b,c}.json", GlobErrorCodes.DisallowedCharacter };
    }

    [Theory]
    [MemberData(nameof(MatchCases))]
    [MemberData(nameof(HiddenSegmentCases))]
    public void MatchesExpectedPaths(string pattern, string path, bool expectedMatch)
    {
        bool parsed = GlobPattern.TryParse(pattern, out GlobPattern? glob, out string errorCode);
        Assert.True(parsed, errorCode);

        Assert.Equal(expectedMatch, glob!.IsMatch(path));
    }

    [Theory]
    [MemberData(nameof(RejectedPatterns))]
    public void RejectsUnsupportedSyntax(string pattern, string expectedErrorCode)
    {
        bool parsed = GlobPattern.TryParse(pattern, out GlobPattern? glob, out string errorCode);

        Assert.False(parsed);
        Assert.Null(glob);
        Assert.Equal(expectedErrorCode, errorCode);
    }

    [Fact]
    public void NfcNormalizesPatternSoItMatchesAnNfcCandidateEvenWhenWrittenAsNfd()
    {
        // "e" + combining acute accent (NFD) in the pattern; precomposed U+00E9 (NFC) in the
        // candidate - both spelled with explicit escapes so the two forms are unambiguous.
        string nfdPattern = "café/*.json";
        string nfcCandidatePath = "café/a.json";

        bool parsed = GlobPattern.TryParse(nfdPattern, out GlobPattern? pattern, out string errorCode);
        Assert.True(parsed, errorCode);

        Assert.True(pattern!.IsMatch(nfcCandidatePath));
    }
}
