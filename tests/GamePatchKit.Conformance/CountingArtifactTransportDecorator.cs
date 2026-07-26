using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Wraps any IArtifactTransport and counts OpenArtifactAsync calls per relative path, so a scenario can prove an
// artifact was (or was not) re-requested - e.g. an unchanged optional group reconnecting without downloading
// again - without depending on adapter-specific instrumentation.
public sealed class CountingArtifactTransportDecorator : IArtifactTransport
{
    private readonly IArtifactTransport _inner;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _openCounts = new(StringComparer.Ordinal);

    public CountingArtifactTransportDecorator(IArtifactTransport inner)
    {
        _inner = inner;
    }

    public int GetOpenCount(string relativePath)
    {
        lock (_gate)
        {
            return _openCounts.TryGetValue(relativePath, out int count) ? count : 0;
        }
    }

    public Task<Stream> OpenManifestAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        return _inner.OpenManifestAsync(target, cancellationToken);
    }

    public Task<Stream> OpenManifestSignatureAsync(TargetManifestReference target, CancellationToken cancellationToken)
    {
        return _inner.OpenManifestSignatureAsync(target, cancellationToken);
    }

    public Task<Stream> OpenArtifactAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _openCounts[relativePath] = (_openCounts.TryGetValue(relativePath, out int count) ? count : 0) + 1;
        }

        return _inner.OpenArtifactAsync(packageId, relativePath, cancellationToken);
    }
}
