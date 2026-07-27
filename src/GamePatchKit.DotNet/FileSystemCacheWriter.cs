using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet;

// Content is a temporary file invisible to OpenCachedArtifactAsync until CommitAsync renames it into place, so
// a reader can never observe a partially written or unverified cache object. Runtime writes to Content and
// verifies size/hash itself before calling CommitAsync - this class does not re-check either.
internal sealed class FileSystemCacheWriter : IRuntimeCacheWriter
{
    private readonly FileStream _stream;
    private readonly string _temporaryPath;
    private readonly string _finalPath;
    private bool _committed;

    public Stream Content => _stream;

    public FileSystemCacheWriter(FileStream stream, string temporaryPath, string finalPath)
    {
        _stream = stream;
        _temporaryPath = temporaryPath;
        _finalPath = finalPath;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _stream.Flush(flushToDisk: true);
        await _stream.DisposeAsync().ConfigureAwait(false);

        // Replaces whatever was at the final path, including a corrupt object left by an earlier attempt -
        // cache commit has to be able to repair that path, not just fill it in when empty.
        File.Move(_temporaryPath, _finalPath, overwrite: true);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_committed)
        {
            return;
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
        AtomicFile.TryDeleteTemporary(_temporaryPath);
    }
}
