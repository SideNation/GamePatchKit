using System.Security.Cryptography;
using System.Text;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestSyncCommand
{
    [Fact]
    public async Task ExecuteAsync_LocalIsEmpty_DownloadsEveryArtifactAndWritesManifest()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 3, ("a.bin", "alpha"), ("b.bin", "beta"));
        var sut = new SyncCommand(environment.Remote);

        SyncSummary summary = await sut.ExecuteAsync(environment.OutputPath);

        Assert.Equal(SyncOutcome.Synced, summary.Outcome);
        Assert.Equal(3, summary.PointerVersion);
        Assert.Null(summary.PreviousVersion);
        Assert.Equal(2, summary.DownloadedCount);
        Assert.Equal(0, summary.ReusedCount);
        Assert.Equal(9, summary.DownloadedBytes);
        environment.AssertLocalContent("a.bin", "alpha");
        environment.AssertLocalContent("b.bin", "beta");
        Assert.Equal(3, environment.ReadLocalManifest()!.ReleaseVersion);
    }

    // 다른 서버가 바로 읽는 것은 미러가 아니라 data 아래의 원본 트리다.
    [Fact]
    public async Task ExecuteAsync_LocalIsEmpty_ExtractsEveryEntryUnderData()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 3, ("a.bin", "alpha"), ("b.bin", "beta"));

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(2, summary.ExtractedCount);
        Assert.Equal(0, summary.RemovedCount);
        environment.AssertDataContent("a.bin", "alpha");
        environment.AssertDataContent("b.bin", "beta");
    }

    [Fact]
    public async Task ExecuteAsync_EntryDisappearedInTheNewGeneration_RemovesItFromData()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"), ("b.bin", "beta"));
        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        environment.PublishGeneration(releaseVersion: 2, ("a.bin", "alpha"));
        environment.Remote.ReleaseVersion = 2;

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(0, summary.ExtractedCount);
        Assert.Equal(1, summary.RemovedCount);
        environment.AssertDataContent("a.bin", "alpha");
        Assert.False(File.Exists(environment.GetDataPath("b.bin")));
    }

    // 해제가 매니페스트 교체보다 먼저다. 해제가 실패하면 로컬은 이전 세대 그대로여야 한다.
    [Fact]
    public async Task ExecuteAsync_ExtractionFails_KeepsThePreviousGeneration()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        environment.PublishGeneration(releaseVersion: 2, entrySize: 4096, ("a.bin", "gamma"));
        environment.Remote.ReleaseVersion = 2;

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));

        Assert.Contains("해제한 파일의 크기가 다릅니다", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, environment.ReadLocalManifest()!.ReleaseVersion);
        environment.AssertDataContent("a.bin", "alpha");
        Assert.Empty(environment.FindTemporaryFiles());
    }

    // 폴링이 대부분의 시간에 하는 일이다. 원격 객체를 한 번도 건드리지 않아야 한다.
    [Fact]
    public async Task ExecuteAsync_LocalMatchesPointer_DownloadsNothing()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 3, ("a.bin", "alpha"));
        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        environment.Remote.RequestedObjectPaths.Clear();

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(SyncOutcome.AlreadyUpToDate, summary.Outcome);
        Assert.Equal(3, summary.PointerVersion);
        Assert.Empty(environment.Remote.RequestedObjectPaths);
    }

    [Fact]
    public async Task ExecuteAsync_SomeArtifactsAreAlreadyStored_DownloadsOnlyTheMissingOnes()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"), ("b.bin", "beta"));
        environment.WriteLocalArtifact("a.bin", "alpha");

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(1, summary.DownloadedCount);
        Assert.Equal(1, summary.ReusedCount);
        Assert.DoesNotContain(environment.ArtifactName("a.bin"), environment.Remote.RequestedObjectPaths);
        Assert.Contains(environment.ArtifactName("b.bin"), environment.Remote.RequestedObjectPaths);
    }

    // 포인터를 이전 값으로 되돌리는 것이 롤백이다. "크면"이 아니라 "다르면" 동기화해야 성립한다.
    [Fact]
    public async Task ExecuteAsync_PointerIsOlderThanLocal_SynchronizesBackward()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 5, ("a.bin", "new"));
        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        environment.PublishGeneration(releaseVersion: 4, ("a.bin", "old"));
        environment.Remote.ReleaseVersion = 4;

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(SyncOutcome.Synced, summary.Outcome);
        Assert.Equal(4, summary.PointerVersion);
        Assert.Equal(5, summary.PreviousVersion);
        Assert.Equal(4, environment.ReadLocalManifest()!.ReleaseVersion);
    }

    [Fact]
    public async Task ExecuteAsync_ChecksumDoesNotMatch_KeepsPreviousGenerationAndLeavesNoTemporaryFile()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        environment.PublishGeneration(releaseVersion: 2, ("a.bin", "beta"));
        environment.Remote.ReleaseVersion = 2;
        // 크기 검사가 아니라 checksum 검사가 걸리도록 길이는 같고 내용만 다르게 바꾼다.
        environment.CorruptRemoteArtifact("a.bin", "BETA");

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));

        Assert.Contains("checksum", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, environment.ReadLocalManifest()!.ReleaseVersion);
        environment.AssertLocalContent("a.bin", "alpha");
        Assert.Empty(environment.FindTemporaryFiles());
    }

    [Fact]
    public async Task ExecuteAsync_LocalArtifactHasDifferentSize_DownloadsItAgain()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        environment.WriteLocalArtifact("a.bin", "truncated-differently");

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(1, summary.DownloadedCount);
        Assert.Equal(0, summary.ReusedCount);
        environment.AssertLocalContent("a.bin", "alpha");
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactIsMissingRemotely_DoesNotReplaceTheManifest()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"), ("b.bin", "beta"));
        environment.RemoveRemoteArtifact("b.bin");

        await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));

        Assert.Null(environment.ReadLocalManifest());
        Assert.Empty(environment.FindTemporaryFiles());
    }

    [Fact]
    public async Task ExecuteAsync_ManifestReleaseVersionDiffersFromPointer_Throws()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        environment.RepublishUnderAnotherObjectPath(pointerVersion: 2, manifestReleaseVersion: 1);
        environment.Remote.ReleaseVersion = 2;

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));

        Assert.Contains("releaseVersion", exception.Message, StringComparison.Ordinal);
    }

    // cron 실행과 수동 실행이 겹치는 것이 정상 시나리오다. 예외가 아니라 건너뛰기로 끝나야 한다.
    [Fact]
    public async Task ExecuteAsync_LockIsAlreadyHeld_SkipsWithoutAnyRemoteCall()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        Directory.CreateDirectory(environment.OutputPath);

        using (new FileStream(
            Path.Combine(environment.OutputPath, ".gpk-sync.lock"),
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None))
        {
            SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

            Assert.Equal(SyncOutcome.SkippedBecauseLocked, summary.Outcome);
            Assert.Null(summary.PointerVersion);
            Assert.Empty(environment.Remote.RequestedObjectPaths);
        }
    }

    [Fact]
    public async Task ExecuteAsync_LockIsReleased_RunsAgain()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        Directory.CreateDirectory(environment.OutputPath);
        string lockPath = Path.Combine(environment.OutputPath, ".gpk-sync.lock");

        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);
        }

        SyncSummary summary = await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(SyncOutcome.Synced, summary.Outcome);
    }

    // 매니페스트 검증은 통과하지만 원격 경로로는 쓸 수 없는 이름이다. 첫 산출물을 받기 전에 걸러야 한다.
    [Fact]
    public async Task ExecuteAsync_ArtifactNameIsNotRemoteSafe_ThrowsBeforeDownloadingAnyArtifact()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a b.bin", "alpha"));

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));

        Assert.Contains("원격 경로", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { SyncCommand.GetManifestObjectPath(1) }, environment.Remote.RequestedObjectPaths);
    }

    [Fact]
    public async Task ExecuteAsync_LocalManifestIsCorrupted_Throws()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));
        Directory.CreateDirectory(environment.OutputPath);
        File.WriteAllText(Path.Combine(environment.OutputPath, "manifest.json"), "not-json");

        await Assert.ThrowsAsync<BuildException>(
            () => new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath));
    }

    // 동기화가 끝난 폴더는 게시된 세대와 바이트까지 같아야 upload가 만든 트리와 구분되지 않는다.
    [Fact]
    public async Task ExecuteAsync_WritesTheManifestBytesVerbatim()
    {
        using var environment = new SyncTestEnvironment(releaseVersion: 1, ("a.bin", "alpha"));

        await new SyncCommand(environment.Remote).ExecuteAsync(environment.OutputPath);

        Assert.Equal(
            environment.RemoteManifestBytes(1),
            File.ReadAllBytes(Path.Combine(environment.OutputPath, "manifest.json")));
    }

    private sealed class FakeSyncRemote : ISyncRemote
    {
        private readonly Dictionary<string, byte[]> _objectsByPath = new(StringComparer.Ordinal);

        public long ReleaseVersion { get; set; }

        public List<string> RequestedObjectPaths { get; } = new();

        public void Put(string objectPath, byte[] content)
        {
            _objectsByPath[objectPath] = content;
        }

        public void Remove(string objectPath)
        {
            _objectsByPath.Remove(objectPath);
        }

        public byte[] Get(string objectPath)
        {
            return _objectsByPath[objectPath];
        }

        public Task<long> GetReleaseVersionAsync()
        {
            return Task.FromResult(ReleaseVersion);
        }

        public Task<Stream> OpenObjectAsync(string objectPath)
        {
            RequestedObjectPaths.Add(objectPath);

            if (!_objectsByPath.TryGetValue(objectPath, out byte[]? content))
            {
                throw new BuildException($"게시된 객체가 없습니다: {objectPath}");
            }

            return Task.FromResult<Stream>(new MemoryStream(content));
        }
    }

    private sealed class SyncTestEnvironment : IDisposable
    {
        private const string GroupId = "group";
        private const int GroupVersion = 1;
        private const string EntryVersion = "1.0";

        private readonly string _testPath;

        public string OutputPath { get; }

        public FakeSyncRemote Remote { get; } = new();

        public SyncTestEnvironment(int releaseVersion, params (string Path, string Content)[] artifacts)
        {
            _testPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
            OutputPath = Path.Combine(_testPath, "mirror");
            Remote.ReleaseVersion = releaseVersion;
            PublishGeneration(releaseVersion, artifacts);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testPath))
            {
                Directory.Delete(_testPath, recursive: true);
            }
        }

        public string ArtifactName(string path)
        {
            return $"files/{GroupId}/{GroupVersion}/{path}.v{EntryVersion}";
        }

        public byte[] RemoteManifestBytes(int releaseVersion)
        {
            return Remote.Get(SyncCommand.GetManifestObjectPath(releaseVersion));
        }

        // 게시자가 만드는 것과 같은 세대를 원격에 올린다. 매니페스트 바이트는 ManifestStore가 쓴 정본을
        // 그대로 쓰고, checksum도 실제 내용으로 계산한다.
        public void PublishGeneration(int releaseVersion, params (string Path, string Content)[] artifacts)
        {
            PublishGeneration(releaseVersion, entrySize: null, artifacts);
        }

        // entrySize를 주면 매니페스트가 실제 산출물보다 큰 크기를 말하게 되어 해제 단계에서 실패한다.
        public void PublishGeneration(
            int releaseVersion,
            long? entrySize,
            params (string Path, string Content)[] artifacts)
        {
            var entries = new List<ManifestEntry>();

            foreach ((string path, string content) in artifacts)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                Remote.Put(ArtifactName(path), bytes);
                entries.Add(
                    new ManifestEntry
                    {
                        Path = path,
                        Version = EntryVersion,
                        Size = entrySize ?? bytes.Length,
                        Source = EntrySource.File,
                        Name = ArtifactName(path),
                        StoredSize = bytes.Length,
                        Checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    });
            }

            var manifest = new PatchManifest
            {
                SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
                ReleaseVersion = releaseVersion,
                SourcePath = "data",
                SourceCommit = new string('a', 40),
                Groups = new[]
                {
                    new ManifestGroup
                    {
                        Id = GroupId,
                        Version = GroupVersion,
                        Packing = PackingKind.File,
                        Compression = CompressionKind.None,
                        Entries = entries
                    }
                }
            };
            Remote.Put(SyncCommand.GetManifestObjectPath(releaseVersion), SerializeManifest(manifest));
        }

        // 포인터가 가리키는 경로에 다른 releaseVersion의 매니페스트를 둔다.
        public void RepublishUnderAnotherObjectPath(int pointerVersion, int manifestReleaseVersion)
        {
            Remote.Put(
                SyncCommand.GetManifestObjectPath(pointerVersion),
                Remote.Get(SyncCommand.GetManifestObjectPath(manifestReleaseVersion)));
        }

        public void CorruptRemoteArtifact(string path, string content)
        {
            Remote.Put(ArtifactName(path), Encoding.UTF8.GetBytes(content));
        }

        public void RemoveRemoteArtifact(string path)
        {
            Remote.Remove(ArtifactName(path));
        }

        public void WriteLocalArtifact(string path, string content)
        {
            string localPath = Path.Combine(
                OutputPath,
                ArtifactName(path).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllBytes(localPath, Encoding.UTF8.GetBytes(content));
        }

        public void AssertLocalContent(string path, string expected)
        {
            string localPath = Path.Combine(
                OutputPath,
                ArtifactName(path).Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(expected, File.ReadAllText(localPath, Encoding.UTF8));
        }

        public string GetDataPath(string path)
        {
            return Path.Combine(
                OutputPath,
                DataExtractor.DATA_DIRECTORY_NAME,
                GroupId,
                path.Replace('/', Path.DirectorySeparatorChar));
        }

        public void AssertDataContent(string path, string expected)
        {
            Assert.Equal(expected, File.ReadAllText(GetDataPath(path), Encoding.UTF8));
        }

        public PatchManifest? ReadLocalManifest()
        {
            return ManifestStore.ReadPrevious(OutputPath);
        }

        public IReadOnlyList<string> FindTemporaryFiles()
        {
            if (!Directory.Exists(OutputPath))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(OutputPath, "*.tmp", SearchOption.AllDirectories).ToArray();
        }

        private byte[] SerializeManifest(PatchManifest manifest)
        {
            string temporaryDirectory = Path.Combine(_testPath, $"publish-{Guid.NewGuid():N}");
            ManifestStore.WriteAtomically(temporaryDirectory, manifest);
            byte[] bytes = File.ReadAllBytes(Path.Combine(temporaryDirectory, "manifest.json"));
            Directory.Delete(temporaryDirectory, recursive: true);
            return bytes;
        }
    }
}
