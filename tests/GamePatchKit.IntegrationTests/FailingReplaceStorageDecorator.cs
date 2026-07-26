using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// Forwards every IRuntimeStorage member to a real FileSystemRuntimeStorage except ReplacePackageStateAsync,
// which fails a fixed number of times first - the state-commit-fails-keeps-previous-state scenario needs a
// real storage backing everything else, with just that one step made to fail on demand.
internal sealed class FailingReplaceStorageDecorator : IRuntimeStorage
{
    private readonly FileSystemRuntimeStorage _inner;
    private int _remainingFailures;

    public FailingReplaceStorageDecorator(FileSystemRuntimeStorage inner, int failuresBeforeSuccess)
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
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new IOException("injected state replacement failure");
        }

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
}
