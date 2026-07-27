using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Runtime
{
    internal sealed class RuntimeHashingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _isFinalized;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public RuntimeHashingReadStream(Stream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string FinalizeHash()
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("The hash was already finalized.");
            }

            _isFinalized = true;
            return ToHex(_hash.GetHashAndReset());
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            Append(buffer, offset, read);
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadLegacyAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private void Append(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                return;
            }

            if (_isFinalized)
            {
                throw new InvalidOperationException("Cannot read after finalizing the hash.");
            }

            _hash.AppendData(buffer, offset, count);
            BytesRead += count;
        }

        private async Task<int> ReadLegacyAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int read = await _inner
                .ReadAsync(buffer, offset, count, cancellationToken)
                .ConfigureAwait(false);
            Append(buffer, offset, read);
            return read;
        }

        private static string ToHex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);

            foreach (byte value in digest)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    internal sealed class RuntimeHashingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumBytes;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _isFinalized;

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public RuntimeHashingWriteStream(Stream inner, long maximumBytes)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _maximumBytes = maximumBytes >= 0
                ? maximumBytes
                : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        public string FinalizeHash()
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("The hash was already finalized.");
            }

            _isFinalized = true;
            return RuntimeHashingReadStreamToHex(_hash.GetHashAndReset());
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
            Append(buffer, offset, count);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return WriteLegacyAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        private void Append(byte[] buffer, int offset, int count)
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("Cannot write after finalizing the hash.");
            }

            _hash.AppendData(buffer, offset, count);
            BytesWritten += count;
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || BytesWritten > _maximumBytes - count)
            {
                throw new InvalidDataException("The stream exceeded its declared maximum length.");
            }
        }

        private async Task WriteLegacyAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Append(buffer, offset, count);
        }

        private static string RuntimeHashingReadStreamToHex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);

            foreach (byte value in digest)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
