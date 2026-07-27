using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Unity
{
    internal sealed class TemporaryDownloadStream : Stream
    {
        private readonly FileStream _stream;
        private readonly string _temporaryPath;
        private bool _isDisposed;

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public TemporaryDownloadStream(string temporaryPath)
        {
            _temporaryPath = temporaryPath;
            _stream = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return _stream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _stream.Dispose();
                UnityAtomicFile.TryDelete(_temporaryPath);
                _isDisposed = true;
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            UnityAtomicFile.TryDelete(_temporaryPath);
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

