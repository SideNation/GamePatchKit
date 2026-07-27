using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.Unity
{
    internal sealed class UnityRuntimeCacheWriter : IRuntimeCacheWriter
    {
        private readonly string _finalPath;
        private readonly FileStream _stream;
        private readonly string _temporaryPath;
        private bool _isCommitted;

        public Stream Content => _stream;

        public UnityRuntimeCacheWriter(FileStream stream, string temporaryPath, string finalPath)
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

            UnityAtomicFile.Replace(_temporaryPath, _finalPath);
            _isCommitted = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_isCommitted)
            {
                return;
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            UnityAtomicFile.TryDelete(_temporaryPath);
        }
    }
}

