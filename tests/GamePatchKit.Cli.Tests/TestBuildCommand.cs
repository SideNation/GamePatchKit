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
    public void Execute_FirstGroupBuild_WritesArchiveAndManifestFromActualLayout()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 4,
            ("b.bin", "second"),
            ("a.bin", "first"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        string currentCommit = testRepository.RunGit("rev-parse", "HEAD").TrimEnd('\r', '\n');

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        PatchManifest manifest = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));
        ManifestGroup group = Assert.Single(manifest.Groups);
        ManifestArchive archive = Assert.IsType<ManifestArchive>(group.Archive);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("data", manifest.SourcePath);
        Assert.Equal(currentCommit, manifest.SourceCommit);
        Assert.Equal("group", group.Id);
        Assert.Equal(4, group.Version);
        Assert.Equal(PackingKind.Group, group.Packing);
        Assert.Equal(CompressionKind.None, group.Compression);
        Assert.Equal("archives/group/4.gpka", archive.Name);
        Assert.Equal("firstsecond"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, archive.Name)));
        Assert.Collection(
            group.Entries,
            entry => AssertArchiveEntry(entry, "a.bin", "4.0", offset: 0, length: 5),
            entry => AssertArchiveEntry(entry, "b.bin", "4.0", offset: 5, length: 6));
        Assert.Equal(
            new[] { "archives/group/4.gpka", "manifest.json" },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_GroupVersionIncreased_RebuildsEntriesAtRevisionZeroWithoutPreviousCommit()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 2,
            ("file.txt", "current"),
            ("new.txt", "new"));
        string outputPath = testRepository.GetExternalPath("patches");
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = 1,
                SourcePath = "data",
                SourceCommit = new string('f', 40),
                Groups = new[]
                {
                    new ManifestGroup
                    {
                        Id = "group",
                        Version = 1,
                        Packing = PackingKind.Group,
                        Compression = CompressionKind.None,
                        Archive = new ManifestArchive
                        {
                            Name = "archives/group/1.gpka",
                            PayloadSize = 3,
                            StoredSize = 3,
                            Checksum = new string('a', 64)
                        },
                        Entries = new[]
                        {
                            new ManifestEntry
                            {
                                Path = "file.txt",
                                Version = "1.7",
                                Size = 3,
                                Source = EntrySource.File,
                                Name = "files/group/1/file.txt.v1.7",
                                StoredSize = 3,
                                Checksum = new string('b', 64)
                            }
                        }
                    }
                }
            });

        _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        PatchManifest manifest = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));
        ManifestGroup group = Assert.Single(manifest.Groups);
        Assert.Equal(2, group.Version);
        Assert.Equal("archives/group/2.gpka", Assert.IsType<ManifestArchive>(group.Archive).Name);
        Assert.Collection(
            group.Entries,
            entry => AssertArchiveEntry(entry, "file.txt", "2.0", offset: 0, length: 7),
            entry => AssertArchiveEntry(entry, "new.txt", "2.0", offset: 7, length: 3));
        Assert.All(group.Entries, entry => Assert.Equal(EntrySource.Archive, entry.Source));
        Assert.Equal(
            new[] { "archives/group/2.gpka", "manifest.json" },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_ArchiveExistsWithoutManifestAndMatchesCandidate_ReusesArchiveAndSucceeds()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 3,
            ("a.txt", "alpha"),
            ("b.txt", "beta"));
        string outputPath = testRepository.GetExternalPath("patches");
        string archivePath = GetOutputPath(outputPath, "archives/group/3.gpka");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        byte[] archiveBytes = "alphabeta"u8.ToArray();
        File.WriteAllBytes(archivePath, archiveBytes);
        File.SetLastWriteTimeUtc(archivePath, new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        DateTime recordedTimestamp = File.GetLastWriteTimeUtc(archivePath);

        _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        Assert.NotNull(ManifestStore.ReadPrevious(outputPath));
        Assert.Equal(archiveBytes, File.ReadAllBytes(archivePath));
        Assert.Equal(recordedTimestamp, File.GetLastWriteTimeUtc(archivePath));
        Assert.Equal(
            new[] { "archives/group/3.gpka", "manifest.json" },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_RemovedGroupReturnsWithDifferentArchive_ThrowsWithoutChangingOutput()
    {
        using var testRepository = CreateGroupRepository(currentVersion: 1, ("file.txt", "new"));
        string outputPath = testRepository.GetExternalPath("patches");
        string currentCommit = testRepository.RunGit("rev-parse", "HEAD").TrimEnd('\r', '\n');
        ManifestStore.WriteAtomically(
            outputPath,
            new PatchManifest
            {
                SchemaVersion = 1,
                SourcePath = "data",
                SourceCommit = currentCommit,
                Groups = Array.Empty<ManifestGroup>()
            });
        string manifestPath = Path.Combine(outputPath, "manifest.json");
        string archivePath = GetOutputPath(outputPath, "archives/group/1.gpka");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        File.WriteAllBytes(archivePath, "old"u8.ToArray());
        byte[] manifestBefore = File.ReadAllBytes(manifestPath);
        byte[] archiveBefore = File.ReadAllBytes(archivePath);
        IReadOnlyList<string> filesBefore = GetOutputFiles(outputPath);

        BuildException exception = Assert.Throws<BuildException>(() =>
            _sut.Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath)));

        Assert.Contains("그룹 버전 충돌", exception.Message, StringComparison.Ordinal);
        Assert.Equal(manifestBefore, File.ReadAllBytes(manifestPath));
        Assert.Equal(archiveBefore, File.ReadAllBytes(archivePath));
        Assert.Equal(filesBefore, GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_GroupFileModifiedTwice_CreatesOnlySuccessiveOverlaysAndNoOpIsStable()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 1,
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        ManifestGroup initialGroup = GetOnlyGroup(outputPath);
        ManifestEntry initialB = GetEntry(initialGroup, "b.txt");
        ManifestArchive initialArchive = Assert.IsType<ManifestArchive>(initialGroup.Archive);
        string archivePath = GetOutputPath(outputPath, initialArchive.Name);
        byte[] archiveBytes = File.ReadAllBytes(archivePath);
        DateTime archiveTimestamp = File.GetLastWriteTimeUtc(archivePath);
        testRepository.WriteFile("data/group/a.txt", "a1");
        testRepository.CommitAll("modify a once");

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup firstIncrement = GetOnlyGroup(outputPath);
        ManifestEntry firstA = GetEntry(firstIncrement, "a.txt");
        Assert.Equal("1.1", firstA.Version);
        Assert.Equal(EntrySource.File, firstA.Source);
        Assert.Equal("files/group/1/a.txt.v1.1", firstA.Name);
        Assert.Equal("a1"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, firstA.Name!)));
        AssertManifestEntryEqual(initialB, GetEntry(firstIncrement, "b.txt"));
        Assert.Equal(initialArchive.Checksum, Assert.IsType<ManifestArchive>(firstIncrement.Archive).Checksum);
        Assert.Equal(archiveBytes, File.ReadAllBytes(archivePath));
        Assert.Equal(archiveTimestamp, File.GetLastWriteTimeUtc(archivePath));
        testRepository.WriteFile("data/group/a.txt", "a2");
        testRepository.CommitAll("modify a twice");

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup secondIncrement = GetOnlyGroup(outputPath);
        ManifestEntry secondA = GetEntry(secondIncrement, "a.txt");
        Assert.Equal("1.2", secondA.Version);
        Assert.Equal("files/group/1/a.txt.v1.2", secondA.Name);
        Assert.Equal("a1"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, firstA.Name!)));
        Assert.Equal("a2"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, secondA.Name!)));
        AssertManifestEntryEqual(initialB, GetEntry(secondIncrement, "b.txt"));
        byte[] manifestBeforeNoOp = File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));
        IReadOnlyDictionary<string, byte[]> outputBeforeNoOp = GetOutputContents(outputPath);

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        Assert.Equal(manifestBeforeNoOp, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
        AssertOutputContentsEqual(outputBeforeNoOp, outputPath);
    }

    [Fact]
    public void Execute_GroupFileAddedAndDeleted_AddsRevisionZeroAndRemovesDeletedEntry()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 1,
            ("a.txt", "a"),
            ("b.txt", "b"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        ManifestEntry previousB = GetEntry(GetOnlyGroup(outputPath), "b.txt");
        testRepository.DeleteFile("data/group/a.txt");
        testRepository.WriteFile("data/group/c.txt", "c");
        testRepository.CommitAll("delete a and add c");

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        Assert.DoesNotContain(group.Entries, entry => entry.Path == "a.txt");
        AssertManifestEntryEqual(previousB, GetEntry(group, "b.txt"));
        ManifestEntry added = GetEntry(group, "c.txt");
        Assert.Equal("1.0", added.Version);
        Assert.Equal(EntrySource.File, added.Source);
        Assert.Equal("files/group/1/c.txt.v1.0", added.Name);
        Assert.Equal("c"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, added.Name!)));
    }

    [Fact]
    public void Execute_DeletingLastGroupFile_ThrowsAndPreservesOutput()
    {
        using var testRepository = CreateGroupRepository(currentVersion: 1, ("file.txt", "data"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.DeleteFile("data/group/file.txt");
        testRepository.CommitAll("delete last file");
        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains("yaml에서 그룹을 제거", exception.Message, StringComparison.Ordinal);
        AssertOutputContentsEqual(outputBefore, outputPath);
    }

    [Fact]
    public void Execute_LaterIncrementalGroupHasMissingArchive_FailsBeforeBuildingEarlierNewGroup()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: z-existing
                version: 1
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/z-existing/file.txt", "existing");
        testRepository.CommitAll("initial");
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        string inheritedArchiveName = Assert.IsType<ManifestArchive>(GetOnlyGroup(outputPath).Archive).Name;
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: a-new
                version: 1
                packing: group
                compression: none
              - id: z-existing
                version: 1
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/a-new/file.txt", "new");
        testRepository.CommitAll("add new group");
        File.Delete(GetOutputPath(outputPath, inheritedArchiveName));
        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains(inheritedArchiveName, exception.Message, StringComparison.Ordinal);
        AssertOutputContentsEqual(outputBefore, outputPath);
        Assert.False(File.Exists(GetOutputPath(outputPath, "archives/a-new/1.gpka")));
    }

    [Theory]
    [InlineData("archive", "missing", "없습니다")]
    [InlineData("archive", "truncated", "크기가 다릅니다")]
    [InlineData("overlay", "missing", "없습니다")]
    [InlineData("overlay", "truncated", "크기가 다릅니다")]
    public void Execute_InheritedArtifactIsMissingOrTruncated_FailsBeforeWritingChangedFile(
        string artifactKind,
        string damageKind,
        string expectedReason)
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 1,
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        ManifestGroup previousGroup = BuildGroupWithOverlay(testRepository, outputPath);
        testRepository.WriteFile("data/group/b.txt", "b-changed");
        testRepository.CommitAll("modify b");
        string artifactName = artifactKind == "archive"
            ? Assert.IsType<ManifestArchive>(previousGroup.Archive).Name
            : GetEntry(previousGroup, "a.txt").Name!;
        string artifactPath = GetOutputPath(outputPath, artifactName);

        if (damageKind == "missing")
        {
            File.Delete(artifactPath);
        }
        else
        {
            File.WriteAllBytes(artifactPath, [0]);
        }

        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains(artifactName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
        AssertOutputContentsEqual(outputBefore, outputPath);
        Assert.DoesNotContain(GetOutputFiles(outputPath), path => path.Contains("b.txt.v", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("overlay")]
    public void Execute_InheritedArtifactHasSameSizeCorruption_DoesNotReadBytes(string artifactKind)
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 1,
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string outputPath = testRepository.GetExternalPath("patches");
        ManifestGroup previousGroup = BuildGroupWithOverlay(testRepository, outputPath);
        string artifactName = artifactKind == "archive"
            ? Assert.IsType<ManifestArchive>(previousGroup.Archive).Name
            : GetEntry(previousGroup, "a.txt").Name!;
        string artifactPath = GetOutputPath(outputPath, artifactName);
        CorruptSameSize(artifactPath);
        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        AssertOutputContentsEqual(outputBefore, outputPath);
    }

    [Theory]
    [InlineData("file", "none")]
    [InlineData("group", "zstd")]
    public void Execute_GroupSettingChangedAtSameVersion_ThrowsAndSuggestsVersionIncrease(
        string packing,
        string compression)
    {
        using var testRepository = CreateGroupRepository(currentVersion: 1, ("file.txt", "data"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            $$"""
            groups:
              - id: group
                version: 1
                packing: {{packing}}
                compression: {{compression}}
            """);
        testRepository.CommitAll("change group setting");
        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains("group", exception.Message, StringComparison.Ordinal);
        Assert.Contains("그룹 버전을 올리세요", exception.Message, StringComparison.Ordinal);
        AssertOutputContentsEqual(outputBefore, outputPath);
    }

    [Theory]
    [InlineData(true, "1.5")]
    [InlineData(false, "1.6")]
    public void Execute_ExistingHigherOverlay_ReusesSameCandidateOrUsesNextRevision(
        bool hasSameBytes,
        string expectedVersion)
    {
        using var testRepository = CreateGroupRepository(currentVersion: 1, ("file.txt", "original"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.WriteFile("data/group/file.txt", "candidate");
        testRepository.CommitAll("modify file");
        string existingName = "files/group/1/file.txt.v1.5";
        string existingPath = GetOutputPath(outputPath, existingName);
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        byte[] existingBytes = hasSameBytes ? "candidate"u8.ToArray() : "different"u8.ToArray();
        File.WriteAllBytes(existingPath, existingBytes);

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestEntry entry = GetEntry(GetOnlyGroup(outputPath), "file.txt");
        Assert.Equal(expectedVersion, entry.Version);
        Assert.Equal($"files/group/1/file.txt.v{expectedVersion}", entry.Name);
        Assert.Equal(existingBytes, File.ReadAllBytes(existingPath));
        Assert.Equal("candidate"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, entry.Name!)));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("zstd")]
    public void Execute_FirstFileBuild_WritesEveryFileWithoutArchiveAndUsesActualStoredMetadata(
        string compression)
    {
        using var testRepository = CreateFileRepository(
            currentVersion: 3,
            compression,
            ("a.bin", "first"),
            ("nested/b.bin", "second"));
        string outputPath = testRepository.GetExternalPath("patches");

        BuildSummary summary = _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        Assert.Equal(3, group.Version);
        Assert.Equal(PackingKind.File, group.Packing);
        Assert.Equal(
            compression == "none" ? CompressionKind.None : CompressionKind.Zstd,
            group.Compression);
        Assert.Null(group.Archive);
        Assert.Collection(
            group.Entries,
            entry => AssertFileEntry(outputPath, entry, "a.bin", "3.0", size: 5),
            entry => AssertFileEntry(outputPath, entry, "nested/b.bin", "3.0", size: 6));
        Assert.Equal(
            new GroupBuildSummary(
                "group",
                3,
                2,
                IsArchiveCreated: false,
                FileObjectCount: 2,
                group.Entries.Sum(entry => entry.StoredSize!.Value)),
            Assert.Single(summary.Groups));
        Assert.Empty(summary.FileRevisionAdjustments);
        Assert.Equal(
            new[]
            {
                "files/group/3/a.bin.v3.0",
                "files/group/3/nested/b.bin.v3.0",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_FilePackingChangedAddedAndDeleted_WritesOnlyChangedAndNewObjects()
    {
        using var testRepository = CreateFileRepository(
            currentVersion: 1,
            compression: "none",
            ("a.txt", "a0"),
            ("b.txt", "b0"),
            ("c.txt", "c0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        ManifestEntry previousC = GetEntry(GetOnlyGroup(outputPath), "c.txt");
        testRepository.WriteFile("data/group/a.txt", "a1");
        testRepository.DeleteFile("data/group/b.txt");
        testRepository.WriteFile("data/group/d.txt", "d0");
        testRepository.CommitAll("change file group");

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        ManifestEntry changed = GetEntry(group, "a.txt");
        ManifestEntry added = GetEntry(group, "d.txt");
        Assert.Equal("1.1", changed.Version);
        Assert.Equal("a1"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, changed.Name!)));
        Assert.DoesNotContain(group.Entries, entry => entry.Path == "b.txt");
        AssertManifestEntryEqual(previousC, GetEntry(group, "c.txt"));
        Assert.Equal("1.0", added.Version);
        Assert.Equal("d0"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, added.Name!)));
        Assert.Equal(
            new[]
            {
                "files/group/1/a.txt.v1.0",
                "files/group/1/a.txt.v1.1",
                "files/group/1/b.txt.v1.0",
                "files/group/1/c.txt.v1.0",
                "files/group/1/d.txt.v1.0",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_FileRemovedThenReaddedWithDifferentBytes_UsesNextRevisionAndKeepsGroupVersion()
    {
        using var testRepository = CreateFileRepository(
            currentVersion: 1,
            compression: "none",
            ("a.txt", "old"),
            ("b.txt", "keep"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.DeleteFile("data/group/a.txt");
        testRepository.CommitAll("remove a");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.WriteFile("data/group/a.txt", "new");
        testRepository.CommitAll("readd a");

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        ManifestEntry readded = GetEntry(group, "a.txt");
        Assert.Equal(1, group.Version);
        Assert.Equal("1.1", readded.Version);
        Assert.Equal("files/group/1/a.txt.v1.1", readded.Name);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, "files/group/1/a.txt.v1.0")));
        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, readded.Name!)));
    }

    [Theory]
    [InlineData("missing", "없습니다")]
    [InlineData("truncated", "크기가 다릅니다")]
    public void Execute_InheritedFileIsMissingOrTruncated_FailsBeforeWritingChangedFile(
        string damageKind,
        string expectedReason)
    {
        using var testRepository = CreateFileRepository(
            currentVersion: 1,
            compression: "none",
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        string inheritedName = GetEntry(GetOnlyGroup(outputPath), "b.txt").Name!;
        testRepository.WriteFile("data/group/a.txt", "a-changed");
        testRepository.CommitAll("modify a");
        string inheritedPath = GetOutputPath(outputPath, inheritedName);

        if (damageKind == "missing")
        {
            File.Delete(inheritedPath);
        }
        else
        {
            File.WriteAllBytes(inheritedPath, [0]);
        }

        IReadOnlyDictionary<string, byte[]> outputBefore = GetOutputContents(outputPath);

        BuildException exception = Assert.Throws<BuildException>(
            () => _sut.Execute(new BuildArguments(sourcePath, outputPath)));

        Assert.Contains(inheritedName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
        AssertOutputContentsEqual(outputBefore, outputPath);
        Assert.DoesNotContain("files/group/1/a.txt.v1.1", GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_InheritedFileHasSameSizeCorruption_DoesNotReadBytes()
    {
        using var testRepository = CreateFileRepository(
            currentVersion: 1,
            compression: "none",
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        ManifestEntry previousB = GetEntry(GetOnlyGroup(outputPath), "b.txt");
        string inheritedPath = GetOutputPath(outputPath, previousB.Name!);
        testRepository.WriteFile("data/group/a.txt", "a-changed");
        testRepository.CommitAll("modify a");
        CorruptSameSize(inheritedPath);
        byte[] corruptedBytes = File.ReadAllBytes(inheritedPath);

        _sut.Execute(new BuildArguments(sourcePath, outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        Assert.Equal("1.1", GetEntry(group, "a.txt").Version);
        AssertManifestEntryEqual(previousB, GetEntry(group, "b.txt"));
        Assert.Equal(corruptedBytes, File.ReadAllBytes(inheritedPath));
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
    public void Execute_FileGroupVersionIncreasedPreviousCommitIsMissing_RebuildsAtRevisionZero()
    {
        using var testRepository = CreateVersionedRepository(currentVersion: 2);
        string outputPath = testRepository.GetExternalPath("patches");
        _ = WritePreviousManifest(outputPath, previousVersion: 1, new string('f', 40));

        _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        ManifestGroup group = GetOnlyGroup(outputPath);
        Assert.Equal(2, group.Version);
        Assert.Null(group.Archive);
        AssertFileEntry(outputPath, Assert.Single(group.Entries), "file.txt", "2.0", size: 4);
        Assert.Equal(
            new[] { "files/group/2/file.txt.v2.0", "manifest.json" },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Execute_GroupPackingLifecycle_ReturnsExpectedSummariesAndManifest()
    {
        using var testRepository = CreateGroupRepository(
            currentVersion: 1,
            ("a.txt", "a0"),
            ("b.txt", "b0"));
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        var arguments = new BuildArguments(sourcePath, outputPath);

        BuildSummary initial = _sut.Execute(arguments);

        AssertOnlyGroupSummary(initial, version: 1, entries: 2, archiveCreated: true, fileObjects: 0, writtenBytes: 4);
        ManifestGroup initialGroup = GetOnlyGroup(outputPath);
        Assert.Collection(
            initialGroup.Entries,
            entry => AssertArchiveEntry(entry, "a.txt", "1.0", offset: 0, length: 2),
            entry => AssertArchiveEntry(entry, "b.txt", "1.0", offset: 2, length: 2));
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
        byte[] manifestBeforeNoOp = File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));
        IReadOnlyDictionary<string, byte[]> outputBeforeNoOp = GetOutputContents(outputPath);
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int noOpExitCode = Program.Run(
            new[] { "build", "--source", sourcePath, "--output", outputPath },
            standardOutput,
            error);

        Assert.Equal(0, noOpExitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("archiveCreated=false", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("writtenBytes=0", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("경고:", standardOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(manifestBeforeNoOp, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
        AssertOutputContentsEqual(outputBeforeNoOp, outputPath);
        testRepository.WriteFile("data/group/a.txt", "a1");
        testRepository.CommitAll("modify a");

        BuildSummary firstModification = _sut.Execute(arguments);

        AssertOnlyGroupSummary(
            firstModification,
            version: 1,
            entries: 2,
            archiveCreated: false,
            fileObjects: 1,
            writtenBytes: 2);
        Assert.Equal("1.1", GetEntry(GetOnlyGroup(outputPath), "a.txt").Version);
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "files/group/1/a.txt.v1.1",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
        testRepository.WriteFile("data/group/a.txt", "a2");
        testRepository.CommitAll("modify a again");

        BuildSummary secondModification = _sut.Execute(arguments);

        AssertOnlyGroupSummary(
            secondModification,
            version: 1,
            entries: 2,
            archiveCreated: false,
            fileObjects: 1,
            writtenBytes: 2);
        Assert.Equal("1.2", GetEntry(GetOnlyGroup(outputPath), "a.txt").Version);
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "files/group/1/a.txt.v1.1",
                "files/group/1/a.txt.v1.2",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
        testRepository.WriteFile("data/group/c.txt", "c0");
        testRepository.CommitAll("add c");

        BuildSummary addition = _sut.Execute(arguments);

        AssertOnlyGroupSummary(addition, version: 1, entries: 3, archiveCreated: false, fileObjects: 1, writtenBytes: 2);
        Assert.Equal("1.0", GetEntry(GetOnlyGroup(outputPath), "c.txt").Version);
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "files/group/1/a.txt.v1.1",
                "files/group/1/a.txt.v1.2",
                "files/group/1/c.txt.v1.0",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
        testRepository.DeleteFile("data/group/b.txt");
        testRepository.CommitAll("delete b");

        BuildSummary deletion = _sut.Execute(arguments);

        AssertOnlyGroupSummary(deletion, version: 1, entries: 2, archiveCreated: false, fileObjects: 0, writtenBytes: 0);
        Assert.DoesNotContain(GetOnlyGroup(outputPath).Entries, entry => entry.Path == "b.txt");
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "files/group/1/a.txt.v1.1",
                "files/group/1/a.txt.v1.2",
                "files/group/1/c.txt.v1.0",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: group
                version: 2
                packing: group
                compression: none
            """);
        testRepository.CommitAll("increase group version");

        BuildSummary repack = _sut.Execute(arguments);

        AssertOnlyGroupSummary(repack, version: 2, entries: 2, archiveCreated: true, fileObjects: 0, writtenBytes: 4);
        ManifestGroup finalGroup = GetOnlyGroup(outputPath);
        Assert.Collection(
            finalGroup.Entries,
            entry => AssertArchiveEntry(entry, "a.txt", "2.0", offset: 0, length: 2),
            entry => AssertArchiveEntry(entry, "c.txt", "2.0", offset: 2, length: 2));
        Assert.Equal(
            new[]
            {
                "archives/group/1.gpka",
                "archives/group/2.gpka",
                "files/group/1/a.txt.v1.1",
                "files/group/1/a.txt.v1.2",
                "files/group/1/c.txt.v1.0",
                "manifest.json"
            },
            GetOutputFiles(outputPath));
    }

    [Fact]
    public void Run_FileRevisionAdjustments_PrintsOneWarningAfterSummaries()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: alpha
                version: 1
                packing: file
                compression: none
              - id: beta
                version: 2
                packing: file
                compression: none
            """);
        testRepository.WriteFile("data/alpha/a.txt", "same-a");
        testRepository.WriteFile("data/beta/b.txt", "new-b");
        testRepository.CommitAll("initial");
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        WriteOutputFile(outputPath, "files/alpha/1/a.txt.v1.5", "same-a"u8.ToArray());
        WriteOutputFile(outputPath, "files/beta/2/b.txt.v2.4", "old-b"u8.ToArray());
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Program.Run(
            new[] { "build", "--source", sourcePath, "--output", outputPath },
            standardOutput,
            error);

        string[] lines = standardOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(
            new[]
            {
                "그룹 'alpha': version=1, entries=1, archiveCreated=false, fileObjects=0, writtenBytes=0",
                "그룹 'beta': version=2, entries=1, archiveCreated=false, fileObjects=1, writtenBytes=5",
                "합계: groups=2, entries=2, archivesCreated=0, fileObjects=1, writtenBytes=5",
                "경고: 파일 리비전이 자동 증가했습니다.",
                "  group='alpha', path='a.txt', requested=1.0, actual=1.5",
                "  group='beta', path='b.txt', requested=2.0, actual=2.5"
            },
            lines);
        PatchManifest manifest = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));
        Assert.Collection(
            manifest.Groups,
            group => Assert.Equal("1.5", GetEntry(group, "a.txt").Version),
            group => Assert.Equal("2.5", GetEntry(group, "b.txt").Version));
    }

    [Fact]
    public void Execute_PartialFailure_RerunReusesMatchingArtifactAndKeepsLastManifest()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: m-base
                version: 1
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/m-base/file.txt", "m");
        testRepository.CommitAll("initial");
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        var arguments = new BuildArguments(sourcePath, outputPath);
        _sut.Execute(arguments);
        byte[] manifestBefore = File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));
        string successfulCommit = ManifestStore.ReadPrevious(outputPath)!.SourceCommit;
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: a-new
                version: 1
                packing: group
                compression: none
              - id: m-base
                version: 1
                packing: group
                compression: none
              - id: z-new
                version: 1
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/a-new/file.txt", "a");
        testRepository.WriteFile("data/z-new/file.txt", "z");
        string currentCommit = testRepository.CommitAll("add groups");
        string conflictingArchiveName = "archives/z-new/1.gpka";
        WriteOutputFile(outputPath, conflictingArchiveName, "collision"u8.ToArray());

        BuildException exception = Assert.Throws<BuildException>(() => _sut.Execute(arguments));

        Assert.Contains("z-new", exception.Message, StringComparison.Ordinal);
        Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
        Assert.Equal(successfulCommit, ManifestStore.ReadPrevious(outputPath)!.SourceCommit);
        string reusableArchivePath = GetOutputPath(outputPath, "archives/a-new/1.gpka");
        Assert.Equal("a"u8.ToArray(), File.ReadAllBytes(reusableArchivePath));
        File.SetLastWriteTimeUtc(reusableArchivePath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        DateTime reusableTimestamp = File.GetLastWriteTimeUtc(reusableArchivePath);
        File.Delete(GetOutputPath(outputPath, conflictingArchiveName));

        BuildSummary rerun = _sut.Execute(arguments);

        Assert.Collection(
            rerun.Groups,
            group => Assert.Equal(new GroupBuildSummary("a-new", 1, 1, false, 0, 0), group),
            group => Assert.Equal(new GroupBuildSummary("m-base", 1, 1, false, 0, 0), group),
            group => Assert.Equal(new GroupBuildSummary("z-new", 1, 1, true, 0, 1), group));
        Assert.Empty(rerun.FileRevisionAdjustments);
        Assert.Equal(reusableTimestamp, File.GetLastWriteTimeUtc(reusableArchivePath));
        Assert.Equal(currentCommit, ManifestStore.ReadPrevious(outputPath)!.SourceCommit);
    }

    [Fact]
    public void Execute_ArchiveConflictThenVersionBump_IncludesFailedRangeAndOtherIncrementalChanges()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: a-incremental
                version: 1
                packing: group
                compression: none
              - id: z-repack
                version: 1
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/a-incremental/file.txt", "a0");
        testRepository.WriteFile("data/z-repack/file.txt", "z0");
        testRepository.CommitAll("initial");
        string sourcePath = testRepository.GetRepositoryPath("data");
        string outputPath = testRepository.GetExternalPath("patches");
        var arguments = new BuildArguments(sourcePath, outputPath);
        _sut.Execute(arguments);
        byte[] manifestBefore = File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));
        string successfulCommit = ManifestStore.ReadPrevious(outputPath)!.SourceCommit;
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: a-incremental
                version: 1
                packing: group
                compression: none
              - id: z-repack
                version: 2
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/a-incremental/file.txt", "a1");
        testRepository.WriteFile("data/z-repack/file.txt", "z1");
        testRepository.CommitAll("prepare failed build");
        const string conflictingArchiveName = "archives/z-repack/2.gpka";
        WriteOutputFile(outputPath, conflictingArchiveName, "collision"u8.ToArray());

        Assert.Throws<BuildException>(() => _sut.Execute(arguments));

        Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(outputPath, "manifest.json")));
        Assert.Equal(successfulCommit, ManifestStore.ReadPrevious(outputPath)!.SourceCommit);
        Assert.Equal(
            "a1"u8.ToArray(),
            File.ReadAllBytes(GetOutputPath(outputPath, "files/a-incremental/1/file.txt.v1.1")));
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: a-incremental
                version: 1
                packing: group
                compression: none
              - id: z-repack
                version: 3
                packing: group
                compression: none
            """);
        testRepository.WriteFile("data/a-incremental/file.txt", "a2");
        string finalCommit = testRepository.CommitAll("recover with new group version");

        BuildSummary recovered = _sut.Execute(arguments);

        Assert.Collection(
            recovered.Groups,
            group => Assert.Equal(new GroupBuildSummary("a-incremental", 1, 1, false, 1, 2), group),
            group => Assert.Equal(new GroupBuildSummary("z-repack", 3, 1, true, 0, 2), group));
        Assert.Equal(
            new FileRevisionAdjustment("a-incremental", "file.txt", "1.1", "1.2"),
            Assert.Single(recovered.FileRevisionAdjustments));
        PatchManifest manifest = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));
        ManifestGroup incrementalGroup = Assert.Single(manifest.Groups, group => group.Id == "a-incremental");
        ManifestGroup repackedGroup = Assert.Single(manifest.Groups, group => group.Id == "z-repack");
        Assert.Equal("1.2", Assert.Single(incrementalGroup.Entries).Version);
        Assert.Equal(3, repackedGroup.Version);
        Assert.Equal(finalCommit, manifest.SourceCommit);
        Assert.Equal("collision"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, conflictingArchiveName)));
    }

    [Fact]
    public void Execute_UntrackedSourceAndTrackedOutsideChange_AreIgnored()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: group
                version: 1
                packing: file
                compression: none
            """);
        testRepository.WriteFile("data/group/file.txt", "tracked");
        testRepository.WriteFile("outside.txt", "before");
        testRepository.CommitAll("initial");
        testRepository.WriteFile("data/group/untracked.txt", "untracked");
        testRepository.WriteFile("outside.txt", "after");
        string outputPath = testRepository.GetExternalPath("patches");

        BuildSummary summary = _sut.Execute(
            new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));

        AssertOnlyGroupSummary(summary, version: 1, entries: 1, archiveCreated: false, fileObjects: 1, writtenBytes: 7);
        Assert.Equal("tracked"u8.ToArray(), File.ReadAllBytes(GetOutputPath(outputPath, "files/group/1/file.txt.v1.0")));
        Assert.DoesNotContain(GetOutputFiles(outputPath), path => path.Contains("untracked", StringComparison.Ordinal));
    }

    private static GitTestRepository CreateVersionedRepository(int currentVersion)
    {
        return CreateFileRepository(currentVersion, "none", ("file.txt", "data"));
    }

    private static GitTestRepository CreateFileRepository(
        int currentVersion,
        string compression,
        params (string Path, string Contents)[] entries)
    {
        var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            $$"""
            groups:
              - id: group
                version: {{currentVersion}}
                packing: file
                compression: {{compression}}
            """);

        foreach ((string path, string contents) in entries)
        {
            testRepository.WriteFile($"data/group/{path}", contents);
        }

        testRepository.CommitAll("initial");
        return testRepository;
    }

    private static GitTestRepository CreateGroupRepository(
        int currentVersion,
        params (string Path, string Contents)[] entries)
    {
        var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            $$"""
            groups:
              - id: group
                version: {{currentVersion}}
                packing: group
                compression: none
            """);

        foreach ((string path, string contents) in entries)
        {
            testRepository.WriteFile($"data/group/{path}", contents);
        }

        testRepository.CommitAll("initial");
        return testRepository;
    }

    private ManifestGroup BuildGroupWithOverlay(GitTestRepository testRepository, string outputPath)
    {
        string sourcePath = testRepository.GetRepositoryPath("data");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        testRepository.WriteFile("data/group/a.txt", "a-overlay");
        testRepository.CommitAll("create a overlay");
        _sut.Execute(new BuildArguments(sourcePath, outputPath));
        return GetOnlyGroup(outputPath);
    }

    private static ManifestGroup GetOnlyGroup(string outputPath)
    {
        PatchManifest manifest = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));
        return Assert.Single(manifest.Groups);
    }

    private static ManifestEntry GetEntry(ManifestGroup group, string path)
    {
        return Assert.Single(group.Entries, entry => entry.Path == path);
    }

    private static void AssertManifestEntryEqual(ManifestEntry expected, ManifestEntry actual)
    {
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Offset, actual.Offset);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.StoredSize, actual.StoredSize);
        Assert.Equal(expected.Checksum, actual.Checksum);
    }

    private static void AssertOnlyGroupSummary(
        BuildSummary summary,
        int version,
        int entries,
        bool archiveCreated,
        int fileObjects,
        long writtenBytes)
    {
        Assert.Equal(
            new GroupBuildSummary("group", version, entries, archiveCreated, fileObjects, writtenBytes),
            Assert.Single(summary.Groups));
        Assert.Empty(summary.FileRevisionAdjustments);
    }

    private static void AssertFileEntry(
        string outputPath,
        ManifestEntry entry,
        string path,
        string version,
        long size)
    {
        Assert.Equal(path, entry.Path);
        Assert.Equal(version, entry.Version);
        Assert.Equal(size, entry.Size);
        Assert.Equal(EntrySource.File, entry.Source);
        Assert.Null(entry.Offset);
        Assert.Null(entry.Length);
        Assert.Equal($"files/group/{version[..version.IndexOf('.', StringComparison.Ordinal)]}/{path}.v{version}", entry.Name);
        Assert.NotNull(entry.StoredSize);
        Assert.NotNull(entry.Checksum);
        StoredArtifact stored = ArtifactWriter.ReadStored(GetOutputPath(outputPath, entry.Name!));
        Assert.Equal(stored.StoredSize, entry.StoredSize);
        Assert.Equal(stored.Checksum, entry.Checksum);
    }

    private static IReadOnlyDictionary<string, byte[]> GetOutputContents(string outputPath)
    {
        return GetOutputFiles(outputPath).ToDictionary(
            path => path,
            path => File.ReadAllBytes(GetOutputPath(outputPath, path)),
            StringComparer.Ordinal);
    }

    private static void AssertOutputContentsEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        string outputPath)
    {
        IReadOnlyDictionary<string, byte[]> actual = GetOutputContents(outputPath);
        Assert.Equal(expected.Keys.OrderBy(path => path), actual.Keys.OrderBy(path => path));

        foreach ((string path, byte[] bytes) in expected)
        {
            Assert.Equal(bytes, actual[path]);
        }
    }

    private static void CorruptSameSize(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xff;
        File.WriteAllBytes(path, bytes);
    }

    private static void AssertArchiveEntry(
        ManifestEntry entry,
        string path,
        string version,
        long offset,
        long length)
    {
        Assert.Equal(path, entry.Path);
        Assert.Equal(version, entry.Version);
        Assert.Equal(length, entry.Size);
        Assert.Equal(EntrySource.Archive, entry.Source);
        Assert.Equal(offset, entry.Offset);
        Assert.Equal(length, entry.Length);
        Assert.Null(entry.Name);
        Assert.Null(entry.StoredSize);
        Assert.Null(entry.Checksum);
    }

    private static IReadOnlyList<string> GetOutputFiles(string outputPath)
    {
        return Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetOutputPath(string outputPath, string relativePath)
    {
        return Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void WriteOutputFile(string outputPath, string relativePath, byte[] bytes)
    {
        string path = GetOutputPath(outputPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
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