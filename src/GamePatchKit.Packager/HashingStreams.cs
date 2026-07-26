using System.Security.Cryptography;

namespace GamePatchKit.Packager;

internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly Stream? _copyDestination;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _isHashFinalized;

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

    public HashingReadStream(Stream inner, Stream? copyDestination = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _copyDestination = copyDestination;
    }

    public string FinalizeHash()
    {
        if (_isHashFinalized)
        {
            throw new InvalidOperationException("The stream hash was already finalized.");
        }

        _isHashFinalized = true;
        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        _copyDestination?.Write(buffer, offset, read);
        Append(buffer.AsSpan(offset, read));
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (_copyDestination != null)
        {
            await _copyDestination.WriteAsync(buffer[..read], cancellationToken).ConfigureAwait(false);
        }

        Append(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        if (_isHashFinalized)
        {
            throw new InvalidOperationException("Cannot read after finalizing the stream hash.");
        }

        _hash.AppendData(bytes);
        BytesRead += bytes.Length;
    }

    private async Task<int> ReadLegacyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

        if (_copyDestination != null)
        {
            await _copyDestination.WriteAsync(buffer, offset, read, cancellationToken).ConfigureAwait(false);
        }

        Append(buffer.AsSpan(offset, read));
        return read;
    }
}

internal sealed class HashingWriteStream : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _isHashFinalized;

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

    public string FinalizeHash()
    {
        if (_isHashFinalized)
        {
            throw new InvalidOperationException("The stream hash was already finalized.");
        }

        _isHashFinalized = true;
        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Append(buffer.AsSpan(offset, count));
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Append(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Append(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
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

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
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

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (_isHashFinalized)
        {
            throw new InvalidOperationException("Cannot write after finalizing the stream hash.");
        }

        _hash.AppendData(bytes);
        BytesWritten += bytes.Length;
    }
}