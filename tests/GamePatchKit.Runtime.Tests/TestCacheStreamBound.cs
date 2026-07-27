using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

// Cache and installation contents are compared against the size and hash the manifest declares, and that
// comparison sits on the path every state load and download plan takes. It has to classify an oversized entry
// as corrupt from the declared size onward rather than reading whatever is there first.
public class TestCacheStreamBound
{
    [Fact]
    public async Task EndlessCachedObject_IsTreatedAsCorruptInsteadOfBeingReadToTheEnd()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddRelease(release);
        ArtifactPayloadObject corePayload = PayloadFor(release, "data/core.bin");
        transport.AddArtifact(corePayload.Path, RuntimeFixture.CoreBytes);
        transport.AddArtifact(PayloadFor(release, "data/maps.bin").Path, RuntimeFixture.MapsBytes);

        // A cache entry at the right path that never stops producing bytes. Without a bound the runtime reads
        // it forever and no install can ever start.
        //
        // Served once only: the damaged entry is what the runtime finds first, and the re-download is then
        // allowed to repair that path, which is what the storage contract promises for a corrupt object.
        var endless = new EndlessStream();
        bool served = false;
        storage.CacheStreamOverride = path =>
        {
            if (path != corePayload.Path || served)
            {
                return null;
            }

            served = true;
            return endless;
        };

        PackageState state = await new PackageRuntime(transport, storage).InstallOrUpdateAsync(
            RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);

        // Abandoned as soon as it went past what the manifest declares, and the object was re-fetched.
        Assert.True(
            endless.BytesRead <= corePayload.Size + 64 * 1024,
            $"read {endless.BytesRead} bytes from a cache entry declared as {corePayload.Size}");
        Assert.Equal(1, transport.ArtifactOpenCount[corePayload.Path]);
    }

    [Fact]
    public async Task CachedObjectOfExactlyTheDeclaredSize_IsStillReused()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddRelease(release);
        ArtifactPayloadObject corePayload = PayloadFor(release, "data/core.bin");
        transport.AddArtifact(corePayload.Path, RuntimeFixture.CoreBytes);
        transport.AddArtifact(PayloadFor(release, "data/maps.bin").Path, RuntimeFixture.MapsBytes);
        storage.PutCache(corePayload.Path, RuntimeFixture.CoreBytes);

        await new PackageRuntime(transport, storage).InstallOrUpdateAsync(RuntimeFixture.Target(release));

        // The bound is the declared size exactly, so a good entry must not be rejected by an off-by-one.
        Assert.False(transport.ArtifactOpenCount.ContainsKey(corePayload.Path));
    }

    private static ArtifactPayloadObject PayloadFor(FinalizedManifest release, string filePath)
    {
        ManifestFileEntry file = release.Manifest.Files.Single(entry => entry.Path == filePath);
        string artifactHash = ((FileSource.FileReference)file.Source).ArtifactHash;

        return release.Manifest.Artifacts
            .OfType<ManifestArtifact.FileArtifact>()
            .Single(artifact => artifact.PrimaryArtifactHash == artifactHash)
            .GetPayloadObjects()
            .Single();
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
