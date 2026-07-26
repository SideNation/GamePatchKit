using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

// Forwards every IRuntimeStorage member to any inner storage except ReplacePackageStateAsync, which fails a
// fixed number of times first - the state-commit-fails-keeps-previous-state scenario needs a real storage
// backing everything else, with just that one step made to fail on demand. Adapter-agnostic (wraps the
// IRuntimeStorage interface, not a concrete implementation) so the same decorator drives the scenario for
// every adapter the conformance suite runs against.
public sealed class FailingReplaceStorageDecorator : IRuntimeStorage
{
    private readonly IRuntimeStorage _inner;
    private int _remainingFailures;

    public FailingReplaceStorageDecorator(IRuntimeStorage inner, int failuresBeforeSuccess)
    {
        _inner = inner;
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
        if (TryConsumeFailure())
        {
            throw new IOException("injected state replacement failure");
        }

        return _inner.ReplacePackageStateAsync(packageId, canonicalStateBytes, cancellationToken);
    }

    // CAS loop rather than a plain read-decrement-write: concurrent callers racing this decorator must still
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
}
