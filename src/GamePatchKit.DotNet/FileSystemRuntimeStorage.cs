using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet;

// Filesystem-backed IRuntimeStorage. Package data lives under <runtimeRoot>/packages/<packageId>/, split into
// state, cache, installs and staging, per the PRD's DotNet adapter layout. This class does not interpret
// PackageState bytes or installationKey contents beyond the opaque "packageId/localId" form it mints itself in
// FileSystemStagingArea.PromoteAsync.
public sealed class FileSystemRuntimeStorage : IRuntimeStorage
{
    private const int LockRetryDelayMilliseconds = 25;
    private static readonly TimeSpan _defaultLockAcquisitionTimeout = TimeSpan.FromSeconds(30);

    private readonly string _runtimeRoot;
    private readonly TimeSpan _lockAcquisitionTimeout;

    public FileSystemRuntimeStorage(string runtimeRoot)
        : this(runtimeRoot, _defaultLockAcquisitionTimeout)
    {
    }

    // Test-only seam: an IOException while opening the lock file is not necessarily another writer holding
    // it - a full disk or a permissions problem looks identical - so retrying forever on cancellationToken
    // alone would turn a real, unrelated failure into a silent hang. A shorter timeout here lets a test prove
    // the bound actually fires without waiting out the real one.
    internal FileSystemRuntimeStorage(string runtimeRoot, TimeSpan lockAcquisitionTimeout)
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
        string lockPath = AdapterLayout.StateLockFilePath(_runtimeRoot, packageId);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(stream);
            }
            catch (IOException exception)
            {
                if (stopwatch.Elapsed >= _lockAcquisitionTimeout)
                {
                    throw new IOException(
                        $"Could not acquire the package writer lock for '{packageId}' within {_lockAcquisitionTimeout}.",
                        exception);
                }

                await Task.Delay(LockRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<byte[]?> ReadPackageStateAsync(string packageId, CancellationToken cancellationToken)
    {
        string statePath = AdapterLayout.StateFilePath(_runtimeRoot, packageId);

        try
        {
            // FileShare.Delete: on Windows, File.Move(..., overwrite: true) onto this path needs the current
            // reader to permit that, or the replace can fail with a sharing violation while this read is still
            // open. Unix rename() is unaffected either way.
            await using var stream = new FileStream(
                statePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
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
        Directory.CreateDirectory(AdapterLayout.StateDirectory(_runtimeRoot, packageId));
        string statePath = AdapterLayout.StateFilePath(_runtimeRoot, packageId);
        string temporaryPath = AtomicFile.TemporaryPathFor(statePath);

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(canonicalStateBytes, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // The old file is never opened for writing, so a crash up to this point leaves it exactly as it
            // was; only this rename can make the new bytes visible, and it is atomic on both Windows and Unix.
            File.Move(temporaryPath, statePath, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDeleteTemporary(temporaryPath);
            throw;
        }
    }

    public Task<Stream?> OpenCachedArtifactAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = AdapterPath.Resolve(AdapterLayout.CacheDirectory(_runtimeRoot, packageId), relativePath);
        return Task.FromResult(OpenIfExists(path));
    }

    public Task<IRuntimeCacheWriter> CreateCacheWriterAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string finalPath = AdapterPath.Resolve(AdapterLayout.CacheDirectory(_runtimeRoot, packageId), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        string temporaryPath = AtomicFile.TemporaryPathFor(finalPath);
        var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
        return Task.FromResult<IRuntimeCacheWriter>(new FileSystemCacheWriter(stream, temporaryPath, finalPath));
    }

    public Task<bool> InstallationExistsAsync(string installationKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(AdapterLayout.InstallationDirectory(_runtimeRoot, installationKey)));
    }

    public Task<IReadOnlyList<string>> GetInstallationFilePathsAsync(
        string installationKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = AdapterLayout.InstallationDirectory(_runtimeRoot, installationKey);

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
        string directory = AdapterLayout.InstallationDirectory(_runtimeRoot, installationKey);
        string path = AdapterPath.Resolve(directory, relativePath);
        return Task.FromResult(OpenIfExists(path));
    }

    public Task<IRuntimeStagingArea> CreateStagingAreaAsync(
        string packageId,
        string group,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string operationId = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(AdapterLayout.StagingDirectory(_runtimeRoot, packageId), operationId);
        Directory.CreateDirectory(stagingDirectory);
        return Task.FromResult<IRuntimeStagingArea>(
            new FileSystemStagingArea(_runtimeRoot, packageId, stagingDirectory, operationId));
    }

    public Task<Stream> CreateScratchStreamAsync(string packageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string stagingRoot = AdapterLayout.StagingDirectory(_runtimeRoot, packageId);
        Directory.CreateDirectory(stagingRoot);
        string scratchPath = Path.Combine(stagingRoot, "scratch-" + Guid.NewGuid().ToString("N") + ".tmp");
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
            // FileShare.Delete: a concurrent commit's File.Move(..., overwrite: true) onto this same path
            // needs a currently-open reader to allow that, or it can fail with a sharing violation on Windows.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
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
