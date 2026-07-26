using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Wraps any IArtifactTransport and blocks OpenArtifactAsync for a named relative path until the returned handle
// is disposed, so a scenario can force a deterministic overlap window - a real in-flight request stuck exactly
// where a cancellation or a competing writer needs it to be - instead of racing a fixed delay against timing.
public sealed class HoldingArtifactTransportDecorator : IArtifactTransport
{
    private readonly IArtifactTransport _inner;
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource> _holdGates = new(StringComparer.Ordinal);

    public HoldingArtifactTransportDecorator(IArtifactTransport inner)
    {
        _inner = inner;
    }

    public IDisposable HoldUntilReleased(string relativePath)
    {
        var holdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _holdGates[relativePath] = holdGate;
        }

        return new ReleaseOnDispose(holdGate);
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
        Task? holdTask;

        lock (_gate)
        {
            holdTask = _holdGates.TryGetValue(relativePath, out TaskCompletionSource? holdGate) ? holdGate.Task : null;
        }

        if (holdTask != null)
        {
            await holdTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _inner.OpenArtifactAsync(packageId, relativePath, cancellationToken).ConfigureAwait(false);
    }

    private sealed class ReleaseOnDispose : IDisposable
    {
        private readonly TaskCompletionSource _holdGate;

        public ReleaseOnDispose(TaskCompletionSource holdGate)
        {
            _holdGate = holdGate;
        }

        public void Dispose()
        {
            _holdGate.TrySetResult();
        }
    }
}
