using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Runs the full conformance suite against the in-memory reference adapter. Transport and storage are memoized
// (constructed once, reused for every CreateTransport()/CreateStorage() call within a test) so multiple runtime
// instances built during one scenario share the same manifests, artifacts, cache, state and installations -
// standing in for "two callers of the same package", the same way two FileSystemRuntimeStorage instances
// sharing a runtimeRoot do for the DotNet adapter.
public sealed class TestConformanceInMemoryAdapter : ConformanceTestBase
{
    private readonly InMemoryArtifactTransport _transport = new();
    private readonly InMemoryRuntimeStorage _storage = new();

    protected override IArtifactTransport CreateTransport()
    {
        return _transport;
    }

    protected override IRuntimeStorage CreateStorage()
    {
        return _storage;
    }

    protected override IRuntimeStorage CreateIsolatedStorage()
    {
        return new InMemoryRuntimeStorage();
    }

    protected override void RegisterRelease(FinalizedManifest release)
    {
        _transport.AddManifest(release);
        _transport.LoadArtifactsFromDirectory(Fixture.PublishRoot);
    }

    protected override Task RegisterRawManifestAsync(string packageId, string manifestHash, byte[] manifestBytes)
    {
        _transport.AddManifest(manifestHash, manifestBytes);
        return Task.CompletedTask;
    }
}
