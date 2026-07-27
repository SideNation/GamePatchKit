using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Runtime
{
    public interface IRuntimeStorage
    {
        Task<IAsyncDisposable> AcquirePackageWriterLockAsync(
            string packageId,
            CancellationToken cancellationToken);

        Task<byte[]?> ReadPackageStateAsync(
            string packageId,
            CancellationToken cancellationToken);

        Task ReplacePackageStateAsync(
            string packageId,
            byte[] canonicalStateBytes,
            CancellationToken cancellationToken);

        Task<Stream?> OpenCachedArtifactAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken);

        Task<IRuntimeCacheWriter> CreateCacheWriterAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken);

        Task<bool> InstallationExistsAsync(
            string installationKey,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<string>> GetInstallationFilePathsAsync(
            string installationKey,
            CancellationToken cancellationToken);

        Task<Stream?> OpenInstallationFileAsync(
            string installationKey,
            string relativePath,
            CancellationToken cancellationToken);

        Task<IRuntimeStagingArea> CreateStagingAreaAsync(
            string packageId,
            string group,
            CancellationToken cancellationToken);

        Task<Stream> CreateScratchStreamAsync(
            string packageId,
            CancellationToken cancellationToken);
    }
}
