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