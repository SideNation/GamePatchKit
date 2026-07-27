using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Runtime.Tests;

public class TestRuntimeArtifactMaterialization
{
    [Fact]
    public async Task ExtractsCanonicalBundleAndRejectsNonCanonicalTrailingBytes()
    {
        (FinalizedManifest goodRelease, byte[] goodBytes) =
            RuntimeArtifactFixtures.CreateBundleRelease();
        var goodTransport = new FakeArtifactTransport();
        var goodStorage = new FakeRuntimeStorage();
        goodTransport.AddRelease(goodRelease);
        goodTransport.AddArtifact(
            Assert.Single(goodRelease.Manifest.Artifacts).GetPayloadObjects()[0].Path,
            goodBytes);

        PackageState installed = await new PackageRuntime(goodTransport, goodStorage)
            .InstallOrUpdateAsync(RuntimeFixture.Target(goodRelease));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(installed.Groups).Status);

        (FinalizedManifest badRelease, byte[] badBytes) =
            RuntimeArtifactFixtures.CreateBundleRelease(addTrailingByte: true);
        var badTransport = new FakeArtifactTransport();
        var badStorage = new FakeRuntimeStorage();
        badTransport.AddRelease(badRelease);
        badTransport.AddArtifact(
            Assert.Single(badRelease.Manifest.Artifacts).GetPayloadObjects()[0].Path,
            badBytes);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(badTransport, badStorage)
                .InstallOrUpdateAsync(RuntimeFixture.Target(badRelease)));

        Assert.Equal(RuntimeErrorCodes.ArtifactCorrupted, exception.Error.Code);
        Assert.Null(badStorage.StateBytes);
    }

    [Fact]
    public async Task ExtractsCompressedBundleWithInjectedCodec()
    {
        (FinalizedManifest release, byte[] bytes) =
            RuntimeArtifactFixtures.CreateBundleRelease(compressed: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddRelease(release);
        transport.AddArtifact(
            Assert.Single(release.Manifest.Artifacts).GetPayloadObjects()[0].Path,
            bytes);
        var runtime = new PackageRuntime(
            transport,
            storage,
            new[] { new PassThroughZstdCodec() });

        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(state.Groups).Status);
    }

    [Fact]
    public async Task JoinsMultipartArtifactAndRejectsCorruptPart()
    {
        (FinalizedManifest release, byte[] first, byte[] second) =
            RuntimeArtifactFixtures.CreatePartsRelease();
        ManifestArtifact artifact = Assert.Single(release.Manifest.Artifacts);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddRelease(release);
        transport.AddArtifact(artifact.GetPayloadObjects()[0].Path, first);
        transport.AddArtifact(artifact.GetPayloadObjects()[1].Path, second);

        PackageState state = await new PackageRuntime(transport, storage)
            .InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(state.Groups).Status);

        var corruptTransport = new FakeArtifactTransport();
        var corruptStorage = new FakeRuntimeStorage();
        corruptTransport.AddRelease(release);
        corruptTransport.AddArtifact(artifact.GetPayloadObjects()[0].Path, first);
        corruptTransport.AddArtifact(
            artifact.GetPayloadObjects()[1].Path,
            second.Concat(new byte[] { 1 }).ToArray());

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(corruptTransport, corruptStorage)
                .InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.ArtifactCorrupted, exception.Error.Code);
        Assert.Null(corruptStorage.StateBytes);
    }

    [Fact]
    public async Task RejectsNonCanonicalManifestEvenWhenTrustedHashMatchesTransferredBytes()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        byte[] nonCanonical = release.GetCanonicalBytes()
            .Concat(new byte[] { (byte)'\n' })
            .ToArray();
        string transferredHash = GamePatchKit.Core.Sha256Hash.ComputeHex(nonCanonical);
        var target = new TargetManifestReference(
            release.Manifest.PackageId,
            release.Manifest.DataVersion,
            transferredHash);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddManifest(transferredHash, nonCanonical);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(transport, storage)
                .InstallOrUpdateAsync(target));

        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);
        Assert.Empty(transport.ArtifactOpenCount);
    }

    [Fact]
    public async Task RejectsSemanticallyInvalidManifestBeforeStaging()
    {
        FinalizedManifest valid = RuntimeFixture.CreateRelease();
        var invalidManifest = new ReleaseManifest(
            valid.Manifest.SchemaVersion,
            valid.Manifest.PackageId,
            valid.Manifest.DataVersion,
            valid.Manifest.CompactVersion,
            valid.Manifest.Groups.Reverse().ToArray(),
            valid.Manifest.Artifacts,
            valid.Manifest.Files);
        byte[] bytes = GamePatchKit.Core.Manifests.ReleaseIdentity.ComputeCanonicalBytes(
            invalidManifest);
        string hash = GamePatchKit.Core.Sha256Hash.ComputeHex(bytes);
        var target = new TargetManifestReference(
            invalidManifest.PackageId,
            invalidManifest.DataVersion,
            hash);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddManifest(hash, bytes);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(transport, storage)
                .InstallOrUpdateAsync(target));

        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);
        Assert.Null(storage.StateBytes);
    }

    [Fact]
    public async Task InjectedNativeZstdCodecStagesMixedCompressionGroup()
    {
        ICompressionCodec codec =
            GamePatchKit.Compression.NativeCompressions.ZstdCompressionCodecFactory.Create();
        using var compressedStream = new MemoryStream();
        using (var source = new MemoryStream(RuntimeFixture.MapsBytes, writable: false))
        {
            await codec.CompressAsync(source, compressedStream, CancellationToken.None);
        }

        byte[] compressedMaps = compressedStream.ToArray();
        string coreHash = RuntimeFixture.Hash(RuntimeFixture.CoreBytes);
        string mapsFileHash = RuntimeFixture.Hash(RuntimeFixture.MapsBytes);
        string mapsArtifactHash = RuntimeFixture.Hash(compressedMaps);
        var artifacts = new List<ManifestArtifact>
        {
            new ManifestArtifact.FileArtifact(
                CompressionKind.None,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        coreHash,
                        CompressionKind.None),
                    RuntimeFixture.CoreBytes.LongLength,
                    coreHash)),
            new ManifestArtifact.FileArtifact(
                CompressionKind.Zstd,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        mapsArtifactHash,
                        CompressionKind.Zstd),
                    compressedMaps.LongLength,
                    mapsArtifactHash)),
        };
        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(RuntimeFixture.PackageId),
                right.ContentAddressedSortKey(RuntimeFixture.PackageId)));
        var files = new[]
        {
            new ManifestFileEntry(
                "data/core.bin",
                "core",
                RuntimeFixture.CoreBytes.LongLength,
                coreHash,
                new FileSource.FileReference(coreHash)),
            new ManifestFileEntry(
                "data/maps.bin",
                "core",
                RuntimeFixture.MapsBytes.LongLength,
                mapsFileHash,
                new FileSource.FileReference(mapsArtifactHash)),
        };
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("core", required: true) },
            artifacts,
            files);
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        FinalizedManifest release = ReleaseIdentity.Finalize(draft, compactVersion: 0);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddRelease(release);
        transport.AddArtifact(
            artifacts.OfType<ManifestArtifact.FileArtifact>()
                .Single(artifact => artifact.Compression == CompressionKind.None)
                .GetPayloadObjects()[0].Path,
            RuntimeFixture.CoreBytes);
        transport.AddArtifact(
            artifacts.OfType<ManifestArtifact.FileArtifact>()
                .Single(artifact => artifact.Compression == CompressionKind.Zstd)
                .GetPayloadObjects()[0].Path,
            compressedMaps);

        PackageState state = await new PackageRuntime(
            transport,
            storage,
            new[] { codec })
            .InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(state.Groups).Status);
    }
}
