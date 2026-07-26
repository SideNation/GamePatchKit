using GamePatchKit.Conformance;
using GamePatchKit.Core.Manifests;
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// Runs the full conformance suite against the real DotNet adapter (HttpArtifactTransport + FileSystemRuntimeStorage)
// backed by an actual publish tree served over HTTP and a real filesystem runtime root - the same two pieces a
// host application would assemble, exercised through the shared conformance scenarios rather than DotNet-specific
// ones only.
public sealed class TestConformanceDotNetAdapter : ConformanceTestBase
{
    private readonly string _runtimeRoot = Path.Combine(
        Path.GetTempPath(), $"gamepatchkit-conformance-runtime-{Guid.NewGuid():N}");
    private readonly List<string> _isolatedRoots = new();
    private readonly List<HttpClient> _httpClients = new();
    private FakePublishServer? _server;

    private FakePublishServer Server => _server ??= new FakePublishServer(Fixture.PublishRoot);

    protected override IArtifactTransport CreateTransport()
    {
        var client = new HttpClient(Server) { BaseAddress = new Uri("http://fake.local/") };
        _httpClients.Add(client);
        return new HttpArtifactTransport(client);
    }

    protected override IRuntimeStorage CreateStorage()
    {
        Directory.CreateDirectory(_runtimeRoot);
        return new FileSystemRuntimeStorage(_runtimeRoot);
    }

    protected override IRuntimeStorage CreateIsolatedStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"gamepatchkit-conformance-isolated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _isolatedRoots.Add(root);
        return new FileSystemRuntimeStorage(root);
    }

    protected override void RegisterRelease(FinalizedManifest release)
    {
        // FakePublishServer reads straight from Fixture.PublishRoot on every request, so a release built
        // through ConformanceFixture is already visible - nothing to register.
    }

    protected override async Task RegisterRawManifestAsync(string packageId, string manifestHash, byte[] manifestBytes)
    {
        string path = Path.Combine(Fixture.PublishRoot, packageId, "manifests", manifestHash, "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, manifestBytes);
    }

    public override void Dispose()
    {
        foreach (HttpClient client in _httpClients)
        {
            client.Dispose();
        }

        if (Directory.Exists(_runtimeRoot))
        {
            Directory.Delete(_runtimeRoot, recursive: true);
        }

        foreach (string root in _isolatedRoots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        base.Dispose();
    }
}
