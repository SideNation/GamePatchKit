#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;
using UnityEngine;

namespace GamePatchKit.Unity
{
    public sealed class UnityRuntimeStorage : IRuntimeStorage
    {
        private const int LOCK_RETRY_DELAY_MILLISECONDS = 25;
        private const string RUNTIME_DIRECTORY_NAME = "GamePatchKit";

        private static readonly TimeSpan _defaultLockAcquisitionTimeout = TimeSpan.FromSeconds(30);

        private readonly TimeSpan _lockAcquisitionTimeout;
        private readonly string _runtimeRoot;

        public UnityRuntimeStorage()
            : this(Path.Combine(Application.persistentDataPath, RUNTIME_DIRECTORY_NAME))
        {
        }

        public UnityRuntimeStorage(string runtimeRoot)
            : this(runtimeRoot, _defaultLockAcquisitionTimeout)
        {
        }

        internal UnityRuntimeStorage(string runtimeRoot, TimeSpan lockAcquisitionTimeout)
        {
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                throw new ArgumentException("Runtime root must not be empty.", nameof(runtimeRoot));
            }

            _runtimeRoot = Path.GetFullPath(runtimeRoot);
            _lockAcquisitionTimeout = lockAcquisitionTimeout;
        }

        public async Task<IAsyncDisposable> AcquirePackageWriterLockAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            string lockPath = UnityStorageLayout.StateLockFilePath(_runtimeRoot, packageId);
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return new UnityFileLockHandle(stream);
                }
                catch (IOException exception)
                {
                    if (stopwatch.Elapsed >= _lockAcquisitionTimeout)
                    {
                        throw new IOException(
                            $"Could not acquire the package writer lock for '{packageId}' within {_lockAcquisitionTimeout}.",
                            exception);
                    }

                    await Task.Delay(LOCK_RETRY_DELAY_MILLISECONDS, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async Task<byte[]?> ReadPackageStateAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            string statePath = UnityStorageLayout.StateFilePath(_runtimeRoot, packageId);

            try
            {
                await using var stream = new FileStream(
                    statePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        public async Task ReplacePackageStateAsync(
            string packageId,
            byte[] canonicalStateBytes,
            CancellationToken cancellationToken)
        {
            if (canonicalStateBytes == null)
            {
                throw new ArgumentNullException(nameof(canonicalStateBytes));
            }

            Directory.CreateDirectory(UnityStorageLayout.StateDirectory(_runtimeRoot, packageId));
            string statePath = UnityStorageLayout.StateFilePath(_runtimeRoot, packageId);
            string temporaryPath = UnityAtomicFile.TemporaryPathFor(statePath);

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await stream.WriteAsync(canonicalStateBytes, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                UnityAtomicFile.Replace(temporaryPath, statePath);
            }
            catch
            {
                UnityAtomicFile.TryDelete(temporaryPath);
                throw;
            }
        }

        public Task<Stream?> OpenCachedArtifactAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = UnityStoragePath.Resolve(
                UnityStorageLayout.CacheDirectory(_runtimeRoot, packageId),
                relativePath);
            return Task.FromResult(OpenIfExists(path));
        }

        public Task<IRuntimeCacheWriter> CreateCacheWriterAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string finalPath = UnityStoragePath.Resolve(
                UnityStorageLayout.CacheDirectory(_runtimeRoot, packageId),
                relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            string temporaryPath = UnityAtomicFile.TemporaryPathFor(finalPath);
            var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            IRuntimeCacheWriter writer = new UnityRuntimeCacheWriter(
                stream,
                temporaryPath,
                finalPath);
            return Task.FromResult(writer);
        }

        public Task<bool> InstallationExistsAsync(
            string installationKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = UnityStorageLayout.InstallationDirectory(_runtimeRoot, installationKey);
            return Task.FromResult(Directory.Exists(directory));
        }

        public Task<IReadOnlyList<string>> GetInstallationFilePathsAsync(
            string installationKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = UnityStorageLayout.InstallationDirectory(_runtimeRoot, installationKey);

            if (!Directory.Exists(directory))
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            string[] paths = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'))
                .ToArray();
            return Task.FromResult<IReadOnlyList<string>>(paths);
        }

        public Task<Stream?> OpenInstallationFileAsync(
            string installationKey,
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = UnityStorageLayout.InstallationDirectory(_runtimeRoot, installationKey);
            string path = UnityStoragePath.Resolve(directory, relativePath);
            return Task.FromResult(OpenIfExists(path));
        }

        public Task<IRuntimeStagingArea> CreateStagingAreaAsync(
            string packageId,
            string group,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string operationId = Guid.NewGuid().ToString("N");
            string stagingDirectory = Path.Combine(
                UnityStorageLayout.StagingDirectory(_runtimeRoot, packageId),
                operationId);
            Directory.CreateDirectory(stagingDirectory);
            IRuntimeStagingArea stagingArea = new UnityRuntimeStagingArea(
                _runtimeRoot,
                packageId,
                stagingDirectory,
                operationId);
            return Task.FromResult(stagingArea);
        }

        public Task<Stream> CreateScratchStreamAsync(
            string packageId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string stagingRoot = UnityStorageLayout.StagingDirectory(_runtimeRoot, packageId);
            Directory.CreateDirectory(stagingRoot);
            string scratchPath = Path.Combine(
                stagingRoot,
                "scratch-" + Guid.NewGuid().ToString("N") + ".tmp");
            Stream stream = new FileStream(
                scratchPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose);
            return Task.FromResult(stream);
        }

        private static Stream? OpenIfExists(string path)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }
}
