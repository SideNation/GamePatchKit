using System.Text;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestDataExtractor
{
    [Fact]
    public void Execute_ArchiveGroupIsZstd_WritesTheOriginalBytesUnderData()
    {
        AssertArchiveGroupIsExtracted(CompressionKind.Zstd);
    }

    [Fact]
    public void Execute_ArchiveGroupIsNone_WritesTheOriginalBytesUnderData()
    {
        AssertArchiveGroupIsExtracted(CompressionKind.None);
    }

    [Fact]
    public void Execute_FileGroupIsZstd_WritesTheOriginalBytesUnderData()
    {
        AssertFileGroupIsExtracted(CompressionKind.Zstd);
    }

    [Fact]
    public void Execute_FileGroupIsNone_WritesTheOriginalBytesUnderData()
    {
        AssertFileGroupIsExtracted(CompressionKind.None);
    }

    // 그룹 하나가 아카이브와 파일 객체를 함께 갖는 것이 증분 빌드의 결과다. 두 경로가 한 번에 나온다.
    [Fact]
    public void Execute_GroupHasBothArchiveAndFileEntries_WritesBoth()
    {
        using var environment = new ExtractTestEnvironment();
        ManifestGroup group = environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha"));
        PatchManifest manifest = environment.CreateManifest(
            environment.AppendFileEntry(group, ("c.bin", "charlie")));

        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);

        environment.AssertDataContent("content/a.bin", "alpha");
        environment.AssertDataContent("content/c.bin", "charlie");
    }

    // 바뀌지 않은 엔트리를 다시 푸는지 보려면 푼 결과를 같은 길이로 바꿔 두고 그것이 살아남는지 본다.
    // 길이가 다르면 크기 검사에 걸려 다시 풀리므로 판정이 되지 않는다.
    [Fact]
    public void Execute_EntryIsUnchanged_DoesNotExtractItAgain()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha")));
        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);
        environment.WriteDataContent("content/a.bin", "ALPHA");

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, manifest);

        Assert.Equal(new ExtractSummary(ExtractedCount: 0, RemovedCount: 0), summary);
        environment.AssertDataContent("content/a.bin", "ALPHA");
    }

    [Fact]
    public void Execute_EntryChanged_ExtractsItAgain()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest previous = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha")));
        DataExtractor.Execute(environment.OutputPath, previous, previous: null);
        PatchManifest target = environment.CreateManifest(
            environment.AddArchiveGroup("content", 2, CompressionKind.Zstd, ("a.bin", "gamma")));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, target, previous);

        Assert.Equal(1, summary.ExtractedCount);
        environment.AssertDataContent("content/a.bin", "gamma");
    }

    [Fact]
    public void Execute_ExtractedFileIsMissing_ExtractsItAgainEvenWhenUnchanged()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddFileGroup("config", 1, CompressionKind.Zstd, ("a.json", "{}")));
        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);
        File.Delete(environment.GetDataPath("config/a.json"));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, manifest);

        Assert.Equal(1, summary.ExtractedCount);
        environment.AssertDataContent("config/a.json", "{}");
    }

    // 아카이브의 일부만 다시 풀 때 앞 구간을 건너뛰는 계산이 맞아야 한다.
    [Fact]
    public void Execute_OnlyOneArchiveEntryNeedsExtraction_SkipsToItsOffset()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup(
                "content",
                1,
                CompressionKind.Zstd,
                ("a.bin", "alpha"),
                ("b.bin", "bravo"),
                ("c.bin", "charlie")));
        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);
        File.Delete(environment.GetDataPath("content/c.bin"));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, manifest);

        Assert.Equal(1, summary.ExtractedCount);
        environment.AssertDataContent("content/c.bin", "charlie");
    }

    [Fact]
    public void Execute_EntryDisappeared_RemovesTheFileAndTheEmptiedDirectory()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest previous = environment.CreateManifest(
            environment.AddArchiveGroup(
                "content",
                1,
                CompressionKind.Zstd,
                ("a.bin", "alpha"),
                ("nested/b.bin", "beta")));
        DataExtractor.Execute(environment.OutputPath, previous, previous: null);
        PatchManifest target = environment.CreateManifest(
            environment.AddArchiveGroup("content", 2, CompressionKind.Zstd, ("a.bin", "alpha")));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, target, previous);

        Assert.Equal(1, summary.RemovedCount);
        Assert.False(File.Exists(environment.GetDataPath("content/nested/b.bin")));
        Assert.False(Directory.Exists(environment.GetDataPath("content/nested")));
        environment.AssertDataContent("content/a.bin", "alpha");
    }

    // 해제 트리는 매니페스트와 정확히 같아야 하므로 gpk가 쓰지 않은 파일도 남기지 않는다.
    [Fact]
    public void Execute_UnknownFileIsInTheTree_RemovesIt()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha")));
        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);
        environment.WriteDataContent("content/leftover.bin", "stale");

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, manifest);

        Assert.Equal(1, summary.RemovedCount);
        Assert.False(File.Exists(environment.GetDataPath("content/leftover.bin")));
        environment.AssertDataContent("content/a.bin", "alpha");
    }

    // 지우기는 링크를 따라 들어가지 않는다. 링크만 없어지고 대상 폴더의 파일은 그대로여야 한다.
    [Fact]
    public void Execute_TreeHasASymbolicLinkDirectory_RemovesTheLinkWithoutTouchingItsTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha")));
        DataExtractor.Execute(environment.OutputPath, manifest, previous: null);
        string outsidePath = environment.CreateOutsideDirectory("outside.bin", "outside");
        Directory.CreateSymbolicLink(environment.GetDataPath("content/link"), outsidePath);

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, manifest);

        Assert.Equal(1, summary.RemovedCount);
        Assert.False(Directory.Exists(environment.GetDataPath("content/link")));
        Assert.Equal("outside", File.ReadAllText(Path.Combine(outsidePath, "outside.bin"), Encoding.UTF8));
    }

    // 지우는 것이 푸는 것보다 먼저여야 성립한다. 파일이 있는 자리에는 폴더를 만들 수 없다.
    [Fact]
    public void Execute_PathWasAFileAndBecomesADirectory_ReplacesIt()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest previous = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("shared", "alpha")));
        DataExtractor.Execute(environment.OutputPath, previous, previous: null);
        PatchManifest target = environment.CreateManifest(
            environment.AddArchiveGroup("content", 2, CompressionKind.Zstd, ("shared/inner.bin", "beta")));

        DataExtractor.Execute(environment.OutputPath, target, previous);

        environment.AssertDataContent("content/shared/inner.bin", "beta");
    }

    // 아카이브가 매니페스트가 말하는 만큼 담고 있지 않으면 잘린 파일을 남기지 않고 멈춰야 한다.
    [Fact]
    public void Execute_ArchiveIsShorterThanTheManifestClaims_ThrowsAndLeavesNothingBehind()
    {
        using var environment = new ExtractTestEnvironment();
        ManifestGroup group = environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("a.bin", "alpha"));
        PatchManifest manifest = environment.CreateManifest(environment.OverstateEntrySize(group, "a.bin"));

        BuildException exception = Assert.Throws<BuildException>(
            () => DataExtractor.Execute(environment.OutputPath, manifest, previous: null));

        Assert.Contains("크기가 다릅니다", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(environment.GetDataPath("content/a.bin")));
        Assert.Empty(environment.FindTemporaryFiles());
    }

    [Fact]
    public void Execute_TwoGroupsPointAtTheSameDataPath_Throws()
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, CompressionKind.Zstd, ("nested/a.bin", "alpha")),
            environment.AddArchiveGroup("content/nested", 1, CompressionKind.Zstd, ("a.bin", "beta")));

        BuildException exception = Assert.Throws<BuildException>(
            () => DataExtractor.Execute(environment.OutputPath, manifest, previous: null));

        Assert.Contains("content/nested/a.bin", exception.Message, StringComparison.Ordinal);
    }

    // 해제 트리의 계약은 "원본 데이터 루트와 같은 배치"다. 실제 빌드 산출물로 그것을 확인한다.
    [Fact]
    public void Execute_RealBuildOutput_ReproducesTheSourceTree()
    {
        using var testRepository = new GitTestRepository();
        testRepository.WriteFile(
            "data/gamepatchkit.yml",
            """
            groups:
              - id: content
                version: 1
                packing: group
                compression: zstd
              - id: config
                version: 1
                packing: file
                compression: zstd
            """);
        var sourceFiles = new (string Path, string Content)[]
        {
            ("content/a.bin", "alpha"),
            ("content/maps/01.bin", "map-one"),
            ("config/server.json", """{"tick":30}"""),
            ("config/nested/client.json", "[1,2,3]")
        };

        foreach ((string path, string content) in sourceFiles)
        {
            testRepository.WriteFile($"data/{path}", content);
        }

        testRepository.CommitAll("initial");
        string outputPath = testRepository.GetExternalPath("patches");
        new BuildCommand().Execute(new BuildArguments(testRepository.GetRepositoryPath("data"), outputPath));
        PatchManifest manifest = ManifestStore.ReadPrevious(outputPath)!;

        ExtractSummary summary = DataExtractor.Execute(outputPath, manifest, previous: null);

        Assert.Equal(sourceFiles.Length, summary.ExtractedCount);

        foreach ((string path, string content) in sourceFiles)
        {
            string extractedPath = Path.Combine(
                outputPath,
                DataExtractor.DATA_DIRECTORY_NAME,
                path.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(
                File.ReadAllBytes(testRepository.GetRepositoryPath($"data/{path}")),
                File.ReadAllBytes(extractedPath));
            Assert.Equal(content, File.ReadAllText(extractedPath, Encoding.UTF8));
        }
    }

    private static void AssertArchiveGroupIsExtracted(CompressionKind compression)
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddArchiveGroup("content", 1, compression, ("a.bin", "alpha"), ("nested/b.bin", "beta")));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, previous: null);

        Assert.Equal(new ExtractSummary(ExtractedCount: 2, RemovedCount: 0), summary);
        environment.AssertDataContent("content/a.bin", "alpha");
        environment.AssertDataContent("content/nested/b.bin", "beta");
    }

    private static void AssertFileGroupIsExtracted(CompressionKind compression)
    {
        using var environment = new ExtractTestEnvironment();
        PatchManifest manifest = environment.CreateManifest(
            environment.AddFileGroup("config", 2, compression, ("a.json", "{}"), ("nested/b.json", "[1,2,3]")));

        ExtractSummary summary = DataExtractor.Execute(environment.OutputPath, manifest, previous: null);

        Assert.Equal(new ExtractSummary(ExtractedCount: 2, RemovedCount: 0), summary);
        environment.AssertDataContent("config/a.json", "{}");
        environment.AssertDataContent("config/nested/b.json", "[1,2,3]");
    }

    private sealed class ExtractTestEnvironment : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        private readonly ArtifactWriter _artifactWriter = new();
        private int _sourceIndex;

        public string OutputPath => Path.Combine(_rootPath, "mirror");

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        public ManifestGroup AddArchiveGroup(
            string groupId,
            int version,
            CompressionKind compression,
            params (string Path, string Content)[] entries)
        {
            var group = new GroupConfiguration
            {
                Id = groupId,
                Version = version,
                Packing = PackingKind.Group,
                Compression = compression
            };
            SourceEntry[] sourceEntries = entries.Select(WriteSource).ToArray();
            WrittenArchive written = _artifactWriter.WriteArchive(OutputPath, group, sourceEntries);
            return new ManifestGroup
            {
                Id = groupId,
                Version = version,
                Packing = PackingKind.Group,
                Compression = compression,
                Archive = new ManifestArchive
                {
                    Name = written.Name,
                    PayloadSize = written.PayloadSize,
                    StoredSize = written.StoredSize,
                    Checksum = written.Checksum
                },
                Entries = written.Layout
                    .Select(
                        layout => new ManifestEntry
                        {
                            Path = layout.Path,
                            Version = $"{version}.0",
                            Size = layout.Length,
                            Source = EntrySource.Archive,
                            Offset = layout.Offset,
                            Length = layout.Length
                        })
                    .ToArray()
            };
        }

        public ManifestGroup AddFileGroup(
            string groupId,
            int version,
            CompressionKind compression,
            params (string Path, string Content)[] entries)
        {
            var group = new GroupConfiguration
            {
                Id = groupId,
                Version = version,
                Packing = PackingKind.File,
                Compression = compression
            };
            return new ManifestGroup
            {
                Id = groupId,
                Version = version,
                Packing = PackingKind.File,
                Compression = compression,
                Entries = entries.Select(entry => WriteFileEntry(group, entry)).ToArray()
            };
        }

        // 아카이브 그룹에 파일 객체 엔트리를 하나 더한다. 증분 빌드가 만드는 혼합 그룹과 같은 모양이다.
        public ManifestGroup AppendFileEntry(ManifestGroup group, (string Path, string Content) entry)
        {
            var configuration = new GroupConfiguration
            {
                Id = group.Id,
                Version = group.Version,
                Packing = group.Packing,
                Compression = group.Compression
            };
            return CopyWithEntries(group, [.. group.Entries, WriteFileEntry(configuration, entry)]);
        }

        public ManifestGroup OverstateEntrySize(ManifestGroup group, string entryPath)
        {
            const long overstatedSize = 4096;
            ManifestEntry[] entries = group.Entries
                .Select(
                    entry => entry.Path == entryPath
                        ? new ManifestEntry
                        {
                            Path = entry.Path,
                            Version = entry.Version,
                            Size = overstatedSize,
                            Source = entry.Source,
                            Offset = entry.Offset,
                            Length = overstatedSize
                        }
                        : entry)
                .ToArray();
            return CopyWithEntries(group, entries);
        }

        public PatchManifest CreateManifest(params ManifestGroup[] groups)
        {
            return new PatchManifest
            {
                SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
                ReleaseVersion = 1,
                SourcePath = "data",
                SourceCommit = new string('a', 40),
                Groups = groups
            };
        }

        public string GetDataPath(string relativePath)
        {
            return Path.Combine(
                OutputPath,
                DataExtractor.DATA_DIRECTORY_NAME,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public string CreateOutsideDirectory(string fileName, string content)
        {
            string directoryPath = Path.Combine(_rootPath, "outside");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllBytes(Path.Combine(directoryPath, fileName), Encoding.UTF8.GetBytes(content));
            return directoryPath;
        }

        public void WriteDataContent(string relativePath, string content)
        {
            string path = GetDataPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        }

        public void AssertDataContent(string relativePath, string expected)
        {
            Assert.Equal(expected, File.ReadAllText(GetDataPath(relativePath), Encoding.UTF8));
        }

        public IReadOnlyList<string> FindTemporaryFiles()
        {
            return Directory.EnumerateFiles(OutputPath, "*.tmp", SearchOption.AllDirectories).ToArray();
        }

        private static ManifestGroup CopyWithEntries(ManifestGroup group, IReadOnlyList<ManifestEntry> entries)
        {
            return new ManifestGroup
            {
                Id = group.Id,
                Version = group.Version,
                Packing = group.Packing,
                Compression = group.Compression,
                Archive = group.Archive,
                Entries = entries
            };
        }

        private ManifestEntry WriteFileEntry(GroupConfiguration group, (string Path, string Content) entry)
        {
            SourceEntry source = WriteSource(entry);
            WrittenArtifact written = _artifactWriter.WriteFile(OutputPath, group, source, $"{group.Version}.0");
            return new ManifestEntry
            {
                Path = entry.Path,
                Version = written.Version,
                Size = source.Size,
                Source = EntrySource.File,
                Name = written.Name,
                StoredSize = written.StoredSize,
                Checksum = written.Checksum
            };
        }

        private SourceEntry WriteSource((string Path, string Content) entry)
        {
            string path = Path.Combine(
                _rootPath,
                "source",
                _sourceIndex++.ToString(),
                entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = Encoding.UTF8.GetBytes(entry.Content);
            File.WriteAllBytes(path, bytes);
            return new SourceEntry(entry.Path, path, bytes.Length);
        }
    }
}
