using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// Wires the real HttpArtifactTransport and FileSystemRuntimeStorage into a real PackageRuntime against a
// FakePublishServer serving an actual publish tree on disk - the same three pieces a host would assemble,
// exercised together instead of through Runtime's in-memory fakes.
internal sealed class AdapterHarness : IDisposable
{
    public string RuntimeRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        $"gamepatchkit-integration-runtime-{Guid.NewGuid():N}");

    public FakePublishServer Server { get; }

    public HttpClient HttpClient { get; }

    public FileSystemRuntimeStorage Storage { get; }

    public PackageRuntime Runtime { get; }

    public AdapterHarness(string publishRoot)
    {
        Directory.CreateDirectory(RuntimeRoot);
        Server = new FakePublishServer(publishRoot);
        HttpClient = new HttpClient(Server) { BaseAddress = new Uri("http://fake.local/") };
        Storage = new FileSystemRuntimeStorage(RuntimeRoot);
        Runtime = new PackageRuntime(new HttpArtifactTransport(HttpClient), Storage, DefaultCompressionCodecs.Create());
    }

    public void Dispose()
    {
        HttpClient.Dispose();

        if (Directory.Exists(RuntimeRoot))
        {
            Directory.Delete(RuntimeRoot, recursive: true);
        }
    }
}
