using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Reference IArtifactTransport implementation: everything lives in memory, keyed exactly the way Runtime asks
// for it (manifest by manifestHash, artifacts by the content-addressed relative path from the manifest). No
// adapter-specific instrumentation lives here - counting, corrupting and failure injection are generic
// decorators (CountingArtifactTransportDecorator, CorruptingArtifactTransportDecorator) wrapped around whatever
// transport a conformance scenario is exercising, so the same instrumentation applies to every adapter.
public sealed class InMemoryArtifactTransport : IArtifactTransport
{
    private readonly Dictionary<string, byte[]> _manifests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _signatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _artifacts = new(StringComparer.Ordinal);

    public void AddManifest(FinalizedManifest release)
    {
        _manifests[release.ManifestHash] = release.GetCanonicalBytes();
    }

    public void AddManifest(string manifestHash, byte[] bytes)
    {
        _manifests[manifestHash] = bytes;
    }

    public void AddSignature(string manifestHash, byte[] bytes)
    {
        _signatures[manifestHash] = bytes;
    }

    public void AddArtifact(string relativePath, byte[] bytes)
    {
        _artifacts[relativePath] = bytes;
    }

    // Loads every file under a real publish tree (as produced by ConformanceFixture/FilePackageBuilder) into
    // the in-memory artifact map, keyed the same way FileSystemRuntimeStorage and HttpArtifactTransport key
    // content-addressed paths - forward-slash relative paths from the publish root.
    public void LoadArtifactsFromDirectory(string publishRoot)
    {
        foreach (string path in Directory.EnumerateFiles(publishRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(publishRoot, path).Replace(Path.DirectorySeparatorChar, '/');
            _artifacts[relativePath] = File.ReadAllBytes(path);
        }
    }

    public Task<Stream> OpenManifestAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_manifests.TryGetValue(target.ManifestHash, out byte[]? bytes))
        {
            throw new ArtifactTransportException(
                $"No manifest is registered for hash '{target.ManifestHash}'.",
                isTransient: false,
                isNotFound: true);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<Stream> OpenManifestSignatureAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_signatures.TryGetValue(target.ManifestHash, out byte[]? bytes))
        {
            throw new ArtifactTransportException(
                $"No manifest signature is registered for hash '{target.ManifestHash}'.",
                isTransient: false,
                isNotFound: true);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<Stream> OpenArtifactAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_artifacts.TryGetValue(relativePath, out byte[]? bytes))
        {
            throw new ArtifactTransportException(
                $"No artifact is registered at path '{relativePath}'.",
                isTransient: false,
                isNotFound: true);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
