using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Forwards every IRuntimeStorage member to a real inner storage, but instruments the span between a successful
// AcquirePackageWriterLockAsync and its handle being disposed via a SharedLockObserver. PackageRuntime's own
// optimistic-concurrency retry (read state, compare, replace) converges to the same correct end state for two
// writers racing the same target even with a no-op lock, so asserting on the *outcome* of a concurrent
// InstallOrUpdateAsync call does not prove the lock is exclusive - only counting overlapping holders does. Two
// instances of this decorator, wrapping two SEPARATE storage instances but sharing one SharedLockObserver, is
// what makes the proof cross-instance rather than just cross-call.
public sealed class ExclusivityTrackingStorageDecorator : IRuntimeStorage
{
    private readonly IRuntimeStorage _inner;
    private readonly SharedLockObserver _observer;

    public ExclusivityTrackingStorageDecorator(IRuntimeStorage inner, SharedLockObserver observer)
    {
        _inner = inner;
        _observer = observer;
    }

    public async Task<IAsyncDisposable> AcquirePackageWriterLockAsync(string packageId, CancellationToken cancellationToken)
    {
        _observer.RecordAttempt();
        IAsyncDisposable handle = await _inner.AcquirePackageWriterLockAsync(packageId, cancellationToken).ConfigureAwait(false);
        await _observer.EnterAsync().ConfigureAwait(false);
        return new TrackedLock(handle, _observer);
    }

    public Task<byte[]?> ReadPackageStateAsync(string packageId, CancellationToken cancellationToken)
    {
        return _inner.ReadPackageStateAsync(packageId, cancellationToken);
    }

    public Task ReplacePackageStateAsync(string packageId, byte[] canonicalStateBytes, CancellationToken cancellationToken)
    {
        return _inner.ReplacePackageStateAsync(packageId, canonicalStateBytes, cancellationToken);
    }

    public Task<Stream?> OpenCachedArtifactAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        return _inner.OpenCachedArtifactAsync(packageId, relativePath, cancellationToken);
    }

    public Task<IRuntimeCacheWriter> CreateCacheWriterAsync(string packageId, string relativePath, CancellationToken cancellationToken)
    {
        return _inner.CreateCacheWriterAsync(packageId, relativePath, cancellationToken);
    }

    public Task<bool> InstallationExistsAsync(string installationKey, CancellationToken cancellationToken)
    {
        return _inner.InstallationExistsAsync(installationKey, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetInstallationFilePathsAsync(string installationKey, CancellationToken cancellationToken)
    {
        return _inner.GetInstallationFilePathsAsync(installationKey, cancellationToken);
    }

    public Task<Stream?> OpenInstallationFileAsync(string installationKey, string relativePath, CancellationToken cancellationToken)
    {
        return _inner.OpenInstallationFileAsync(installationKey, relativePath, cancellationToken);
    }

    public Task<IRuntimeStagingArea> CreateStagingAreaAsync(string packageId, string group, CancellationToken cancellationToken)
    {
        return _inner.CreateStagingAreaAsync(packageId, group, cancellationToken);
    }

    public Task<Stream> CreateScratchStreamAsync(string packageId, CancellationToken cancellationToken)
    {
        return _inner.CreateScratchStreamAsync(packageId, cancellationToken);
    }

    private sealed class TrackedLock : IAsyncDisposable
    {
        private readonly IAsyncDisposable _inner;
        private readonly SharedLockObserver _observer;

        public TrackedLock(IAsyncDisposable inner, SharedLockObserver observer)
        {
            _inner = inner;
            _observer = observer;
        }

        public async ValueTask DisposeAsync()
        {
            // Exit() must be recorded before the real lock is released, not after: a fast lock (e.g. an
            // in-memory SemaphoreSlim) can unblock a waiting second holder - who then calls EnterAsync() -
            // before this holder's own continuation reaches Exit(), which would count two holders as
            // concurrent even though the real lock was never actually held by both at once.
            _observer.Exit();
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
