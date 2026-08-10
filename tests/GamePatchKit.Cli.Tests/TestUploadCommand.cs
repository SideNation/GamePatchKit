using GamePatchKit.Cli;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

public sealed class TestUploadCommand
{
    private const string ValidBucket = "patch-data";
    private const long MAX_ARTIFACT_SIZE_BYTES = 1_073_741_824;

    [Fact]
    public void PlanArtifacts_UploadedIsNull_ReturnsAllCurrentArtifacts()
    {
        PatchManifest current = CreateInMemoryManifest(("a", 10), ("b", 20));

        IReadOnlyList<ManifestArtifact> result = UploadCommand.PlanArtifacts(current, uploaded: null);

        Assert.Equal(new[] { "a", "b" }, result.Select(artifact => artifact.Name));
    }

    [Fact]
    public void PlanArtifacts_UploadedHasSameNames_ReturnsEmpty()
    {
        PatchManifest current = CreateInMemoryManifest(("a", 10), ("b", 20));
        PatchManifest uploaded = CreateInMemoryManifest(("a", 10), ("b", 20));

        IReadOnlyList<ManifestArtifact> result = UploadCommand.PlanArtifacts(current, uploaded);

        Assert.Empty(result);
    }

    [Fact]
    public void PlanArtifacts_UploadedHasSubsetOfNames_ReturnsOnlyNewNames()
    {
        PatchManifest current = CreateInMemoryManifest(("a", 10), ("b", 20), ("c", 30));
        PatchManifest uploaded = CreateInMemoryManifest(("a", 10));

        IReadOnlyList<ManifestArtifact> result = UploadCommand.PlanArtifacts(current, uploaded);

        Assert.Equal(new[] { "b", "c" }, result.Select(artifact => artifact.Name));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("schema-violation")]
    public void ReadUploadState_StateFileIsCorrupted_ReturnsNullAndPlanArtifactsReturnsAllArtifacts(string corruptionKind)
    {
        using var environment = new UploadTestEnvironment(("a", 5), ("b", 7));
        string corruptedContent = corruptionKind == "not-json"
            ? "not-json"
            : CreateSchemaViolatingStateContent(environment.OutputPath);
        File.WriteAllText(environment.UploadStatePath, corruptedContent);

        PatchManifest? uploadedState = ManifestStore.ReadUploadState(environment.OutputPath);
        IReadOnlyList<ManifestArtifact> result = UploadCommand.PlanArtifacts(environment.Manifest, uploadedState);

        Assert.Null(uploadedState);
        Assert.Equal(environment.ArtifactNames, result.Select(artifact => artifact.Name));
    }

