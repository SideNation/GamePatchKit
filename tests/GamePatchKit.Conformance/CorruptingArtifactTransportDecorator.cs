using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Wraps any IArtifactTransport and flips the first byte of a named artifact's response a fixed number of times,
// so the "corrupted artifact fails without poisoning the cache" scenario can be driven identically against every
// adapter instead of relying on an adapter-specific fake response mechanism.
public sealed class CorruptingArtifactTransportDecorator : IArtifactTransport
{
    private readonly IArtifactTransport _inner;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _remainingCorruptions = new(StringComparer.Ordinal);

    public CorruptingArtifactTransportDecorator(IArtifactTransport inner)
    {
        _inner = inner;
    }

    public void CorruptNextOpen(string relativePath, int times = 1)
    {
        lock (_gate)
        {
            _remainingCorruptions[relativePath] = times;
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

    public async Task<Stream> OpenArtifactAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        bool shouldCorrupt;

        lock (_gate)
        {
            shouldCorrupt = _remainingCorruptions.TryGetValue(relativePath, out int remaining) && remaining > 0;
            if (shouldCorrupt)
            {
                _remainingCorruptions[relativePath] = remaining - 1;
            }
        }

        Stream source = await _inner.OpenArtifactAsync(packageId, relativePath, cancellationToken).ConfigureAwait(false);

        if (!shouldCorrupt)
        {
            return source;
        }

        await using (source.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();

            if (bytes.Length > 0)
            {
                bytes[0] ^= 0xFF;
            }

            return new MemoryStream(bytes, writable: false);
        }
    }
}
