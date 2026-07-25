using GamePatchKit.Core.Globbing;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Tests.Globbing;

public class TestFileSelector
{
    private static GlobPattern Pattern(string text)
    {
        Assert.True(GlobPattern.TryParse(text, out GlobPattern? pattern, out string errorCode), errorCode);
        return pattern!;
    }

    [Fact]
    public void ExcludeAlwaysWinsOverInclude()
    {
        var include = new List<GlobPattern> { Pattern("**/*.json") };
        var exclude = new List<GlobPattern> { Pattern("**/*.generated.json") };
        var candidates = new List<string> { "a.json", "a.generated.json" };

        FileSelector.SelectionResult result = FileSelector.Select(candidates, include, exclude, new List<FileSelector.GroupDefinition>());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Single(result.SelectedFiles);
        Assert.Equal("a.json", result.SelectedFiles[0].Path);
    }

    [Fact]
    public void UnmatchedFilesFallBackToDefaultGroup()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var exclude = new List<GlobPattern>();
        var groups = new List<FileSelector.GroupDefinition> { new("core", new List<GlobPattern> { Pattern("core/**/*") }) };
        var candidates = new List<string> { "core/a.json", "misc/b.json" };

        FileSelector.SelectionResult result = FileSelector.Select(candidates, include, exclude, groups);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("core", result.SelectedFiles.Single(f => f.Path == "core/a.json").Group);
        Assert.Equal("default", result.SelectedFiles.Single(f => f.Path == "misc/b.json").Group);
    }

    [Fact]
    public void FileMatchingTwoGroupsIsAnError()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var exclude = new List<GlobPattern>();
        var groups = new List<FileSelector.GroupDefinition>
        {
            new("core", new List<GlobPattern> { Pattern("shared/**/*") }),
            new("extra", new List<GlobPattern> { Pattern("shared/**/*") }),
        };
        var candidates = new List<string> { "shared/a.json" };

        FileSelector.SelectionResult result = FileSelector.Select(candidates, include, exclude, groups);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == GlobErrorCodes.AmbiguousGroupMatch);
        Assert.Empty(result.SelectedFiles);
    }

    [Fact]
    public void ResultIsOrdinalSortedRegardlessOfCandidateInputOrder()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var exclude = new List<GlobPattern>();
        var groups = new List<FileSelector.GroupDefinition>();

        var forward = new List<string> { "b.json", "a.json", "c.json" };
        var shuffled = new List<string> { "c.json", "a.json", "b.json" };

        FileSelector.SelectionResult forwardResult = FileSelector.Select(forward, include, exclude, groups);
        FileSelector.SelectionResult shuffledResult = FileSelector.Select(shuffled, include, exclude, groups);

        var expectedOrder = new[] { "a.json", "b.json", "c.json" };
        Assert.Equal(expectedOrder, forwardResult.SelectedFiles.Select(f => f.Path));
        Assert.Equal(expectedOrder, shuffledResult.SelectedFiles.Select(f => f.Path));
    }

    [Fact]
    public void CaseInsensitiveDuplicateCandidatesAreRejected()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var exclude = new List<GlobPattern>();
        var candidates = new List<string> { "Data/File.json", "data/file.json" };

        FileSelector.SelectionResult result = FileSelector.Select(candidates, include, exclude, new List<FileSelector.GroupDefinition>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PathErrorCodes.CaseInsensitiveDuplicate);
    }

    [Fact]
    public void InvalidCandidatePathIsReportedAndExcluded()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var exclude = new List<GlobPattern>();
        var candidates = new List<string> { "/absolute/path.json", "valid/path.json" };

        FileSelector.SelectionResult result = FileSelector.Select(candidates, include, exclude, new List<FileSelector.GroupDefinition>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PathErrorCodes.Absolute);
    }

    [Fact]
    public void ReservedDefaultGroupNameInCallerSuppliedGroupsThrows()
    {
        var include = new List<GlobPattern> { Pattern("**/*") };
        var groups = new List<FileSelector.GroupDefinition> { new("default", new List<GlobPattern> { Pattern("**/*") }) };

        Assert.Throws<ArgumentException>(() => FileSelector.Select(new List<string> { "a.json" }, include, new List<GlobPattern>(), groups));
    }
}
