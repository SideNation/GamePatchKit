using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestBuildCommand
{
    private readonly BuildCommand _sut = new();

    [Fact]
    public void Execute_OutputIsInsideRepository_ThrowsWithoutCreatingOutput()
    {
        string repositoryRoot = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);

        try
        {
            GitTestRepository.RunGitAt(repositoryRoot, "init", "--quiet");
            string sourcePath = Path.Combine(repositoryRoot, "data");
            string outputPath = Path.Combine(repositoryRoot, "patches");
            Directory.CreateDirectory(sourcePath);

            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

            Assert.Contains("Git 저장소 바깥", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Execute_SourceIsSymbolicLinkAndOutputIsOutsideRepository_DoesNotReportRepositoryOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        string repositoryRoot = Path.Combine(testRoot, "repository");
        string sourceTargetPath = Path.Combine(repositoryRoot, "data");
        string sourceLinkPath = Path.Combine(testRoot, "source-link");
        string outputPath = Path.Combine(testRoot, "patches");
        Directory.CreateDirectory(sourceTargetPath);

        try
        {
            GitTestRepository.RunGitAt(repositoryRoot, "init", "--quiet");
            Directory.CreateSymbolicLink(sourceLinkPath, sourceTargetPath);

            BuildException exception = Assert.Throws<BuildException>(
                () => _sut.Execute(new BuildArguments(sourceLinkPath, outputPath)));

            Assert.DoesNotContain("Git 저장소 바깥", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Execute_PreviousManifestHasDifferentSourcePath_ThrowsWithoutChangingManifest()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 1);
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = 1,
                SourcePath = "other",
                SourceCommit = "abc123",
                Groups = Array.Empty<ManifestGroup>()
            });
        string manifestPath = Path.Combine(outputPath, "manifest.json");
        byte[] before = File.ReadAllBytes(manifestPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains("데이터 루트가 이전 빌드와 다릅니다", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void Execute_ConfigurationIsUntracked_ThrowsBeforeReadingYamlWithoutCreatingOutput()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile("data/group/file.txt", "data");
        testRepository.CommitAll("initial");
        testRepository.WriteFile("data/gamepatchkit.yml", "invalid: [");
        string outputPath = testRepository.GetExternalPath("patches");

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("Git 추적", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public void Execute_TrackedSourceIsDirty_ThrowsWithoutCreatingOutput()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 1);
        testRepository.WriteFile("data/group/file.txt", "changed");
        string outputPath = testRepository.GetExternalPath("patches");

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("커밋되지 않은 추적 파일", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("untracked")]
    [InlineData("deleted")]
    public void Execute_GroupHasNoTrackedEntry_ThrowsSameGuidanceWithoutCreatingOutput(string scenario)
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: group
            """);

        if (scenario == "deleted")
        {
            testRepository.WriteFile("data/group/file.txt", "data");
        }

        testRepository.CommitAll("initial");

        if (scenario == "untracked")
        {
            testRepository.WriteFile("data/group/untracked.txt", "data");
        }
        else if (scenario == "deleted")
        {
            testRepository.DeleteFile("data/group/file.txt");
            Directory.Delete(testRepository.GetRepositoryPath("data/group"));
            testRepository.CommitAll("delete group entry");
        }

        string outputPath = testRepository.GetExternalPath("patches");

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("group", exception.Message, StringComparison.Ordinal);
        Assert.Contains("yaml에서 그룹을 제거", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public void Execute_NestedGroupOwnsDeepestFile_OuterGroupIsReportedEmpty()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "gamepatchkit.yml",
            """
            groups:
              - id: data
              - id: data/maps
            """);
        testRepository.WriteFile("data/maps/map.bin", "map");
        testRepository.CommitAll("initial");
        string outputPath = testRepository.GetExternalPath("patches");

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.RepositoryPath, outputPath)));

        Assert.Contains("그룹 'data'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("그룹 'data/maps'", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public void Execute_GroupContainsOnlyTrackedSymbolicLink_ReportsNoTrackedEntry()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "gamepatchkit.yml",
            """
            groups:
              - id: group
            """);
        testRepository.WriteFile("target.txt", "target");
        string linkPath = testRepository.GetRepositoryPath("group/link\\name");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        File.CreateSymbolicLink(linkPath, "../target.txt");
        testRepository.CommitAll("initial");
        string outputPath = testRepository.GetExternalPath("patches");

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.RepositoryPath, outputPath)));

        Assert.Contains("yaml에서 그룹을 제거", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Git 추적 경로가 올바르지 않습니다", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public void Execute_GroupVersionIsLowerThanPrevious_ThrowsWithoutChangingManifest()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 0);
        string outputPath = testRepository.GetExternalPath("patches");
        string currentCommit = testRepository.RunGit("rev-parse", "HEAD").TrimEnd('\r', '\n');
        byte[] before = WritePreviousManifest(outputPath, previousVersion: 1, currentCommit);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("이전 성공 버전보다 큰 값", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
    }

    [Fact]
    public void Execute_IncrementalGroupPreviousCommitIsMissing_ThrowsWithoutChangingManifest()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 1);
        string outputPath = testRepository.GetExternalPath("patches");
        byte[] before = WritePreviousManifest(outputPath, previousVersion: 1, new string('f', 40));

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Equal("이전 상태가 없습니다.", exception.Message);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
    }

    [Fact]
    public void Execute_GroupVersionIncreasedPreviousCommitIsMissing_DoesNotRequirePreviousCommit()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 2);
        string outputPath = testRepository.GetExternalPath("patches");
        byte[] before = WritePreviousManifest(outputPath, previousVersion: 1, new string('f', 40));

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("아직 구현되지 않았습니다", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("이전 상태", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
    }

    private static GitTestRepository CreateVersionedRepository(int currentVersion)
    {
        var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            $$"""
            groups:
              - id: group
                version: {{currentVersion}}
                packing: file
                compression: none
            """);
        testRepository.WriteFile("data/group/file.txt", "data");
        testRepository.CommitAll("initial");
        return testRepository;
    }

    private static byte[] WritePreviousManifest(string outputPath, int previousVersion, string sourceCommit)
    {
        string entryVersion = $"{previousVersion}.0";
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = 1,
                SourcePath = "data",
                SourceCommit = sourceCommit,
                Groups = new[]
                {
                    new ManifestGroup
                    {
                        Id = "group",
                        Version = previousVersion,
                        Packing = PackingKind.File,
                        Compression = CompressionKind.None,
                        Entries = new[]
                        {
                            new ManifestEntry
                            {
                                Path = "file.txt",
                                Version = entryVersion,
                                Size = 4,
                                Source = EntrySource.File,
                                Name = $"files/group/{previousVersion}/file.txt.v{entryVersion}",
                                StoredSize = 4,
                                Checksum = new string('a', 64)
                            }
                        }
                    }
                }
            });

        return File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));
    }
}