    [Fact]
    public async Task ExecuteAsync_ManifestIsMissing_ThrowsAndCallsNoStorage()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        Directory.Delete(environment.OutputPath, recursive: true);
        Directory.CreateDirectory(environment.OutputPath);
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("manifest.json", exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactIsMissing_ThrowsAndCallsNoStorage()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("산출물이 없습니다", exception.Message, StringComparison.Ordinal);
        Assert.Contains(environment.ArtifactNames[0], exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactSizeDiffersFromStoredSize_ThrowsAndCallsNoStorage()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], length: 4);
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("크기가 다릅니다", exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactStoredSizeOverOneGibibyte_ThrowsAndCallsNoStorage()
    {
        // storedSize의 1 GiB 상한 검사는 실제 파일을 만들지 않고도 선행되므로 산출물 파일을 쓰지 않는다.
        const long overCap = MAX_ARTIFACT_SIZE_BYTES + 1;
        using var environment = new UploadTestEnvironment(("a", overCap));
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("1 GiB 상한", exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactStoredSizeAtOneGibibyteBoundary_DoesNotTriggerCapRejection()
    {
        // 실제 1 GiB 파일을 만들지 않고, storedSize가 상한과 정확히 같을 때 상한 검사만 통과해
        // 다음 검사(파일 존재 여부)로 넘어가는지 확인한다. 상한 위반이었다면 "1 GiB 상한" 메시지가 나온다.
        const long atCap = MAX_ARTIFACT_SIZE_BYTES;
        using var environment = new UploadTestEnvironment(("a", atCap));
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("산출물이 없습니다", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1 GiB 상한", exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_BucketAndArtifactPathsHaveDisallowedCharacters_ReportsAllProblemsTogetherAndCallsNoStorage()
    {
        using var environment = new UploadTestEnvironment(groupId: "그룹 이름", ("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, "bad bucket");

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("버킷 이름이 올바르지 않습니다: bad bucket", exception.Message, StringComparison.Ordinal);
        Assert.Contains("산출물 경로가 올바르지 않습니다", exception.Message, StringComparison.Ordinal);
        Assert.Contains(environment.ArtifactNames[0], exception.Message, StringComparison.Ordinal);
        Assert.Empty(storage.Calls);
    }

    [Theory]
    [InlineData("valid-bucket.01", true)]
    [InlineData("has space", false)]
    [InlineData("has/slash", false)]
    [InlineData("한글버킷", false)]
    [InlineData("has?query", false)]
    [InlineData("has\\backslash", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void IsRemotePathSegment_BucketNameCandidates_MatchesAllowedCharacterRule(string bucket, bool expected)
    {
        Assert.Equal(expected, RelativePathValidator.IsRemotePathSegment(bucket));
    }

    [Fact]
    public async Task ExecuteAsync_FirstRun_UpsertsArtifactsInOrderThenCreatesManifestAndWritesLocalState()
    {
        using var environment = new UploadTestEnvironment(("a", 5), ("b", 7));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        environment.WriteArtifactFile(environment.ArtifactNames[1], 7);
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        UploadSummary summary = await sut.ExecuteAsync(environment.OutputPath);

        Assert.Equal(
            new[]
            {
                $"upsert:{environment.ArtifactNames[0]}",
                $"upsert:{environment.ArtifactNames[1]}",
                "manifest:manifests/0.json"
            },
            storage.Calls);
        Assert.Equal(new UploadSummary(UploadedCount: 2, UploadedBytes: 12, SkippedCount: 0, ReleaseVersion: 0), summary);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(environment.OutputPath, "manifest.json")),
            File.ReadAllBytes(environment.UploadStatePath));
    }

    [Fact]
    public async Task ExecuteAsync_ManifestHasNonZeroReleaseVersion_ReturnsMatchingVersionAndUsesVersionedManifestPath()
    {
        using var environment = new UploadTestEnvironment(groupId: "group", releaseVersion: 5, ("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        var storage = new FakeUploadStorage();
        var sut = new UploadCommand(storage, ValidBucket);

        UploadSummary summary = await sut.ExecuteAsync(environment.OutputPath);

        Assert.Equal(
            new[] { $"upsert:{environment.ArtifactNames[0]}", "manifest:manifests/5.json" },
            storage.Calls);
        Assert.Equal(5, summary.ReleaseVersion);
    }

    [Fact]
    public async Task ExecuteAsync_SameUploadState_SkipsArtifactsAndReusesManifestWithIdenticalRemoteBytes()
    {
        using var environment = new UploadTestEnvironment(("a", 5), ("b", 7));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        environment.WriteArtifactFile(environment.ArtifactNames[1], 7);
        environment.WriteUploadState(("a", 5), ("b", 7));
        byte[] currentManifestBytes = File.ReadAllBytes(Path.Combine(environment.OutputPath, "manifest.json"));
        var storage = new FakeUploadStorage(
            existingManifestBytesByRemotePath: new Dictionary<string, byte[]> { ["manifests/0.json"] = currentManifestBytes });
        var sut = new UploadCommand(storage, ValidBucket);

        UploadSummary summary = await sut.ExecuteAsync(environment.OutputPath);

        Assert.Equal(new[] { "manifest:manifests/0.json" }, storage.Calls);
        Assert.Equal(new UploadSummary(UploadedCount: 0, UploadedBytes: 0, SkippedCount: 2, ReleaseVersion: 0), summary);
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactUpsertFails_StopsWithoutSubsequentCallsAndPreservesPreviousLocalState()
    {
        using var environment = new UploadTestEnvironment(("a", 5), ("b", 7));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        environment.WriteArtifactFile(environment.ArtifactNames[1], 7);
        environment.WriteUploadState(("previous", 3));
        byte[] stateBefore = File.ReadAllBytes(environment.UploadStatePath);
        var storage = new FakeUploadStorage(
            upsertFailureMessagesByRemotePath: new Dictionary<string, string>
            {
                [environment.ArtifactNames[0]] = "Storage 요청이 실패했습니다. stage=artifact-upsert"
            });
        var sut = new UploadCommand(storage, ValidBucket);

        await Assert.ThrowsAsync<BuildException>(() => sut.ExecuteAsync(environment.OutputPath));

        Assert.Empty(storage.Calls);
        Assert.Equal(stateBefore, File.ReadAllBytes(environment.UploadStatePath));
    }

    [Fact]
    public async Task ExecuteAsync_ManifestBytesDifferFromExistingRemote_FirstRun_ThrowsAndDoesNotCreateLocalState()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        byte[] differentRemoteManifestBytes = "different-remote-bytes"u8.ToArray();
        var storage = new FakeUploadStorage(
            existingManifestBytesByRemotePath:
                new Dictionary<string, byte[]> { ["manifests/0.json"] = differentRemoteManifestBytes });
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("이미 다른 내용으로 존재합니다", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { $"upsert:{environment.ArtifactNames[0]}" }, storage.Calls);
        Assert.False(File.Exists(environment.UploadStatePath));
    }

    [Fact]
    public async Task ExecuteAsync_ManifestBytesDifferFromExistingRemote_WithPreviousState_PreservesPreviousLocalStateBytes()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        // "previous" 산출물로 이전 상태를 만들어 현재 manifest.json과 바이트가 다르게 한다. 같은 바이트라면
        // 상태가 잘못 덮어써져도 값이 같아 보존 여부를 검증하지 못한다.
        environment.WriteUploadState(("previous", 3));
        byte[] stateBefore = File.ReadAllBytes(environment.UploadStatePath);
        byte[] currentManifestBytes = File.ReadAllBytes(Path.Combine(environment.OutputPath, "manifest.json"));
        Assert.NotEqual(currentManifestBytes, stateBefore);
        byte[] differentRemoteManifestBytes = "different-remote-bytes"u8.ToArray();
        var storage = new FakeUploadStorage(
            existingManifestBytesByRemotePath:
                new Dictionary<string, byte[]> { ["manifests/0.json"] = differentRemoteManifestBytes });
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Contains("이미 다른 내용으로 존재합니다", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { $"upsert:{environment.ArtifactNames[0]}" }, storage.Calls);
        Assert.Equal(stateBefore, File.ReadAllBytes(environment.UploadStatePath));
    }

    [Fact]
    public async Task ExecuteAsync_StorageFailureMessage_PropagatesUnchangedWithoutKeyOrRawResponseBody()
    {
        using var environment = new UploadTestEnvironment(("a", 5));
        environment.WriteArtifactFile(environment.ArtifactNames[0], 5);
        const string safeMessage =
            "Storage 요청이 실패했습니다. stage=artifact-upsert, remotePath=files/group/1/a.v1.0, "
            + "exceptionType=SupabaseStorageException, statusCode=500, errorMessage=internal error";
        var storage = new FakeUploadStorage(
            upsertFailureMessagesByRemotePath:
                new Dictionary<string, string> { [environment.ArtifactNames[0]] = safeMessage });
        var sut = new UploadCommand(storage, ValidBucket);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.ExecuteAsync(environment.OutputPath));

        Assert.Equal(safeMessage, exception.Message);
        Assert.DoesNotContain("apikey", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sb_secret_", exception.Message, StringComparison.Ordinal);
    }

    private static PatchManifest CreateInMemoryManifest(params (string Name, long StoredSize)[] artifacts)
    {
        ManifestEntry[] entries = artifacts
            .Select(
                (artifact, index) => new ManifestEntry
                {
                    Path = $"entry-{index}",
                    Version = "1.0",
                    Size = artifact.StoredSize,
                    Source = EntrySource.File,
                    Name = artifact.Name,
                    StoredSize = artifact.StoredSize,
                    Checksum = new string('a', 64)
                })
            .ToArray();

        return new PatchManifest
        {
            SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
            ReleaseVersion = 0,
            SourcePath = "data",
            SourceCommit = new string('a', 40),
            Groups = new[]
            {
                new ManifestGroup
                {
                    Id = "group",
                    Version = 1,
                    Packing = PackingKind.File,
                    Compression = CompressionKind.None,
                    Entries = entries
                }
            }
        };
    }

    private static string CreateSchemaViolatingStateContent(string outputPath)
    {
        JObject root = JObject.Parse(File.ReadAllText(Path.Combine(outputPath, "manifest.json")));
        root.Remove("releaseVersion");
        return root.ToString(Formatting.None);
    }

    private static PatchManifest CreateValidatedManifest(
        string groupId,
        int releaseVersion,
        (string ShortName, long StoredSize)[] artifacts,
        out IReadOnlyList<string> artifactNames)
    {
        const int groupVersion = 1;
        const string entryVersion = "1.0";
        var entries = new List<ManifestEntry>();
        var names = new List<string>();

        foreach ((string shortName, long storedSize) in artifacts)
        {
            string name = $"files/{groupId}/{groupVersion}/{shortName}.v{entryVersion}";
            entries.Add(
                new ManifestEntry
                {
                    Path = shortName,
                    Version = entryVersion,
                    Size = storedSize,
                    Source = EntrySource.File,
                    Name = name,
                    StoredSize = storedSize,
                    Checksum = new string('a', 64)
                });
            names.Add(name);
        }

        artifactNames = names;
        return new PatchManifest
        {
            SchemaVersion = PatchManifest.CURRENT_SCHEMA_VERSION,
            ReleaseVersion = releaseVersion,
            SourcePath = "data",
            SourceCommit = new string('a', 40),
            Groups = new[]
            {
                new ManifestGroup
                {
                    Id = groupId,
                    Version = groupVersion,
                    Packing = PackingKind.File,
                    Compression = CompressionKind.None,
                    Entries = entries
                }
            }
        };
    }

    /// <summary>
    /// 산출물 upsert는 원격 경로별 강제 실패 메시지로 시뮬레이션하고, 세대 매니페스트는 실제로 로컬 바이트를 읽어
    /// 미리 채워둔 원격 바이트와 비교해 최초 생성·동일 바이트 재사용·다른 바이트 버전 충돌을 그대로 재현한다.
    /// </summary>
    private sealed class FakeUploadStorage : IUploadStorage
    {
        private readonly IReadOnlyDictionary<string, string> _upsertFailureMessagesByRemotePath;
        private readonly Dictionary<string, byte[]> _existingManifestBytesByRemotePath;

        public FakeUploadStorage(
            IReadOnlyDictionary<string, string>? upsertFailureMessagesByRemotePath = null,
            IReadOnlyDictionary<string, byte[]>? existingManifestBytesByRemotePath = null)
        {
            _upsertFailureMessagesByRemotePath =
                upsertFailureMessagesByRemotePath ?? new Dictionary<string, string>(StringComparer.Ordinal);
            _existingManifestBytesByRemotePath = existingManifestBytesByRemotePath is null
                ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
                : new Dictionary<string, byte[]>(existingManifestBytesByRemotePath, StringComparer.Ordinal);
        }

        public List<string> Calls { get; } = new();

        public Task UpsertArtifactAsync(string localPath, string remotePath)
        {
            if (_upsertFailureMessagesByRemotePath.TryGetValue(remotePath, out string? failureMessage))
            {
                throw new BuildException(failureMessage);
            }

            Calls.Add($"upsert:{remotePath}");
            return Task.CompletedTask;
        }

        public Task CreateOrVerifyManifestAsync(string localPath, string remotePath)
        {
            byte[] localBytes = File.ReadAllBytes(localPath);

            if (_existingManifestBytesByRemotePath.TryGetValue(remotePath, out byte[]? existingBytes))
            {
                if (!existingBytes.AsSpan().SequenceEqual(localBytes))
                {
                    throw new BuildException($"세대 매니페스트가 이미 다른 내용으로 존재합니다: {remotePath}");
                }
            }
            else
            {
                _existingManifestBytesByRemotePath[remotePath] = localBytes;
            }

            Calls.Add($"manifest:{remotePath}");
            return Task.CompletedTask;
        }
    }

    private sealed class UploadTestEnvironment : IDisposable
    {
        private readonly string _testPath;
        private readonly string _groupId;

        public string OutputPath { get; }
        public string UploadStatePath => Path.Combine(OutputPath, ".gpk-upload-state.json");
        public PatchManifest Manifest { get; }
        public IReadOnlyList<string> ArtifactNames { get; }

        public UploadTestEnvironment(params (string Name, long StoredSize)[] artifacts)
            : this(groupId: "group", artifacts)
        {
        }

        public UploadTestEnvironment(string groupId, params (string Name, long StoredSize)[] artifacts)
            : this(groupId, releaseVersion: 0, artifacts)
        {
        }

        public UploadTestEnvironment(string groupId, int releaseVersion, params (string Name, long StoredSize)[] artifacts)
        {
            _testPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
            _groupId = groupId;
            OutputPath = Path.Combine(_testPath, "patches");
            Manifest = CreateValidatedManifest(groupId, releaseVersion, artifacts, out IReadOnlyList<string> artifactNames);
            ArtifactNames = artifactNames;
            ManifestStore.WriteAtomically(OutputPath, Manifest);
        }

        public void Dispose()
        {
            Directory.Delete(_testPath, recursive: true);
        }

        public void WriteArtifactFile(string name, long length)
        {
            string path = Path.Combine(OutputPath, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = File.Create(path);
            stream.SetLength(length);
        }

        public void WriteUploadState(params (string ShortName, long StoredSize)[] artifacts)
        {
            PatchManifest state = CreateValidatedManifest(_groupId, releaseVersion: 0, artifacts, out _);
            string temporaryStateDirectory = Path.Combine(_testPath, $"state-{Guid.NewGuid():N}");
            ManifestStore.WriteAtomically(temporaryStateDirectory, state);
            byte[] bytes = File.ReadAllBytes(Path.Combine(temporaryStateDirectory, "manifest.json"));
            File.WriteAllBytes(UploadStatePath, bytes);
            Directory.Delete(temporaryStateDirectory, recursive: true);
        }
    }
}
