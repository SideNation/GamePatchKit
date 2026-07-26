using System.Text;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

// Recovery paths where the client is holding a state it can no longer confirm. The property both tests are
// really about: a client must never be left with no way forward while the trusted target it was handed is
// healthy. Failing loudly is fine; failing every time, permanently, is not.
public class TestRuntimeStateRecovery
{
    [Fact]
    public async Task RetiredPreviousManifest_DoesNotBlockAnUpdateToAHealthyTarget()
    {
        FinalizedManifest releaseA = RuntimeFixture.CreateRelease();
        FinalizedManifest releaseB = RuntimeFixture.CreateRelease(coreBytes: Encoding.UTF8.GetBytes("core-v2"));
        var inner = new FakeArtifactTransport();
        AddRelease(inner, releaseA, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        AddRelease(inner, releaseB, Encoding.UTF8.GetBytes("core-v2"), RuntimeFixture.MapsBytes);
        var transport = new ManifestFailingTransport(inner);
        var storage = new FakeRuntimeStorage();
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseA));

        // The operator retired release A's manifest after publishing B. Nothing about B is unhealthy, but the
        // client's own state points at A and reading A's manifest now fails non-transiently.
        transport.FailManifest(releaseA.ManifestHash);

        PackageState updated = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseB));

        Assert.Equal(releaseB.ManifestHash, updated.Active.ManifestHash);
        Assert.Equal(PackageGroupStatus.Ready, Group(updated, "core").Status);
    }

    [Fact]
    public async Task RollbackWithCorruptedStaleOptionalData_MarksTheGroupNotInstalledInsteadOfFailingForever()
    {
        byte[] mapsV1 = RuntimeFixture.MapsBytes;
        byte[] mapsV2 = Encoding.UTF8.GetBytes("maps-v2");
        FinalizedManifest releaseA = RuntimeFixture.CreateRelease(mapsBytes: mapsV1);
        FinalizedManifest releaseB = RuntimeFixture.CreateRelease(mapsBytes: mapsV2);
        var transport = new FakeArtifactTransport();
        AddRelease(transport, releaseA, RuntimeFixture.CoreBytes, mapsV1);
        AddRelease(transport, releaseB, RuntimeFixture.CoreBytes, mapsV2);
        var storage = new FakeRuntimeStorage();
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseA));
        PackageState withMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });
        string installationKey = Group(withMaps, "maps").InstallationKey!;

        PackageState onB = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseB));
        Assert.Equal(PackageGroupStatus.Stale, Group(onB, "maps").Status);
        Assert.Equal(releaseA.ManifestHash, Group(onB, "maps").VerifiedManifestHash);

        // The stale installation still exists and still has the right paths, so loading the state keeps
        // trusting it - stale contents are not re-read. Its bytes are damaged, which only surfaces when a
        // rollback tries to verify them against release A again.
        storage.CorruptInstallationFile(installationKey, "data/maps.bin", Encoding.UTF8.GetBytes("damaged!"));

        PackageState rolledBack = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseA));

        Assert.Equal(releaseA.ManifestHash, rolledBack.Active.ManifestHash);
        Assert.Equal(PackageGroupStatus.Ready, Group(rolledBack, "core").Status);

        // Not stale: calling it stale would claim the data is good for some earlier manifest when it just
        // failed against this one, and PackageStateValidator rejects a stale group pinned to the active
        // manifest - which is what made every rollback attempt fail.
        PackageGroupState maps = Group(rolledBack, "maps");
        Assert.Equal(PackageGroupStatus.NotInstalled, maps.Status);
        Assert.Null(maps.VerifiedManifestHash);
        Assert.Null(maps.InstallationKey);
    }

    [Fact]
    public async Task RollbackWithIntactStaleOptionalData_StillReusesIt()
    {
        byte[] mapsV1 = RuntimeFixture.MapsBytes;
        byte[] mapsV2 = Encoding.UTF8.GetBytes("maps-v2");
        FinalizedManifest releaseA = RuntimeFixture.CreateRelease(mapsBytes: mapsV1);
        FinalizedManifest releaseB = RuntimeFixture.CreateRelease(mapsBytes: mapsV2);
        var transport = new FakeArtifactTransport();
        AddRelease(transport, releaseA, RuntimeFixture.CoreBytes, mapsV1);
        AddRelease(transport, releaseB, RuntimeFixture.CoreBytes, mapsV2);
        var storage = new FakeRuntimeStorage();
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseA));
        PackageState withMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });
        string installationKey = Group(withMaps, "maps").InstallationKey!;
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseB));

        PackageState rolledBack = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(releaseA));

        // The guard above must not cost the ordinary rollback its reuse: intact data still comes back ready
        // against the release it was installed for, with no download.
        PackageGroupState maps = Group(rolledBack, "maps");
        Assert.Equal(PackageGroupStatus.Ready, maps.Status);
        Assert.Equal(releaseA.ManifestHash, maps.VerifiedManifestHash);
        Assert.Equal(installationKey, maps.InstallationKey);
    }

    private static PackageGroupState Group(PackageState state, string name)
    {
        return state.Groups.Single(group => group.Name == name);
    }

    private static void AddRelease(
        FakeArtifactTransport transport,
        FinalizedManifest release,
        params byte[][] fileBytes)
    {
        transport.AddRelease(release);

        foreach (ManifestFileEntry file in release.Manifest.Files)
        {
            byte[] bytes = fileBytes.Single(candidate => RuntimeFixture.Hash(candidate) == file.FileHash);
            ManifestArtifact.FileArtifact artifact = release.Manifest.Artifacts
                .OfType<ManifestArtifact.FileArtifact>()
                .Single(
                    candidate => candidate.PrimaryArtifactHash
                        == ((FileSource.FileReference)file.Source).ArtifactHash);
            transport.AddArtifact(artifact.GetPayloadObjects().Single().Path, bytes);
        }
    }

    // Fails a chosen manifest the way a retired one would: non-transiently, so the runtime's retry loop does
    // not paper over it.
    private sealed class ManifestFailingTransport : IArtifactTransport
    {
        private readonly FakeArtifactTransport _inner;
        private readonly HashSet<string> _failing = new HashSet<string>(StringComparer.Ordinal);

        public ManifestFailingTransport(FakeArtifactTransport inner)
        {
            _inner = inner;
        }

        public void FailManifest(string manifestHash)
        {
            _failing.Add(manifestHash);
        }

        public Task<Stream> OpenManifestAsync(TargetManifestReference target, CancellationToken cancellationToken)
        {
            if (_failing.Contains(target.ManifestHash))
            {
                throw new ArtifactTransportException("manifest is gone", isTransient: false);
            }

            return _inner.OpenManifestAsync(target, cancellationToken);
        }

        public Task<Stream> OpenManifestSignatureAsync(TargetManifestReference target, CancellationToken cancellationToken)
        {
            return _inner.OpenManifestSignatureAsync(target, cancellationToken);
        }

        public Task<Stream> OpenArtifactAsync(string packageId, string objectPath, CancellationToken cancellationToken)
        {
            return _inner.OpenArtifactAsync(packageId, objectPath, cancellationToken);
        }
    }
}
