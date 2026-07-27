using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Forwards every IRuntimeStorage member to any inner storage, except that the staging area for one named group
// fails to promote a fixed number of times first. Drives the multi-group activation batch scenario: one group's
// promotion failing must leave no partial-ready state observable, for every adapter.
public sealed class FailingGroupPromotionStorageDecorator : IRuntimeStorage
{
    private readonly IRuntimeStorage _inner;
    private readonly string _failingGroup;
    private int _remainingFailures;

    public FailingGroupPromotionStorageDecorator(IRuntimeStorage inner, string failingGroup, int failuresBeforeSuccess)
    {
        _inner = inner;
        _failingGroup = failingGroup;
        _remainingFailures = failuresBeforeSuccess;
    }

    public Task<IAsyncDisposable> AcquirePackageWriterLockAsync(string packageId, CancellationToken cancellationToken)
    {
        return _inner.AcquirePackageWriterLockAsync(packageId, cancellationToken);
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

    public async Task<IRuntimeStagingArea> CreateStagingAreaAsync(string packageId, string group, CancellationToken cancellationToken)
    {
        IRuntimeStagingArea inner = await _inner.CreateStagingAreaAsync(packageId, group, cancellationToken).ConfigureAwait(false);
        return group == _failingGroup ? new FailingPromotionStagingArea(inner, this) : inner;
    }

    public Task<Stream> CreateScratchStreamAsync(string packageId, CancellationToken cancellationToken)
    {
        return _inner.CreateScratchStreamAsync(packageId, cancellationToken);
    }

    // CAS loop rather than a plain read-decrement-write: concurrent promotions racing this decorator must still
    // consume exactly "failuresBeforeSuccess" tokens in total, not lose or double-spend one to a data race.
    private bool TryConsumeFailure()
    {
        while (true)
        {
            int current = Volatile.Read(ref _remainingFailures);

            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _remainingFailures, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    private sealed class FailingPromotionStagingArea : IRuntimeStagingArea
    {
        private readonly IRuntimeStagingArea _inner;
        private readonly FailingGroupPromotionStorageDecorator _owner;

        public FailingPromotionStagingArea(IRuntimeStagingArea inner, FailingGroupPromotionStorageDecorator owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public Task<Stream> CreateFileAsync(string relativePath, CancellationToken cancellationToken)
        {
            return _inner.CreateFileAsync(relativePath, cancellationToken);
        }

        public Task<string> PromoteAsync(CancellationToken cancellationToken)
        {
            if (_owner.TryConsumeFailure())
            {
                throw new IOException("injected group promotion failure");
            }

            return _inner.PromoteAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _inner.DisposeAsync();
        }
    }
}
