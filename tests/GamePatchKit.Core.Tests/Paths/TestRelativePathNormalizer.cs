using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Tests.Paths;

public class TestRelativePathNormalizer
{
    public static IEnumerable<object[]> ValidPaths()
    {
        yield return new object[] { "a/b/c.json" };
        yield return new object[] { "single.json" };
        yield return new object[] { ".hidden/file.json" };
        yield return new object[] { "a/b..c/d" };
    }

    public static IEnumerable<object[]> InvalidPaths()
    {
        yield return new object[] { "", PathErrorCodes.Empty };
        yield return new object[] { "/absolute/path", PathErrorCodes.Absolute };
        yield return new object[] { "C:/windows/path", PathErrorCodes.Absolute };
        yield return new object[] { "a\\b", PathErrorCodes.ContainsBackslash };
        yield return new object[] { "a/./b", PathErrorCodes.DotSegment };
        yield return new object[] { "a/../b", PathErrorCodes.DotDotSegment };
        yield return new object[] { "a//b", PathErrorCodes.EmptySegment };
        yield return new object[] { "a/b/", PathErrorCodes.EmptySegment };
        yield return new object[] { "a/b\0c", PathErrorCodes.ContainsNul };
    }

    [Theory]
    [MemberData(nameof(ValidPaths))]
    public void AcceptsValidRelativePaths(string path)
    {
        bool ok = RelativePathNormalizer.TryNormalize(path, out string normalized, out string errorCode);

        Assert.True(ok, errorCode);
        Assert.Equal(path, normalized);
    }

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void RejectsInvalidRelativePaths(string path, string expectedErrorCode)
    {
        bool ok = RelativePathNormalizer.TryNormalize(path, out _, out string errorCode);

        Assert.False(ok);
        Assert.Equal(expectedErrorCode, errorCode);
    }

    [Fact]
    public void NormalizesDecomposedFormToPrecomposedForm()
    {
        string nfd = "cafe\u0301.json";
        string nfc = "café.json";

        bool ok = RelativePathNormalizer.TryNormalize(nfd, out string normalized, out _);

        Assert.True(ok);
        Assert.NotEqual(nfd, normalized);
        Assert.Equal(nfc.Normalize(System.Text.NormalizationForm.FormC), normalized);
    }

    [Fact]
    public void HiddenSegmentDetectionRequiresLeadingDot()
    {
        Assert.True(RelativePathNormalizer.IsHiddenSegment(".git"));
        Assert.False(RelativePathNormalizer.IsHiddenSegment("git"));
        Assert.False(RelativePathNormalizer.IsHiddenSegment(""));
    }

    [Fact]
    public void FindsCaseInsensitiveDuplicatesRegardlessOfInputOrder()
    {
        var forward = new[] { "a/File.txt", "a/other.txt", "a/file.txt" };
        var shuffled = new[] { "a/file.txt", "a/other.txt", "a/File.txt" };

        IReadOnlyList<IReadOnlyList<string>> forwardDuplicates = RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(forward);
        IReadOnlyList<IReadOnlyList<string>> shuffledDuplicates = RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(shuffled);

        Assert.Single(forwardDuplicates);
        Assert.Equal(2, forwardDuplicates[0].Count);
        Assert.Single(shuffledDuplicates);
        Assert.Equal(2, shuffledDuplicates[0].Count);
    }

    [Fact]
    public void ByteIdenticalDuplicatesAreAlsoDetected()
    {
        var paths = new[] { "a/file.txt", "a/file.txt" };

        IReadOnlyList<IReadOnlyList<string>> duplicates = RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(paths);

        Assert.Single(duplicates);
        Assert.Equal(2, duplicates[0].Count);
    }
}
