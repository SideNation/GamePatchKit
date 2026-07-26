using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Runtime.Tests;

public class TestPackageRuntime
{
    [Fact]
    public async Task InitialInstallDownloadsRequiredOnlyThenInstallsOptionalGroup()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);

        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, initial.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(initial, "core").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "maps").Status);
        Assert.Equal(1, storage.ReplaceCount);
        Assert.Equal(1, transport.ArtifactOpenCount[PayloadPath(release, "data/core.bin")]);
        Assert.False(transport.ArtifactOpenCount.ContainsKey(PayloadPath(release, "data/maps.bin")));

        PackageState withMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });

        Assert.Equal(2, withMaps.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(withMaps, "maps").Status);
        Assert.Equal(initial.Active.ManifestHash, withMaps.Active.ManifestHash);
        Assert.Equal(2, storage.ReplaceCount);
    }

    [Fact]
    public async Task MultiGroupOptionalBatchCommitsOnceOrNotAtAll()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease(includeAudio: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes,
            RuntimeFixture.AudioBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldStateBytes = storage.StateBytes!.ToArray();

        storage.FailPromotionForGroup = "audio";
        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps", "audio" }));

        Assert.Equal(RuntimeErrorCodes.StagingFailed, exception.Error.Code);
        Assert.Equal(oldStateBytes, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "audio").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "maps").Status);

        storage.FailPromotionForGroup = null;
        PackageState installed = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "audio", "maps" });

        Assert.Equal(2, installed.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(installed, "audio").Status);
        Assert.Equal(PackageGroupStatus.Ready, Group(installed, "maps").Status);
        Assert.Equal(2, storage.ReplaceCount);
    }

    [Fact]
    public async Task CancellationKeepsVerifiedCacheAndNextRunReusesIt()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease(mapsRequired: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        string corePath = PayloadPath(release, "data/core.bin");
        string mapsPath = PayloadPath(release, "data/maps.bin");
        using var cancellation = new CancellationTokenSource();
        transport.BeforeArtifactOpen = path =>
        {
            if (path == mapsPath)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.InstallOrUpdateAsync(
                RuntimeFixture.Target(release),
                cancellationToken: cancellation.Token));

        Assert.Null(storage.StateBytes);
        Assert.True(storage.Cache.ContainsKey(corePath));

        transport.BeforeArtifactOpen = null;
        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Group(state, "core").Status);
        Assert.Equal(PackageGroupStatus.Ready, Group(state, "maps").Status);
        Assert.Equal(1, transport.ArtifactOpenCount[corePath]);
        Assert.Equal(2, transport.ArtifactOpenCount[mapsPath]);
    }

    [Fact]
    public async Task GlobalUpdateRelinksUnchangedOptionalMarksChangedStaleAndPreparesRequiredTransition()
    {
        FinalizedManifest first = RuntimeFixture.CreateRelease();
        byte[] changedCore = System.Text.Encoding.UTF8.GetBytes("core-data-v2");
        byte[] changedMaps = System.Text.Encoding.UTF8.GetBytes("maps-data-v2");
        FinalizedManifest coreUpdate = RuntimeFixture.CreateRelease(coreBytes: changedCore);
        FinalizedManifest mapsUpdate = RuntimeFixture.CreateRelease(
            coreBytes: changedCore,
            mapsBytes: changedMaps);
        FinalizedManifest mapsRequired = RuntimeFixture.CreateRelease(
            coreBytes: changedCore,
            mapsBytes: changedMaps,
            mapsRequired: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, first, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        AddRelease(transport, coreUpdate, changedCore, RuntimeFixture.MapsBytes);
        AddRelease(transport, mapsUpdate, changedCore, changedMaps);
        AddRelease(transport, mapsRequired, changedCore, changedMaps);
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(first));
        PackageState firstWithMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });
        string mapsInstallation = Group(firstWithMaps, "maps").InstallationKey!;
        int mapsDownloads = transport.ArtifactOpenCount[PayloadPath(first, "data/maps.bin")];

        PackageState relinked = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(coreUpdate));

        Assert.Equal(PackageGroupStatus.Ready, Group(relinked, "maps").Status);
        Assert.Equal(mapsInstallation, Group(relinked, "maps").InstallationKey);
        Assert.Equal(coreUpdate.ManifestHash, Group(relinked, "maps").VerifiedManifestHash);
        Assert.Equal(mapsDownloads, transport.ArtifactOpenCount[PayloadPath(first, "data/maps.bin")]);

        PackageState stale = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(mapsUpdate));

        Assert.Equal(PackageGroupStatus.Stale, Group(stale, "maps").Status);
        Assert.Equal(mapsInstallation, Group(stale, "maps").InstallationKey);
        Assert.False(transport.ArtifactOpenCount.ContainsKey(PayloadPath(mapsUpdate, "data/maps.bin")));

        PackageState required = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(mapsRequired));

        Assert.Equal(PackageGroupStatus.Ready, Group(required, "maps").Status);
        Assert.NotEqual(mapsInstallation, Group(required, "maps").InstallationKey);
        Assert.Equal(1, transport.ArtifactOpenCount[PayloadPath(mapsRequired, "data/maps.bin")]);
    }

    [Fact]
    public async Task StateConflictReplansWithoutOverwritingConcurrentBatch()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        string concurrentMaps = storage.AddInstallation(
            "maps",
            ("data/maps.bin", RuntimeFixture.MapsBytes));
        var concurrent = new PackageState(
            PackageState.CurrentSchemaVersion,
            stateRevision: 2,
            RuntimeFixture.PackageId,
            initial.Active,
            new[]
            {
                Group(initial, "core"),
                new PackageGroupState(
                    "maps",
                    PackageGroupStatus.Ready,
                    release.ManifestHash,
                    concurrentMaps),
            });
        storage.BeforeWriterLock = () =>
        {
            storage.StateBytes = PackageStateSerializer.Serialize(concurrent);
        };

        PackageState result = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });

        Assert.Equal(2, result.StateRevision);
        Assert.Equal(concurrentMaps, Group(result, "maps").InstallationKey);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task StateReplacementFailureKeepsOldState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldState = storage.StateBytes!.ToArray();
        storage.FailNextReplace = true;

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);
        Assert.Equal(oldState, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task WriterLockFailureKeepsOldState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldState = storage.StateBytes!.ToArray();
        storage.FailNextWriterLock = true;

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);
        Assert.Equal(oldState, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task StagingFailureKeepsStateAbsentAndVerifiedCacheReusable()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage
        {
            FailStagingFilePath = "data/core.bin",
        };
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        string corePayloadPath = PayloadPath(release, "data/core.bin");

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.StagingFailed, exception.Error.Code);
        Assert.Null(storage.StateBytes);
        Assert.True(storage.Cache.ContainsKey(corePayloadPath));

        storage.FailStagingFilePath = null;
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, transport.ArtifactOpenCount[corePayloadPath]);
    }

    [Fact]
    public async Task RejectsManifestHashMismatchBeforeDownloading()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddManifest(
            release.ManifestHash,
            release.GetCanonicalBytes().Concat(new byte[] { (byte)'\n' }).ToArray());
        var runtime = new PackageRuntime(transport, storage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);
        Assert.Empty(transport.ArtifactOpenCount);
        Assert.Null(storage.StateBytes);
    }

    [Fact]
    public async Task CorruptCacheObjectIsDownloadedAgain()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        string corePath = PayloadPath(release, "data/core.bin");
        storage.PutCache(corePath, new byte[] { 1, 2, 3 });
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, transport.ArtifactOpenCount[corePath]);
        Assert.Equal(RuntimeFixture.CoreBytes, storage.Cache[corePath]);
    }

    [Fact]
    public async Task RetriesTransientArtifactFailure()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        string corePath = PayloadPath(release, "data/core.bin");
        transport.FailTransiently(corePath, count: 1);
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(2, transport.ArtifactOpenCount[corePath]);
    }

    [Fact]
    public async Task MissingCodecFailsBeforeArtifactsAreDownloadedAndInjectedCodecHandlesMixedGroup()
    {
        FinalizedManifest release = CreateMixedCompressionRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        var withoutCodec = new PackageRuntime(transport, storage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => withoutCodec.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.MissingCompressionCodec, exception.Error.Code);
        Assert.Empty(transport.ArtifactOpenCount);

        var withCodec = new PackageRuntime(
            transport,
            storage,
            new[] { new PassThroughZstdCodec() });
        PackageState state = await withCodec.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(state.Groups).Status);
        Assert.Equal(2, transport.ArtifactOpenCount.Count);
    }

    [Fact]
    public async Task CorruptStateRecoversForTrustedGlobalTargetButBlocksOptionalRequest()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage
        {
            StateBytes = System.Text.Encoding.UTF8.GetBytes("{broken"),
        };
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);

        await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        PackageState recovered = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, recovered.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(recovered, "core").Status);
    }

    [Fact]
    public async Task StateRevisionSafeIntegerLimitFailsWithoutReplacingState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        var atLimit = new PackageState(
            PackageState.CurrentSchemaVersion,
            GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger,
            initial.PackageId,
            initial.Active,
            initial.Groups);
        storage.StateBytes = PackageStateSerializer.Serialize(atLimit);
        byte[] stateAtLimit = storage.StateBytes.ToArray();

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.StateInvalid, exception.Error.Code);
        Assert.Equal(stateAtLimit, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task NoOpAtStateRevisionSafeIntegerLimitDoesNotCreateAnotherRevision()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        var atLimit = new PackageState(
            PackageState.CurrentSchemaVersion,
            GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger,
            initial.PackageId,
            initial.Active,
            initial.Groups);
        storage.StateBytes = PackageStateSerializer.Serialize(atLimit);

        PackageState result = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger, result.StateRevision);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task MissingReferencedInstallationIsNotAcceptedAsActiveState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        storage.RemoveInstallation(Group(initial, "core").InstallationKey!);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.StateInvalid, exception.Error.Code);

        PackageState recovered = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, recovered.StateRevision);
        Assert.NotEqual(
            Group(initial, "core").InstallationKey,
            Group(recovered, "core").InstallationKey);
    }

    [Fact]
    public async Task DeletedFileForcesExactInstallationWithoutDownloadingUnchangedFile()
    {
        FinalizedManifest first = CreateCoreFilesRelease(includeSecond: true);
        FinalizedManifest second = CreateCoreFilesRelease(includeSecond: false);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            first,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        AddRelease(transport, second, RuntimeFixture.CoreBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(first));
        string initialKey = Group(initial, "core").InstallationKey!;
        string remainingPath = PayloadPath(first, "data/core.bin");
        int originalDownloads = transport.ArtifactOpenCount[remainingPath];

        PackageState updated = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(second));

        Assert.NotEqual(initialKey, Group(updated, "core").InstallationKey);
        Assert.Equal(originalDownloads, transport.ArtifactOpenCount[remainingPath]);
    }

    [Fact]
    public async Task SelectedInstalledGroupStillRequiresItsDeclaredCodec()
    {
        FinalizedManifest release = CreateMixedCompressionRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        await new PackageRuntime(
            transport,
            storage,
            new[] { new PassThroughZstdCodec() })
            .InstallOrUpdateAsync(RuntimeFixture.Target(release));

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(transport, storage)
                .InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.MissingCompressionCodec, exception.Error.Code);
        Assert.Equal(1, storage.ReplaceCount);
    }

    private static FinalizedManifest CreateMixedCompressionRelease()
    {
        string firstHash = RuntimeFixture.Hash(RuntimeFixture.CoreBytes);
        string secondHash = RuntimeFixture.Hash(RuntimeFixture.MapsBytes);
        var group = new ManifestGroupEntry("core", required: true);
        var artifacts = new List<ManifestArtifact>
        {
            new ManifestArtifact.FileArtifact(
                CompressionKind.None,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        firstHash,
                        CompressionKind.None),
                    RuntimeFixture.CoreBytes.LongLength,
                    firstHash)),
            new ManifestArtifact.FileArtifact(
                CompressionKind.Zstd,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        secondHash,
                        CompressionKind.Zstd),
                    RuntimeFixture.MapsBytes.LongLength,
                    secondHash)),
        };
        var files = new List<ManifestFileEntry>
        {
            new(
                "data/core.bin",
                "core",
                RuntimeFixture.CoreBytes.LongLength,
                firstHash,
                new FileSource.FileReference(firstHash)),
            new(
                "data/maps.bin",
                "core",
                RuntimeFixture.MapsBytes.LongLength,
                secondHash,
                new FileSource.FileReference(secondHash)),
        };
        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(RuntimeFixture.PackageId),
                right.ContentAddressedSortKey(RuntimeFixture.PackageId)));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { group },
            artifacts,
            files);
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return ReleaseIdentity.Finalize(draft, compactVersion: 0);
    }

    private static FinalizedManifest CreateCoreFilesRelease(bool includeSecond)
    {
        var bytesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["data/core.bin"] = RuntimeFixture.CoreBytes,
        };

        if (includeSecond)
        {
            bytesByPath.Add("data/legacy.bin", RuntimeFixture.MapsBytes);
        }

        var artifacts = new List<ManifestArtifact>();
        var files = new List<ManifestFileEntry>();

        foreach (KeyValuePair<string, byte[]> pair in bytesByPath)
        {
            string hash = RuntimeFixture.Hash(pair.Value);
            artifacts.Add(
                new ManifestArtifact.FileArtifact(
                    CompressionKind.None,
                    new FilePayload.Single(
                        ContentAddressedPath.FileSinglePayloadPath(
                            RuntimeFixture.PackageId,
                            hash,
                            CompressionKind.None),
                        pair.Value.LongLength,
                        hash)));
            files.Add(
                new ManifestFileEntry(
                    pair.Key,
                    "core",
                    pair.Value.LongLength,
                    hash,
                    new FileSource.FileReference(hash)));
        }

        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(RuntimeFixture.PackageId),
                right.ContentAddressedSortKey(RuntimeFixture.PackageId)));
        files.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.Path,
                right.Path));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("core", required: true) },
            artifacts,
            files);
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return ReleaseIdentity.Finalize(draft, compactVersion: 0);
    }

    private static void AddRelease(
        FakeArtifactTransport transport,
        FinalizedManifest release,
        params byte[][] fileBytes)
    {
        transport.AddRelease(release);

        for (int index = 0; index < release.Manifest.Files.Count; index++)
        {
            ManifestFileEntry file = release.Manifest.Files[index];
            byte[] bytes = fileBytes.Single(bytes => RuntimeFixture.Hash(bytes) == file.FileHash);
            ManifestArtifact.FileArtifact artifact = release.Manifest.Artifacts
                .OfType<ManifestArtifact.FileArtifact>()
                .Single(
                    candidate => candidate.PrimaryArtifactHash
                        == ((FileSource.FileReference)file.Source).ArtifactHash);
            transport.AddArtifact(
                Assert.Single(artifact.GetPayloadObjects()).Path,
                bytes);
        }
    }

    private static PackageGroupState Group(PackageState state, string name)
    {
        return state.Groups.Single(group => group.Name == name);
    }

    private static string PayloadPath(FinalizedManifest release, string filePath)
    {
        ManifestFileEntry file = release.Manifest.Files.Single(file => file.Path == filePath);
        string hash = ((FileSource.FileReference)file.Source).ArtifactHash;
        return Assert.Single(
            release.Manifest.Artifacts
                .OfType<ManifestArtifact.FileArtifact>()
                .Single(artifact => artifact.PrimaryArtifactHash == hash)
                .GetPayloadObjects()).Path;
    }
}
