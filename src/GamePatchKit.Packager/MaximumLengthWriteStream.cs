namespace GamePatchKit.Packager;

// Caps how much a decoder is allowed to produce, failing on the first byte past the limit rather than after
// the fact.
//
// Every decompression here writes into something whose final size the manifest already declares, and the
// declared size is checked afterwards anyway - but "afterwards" is the problem. A small, self-consistent zstd
// object can expand without bound, so a check that only runs at the end lets the expansion happen first: CPU
// burned when the destination merely hashes, disk filled when it is a real file.
internal sealed class MaximumLengthWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maximumLength;
    private readonly string _message;

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

    public MaximumLengthWriteStream(Stream inner, long maximumLength, string message)
    {
        _inner = inner;
        _maximumLength = maximumLength;
        _message = message;
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _inner.FlushAsync(cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        _inner.Write(buffer, offset, count);
        BytesWritten += count;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        BytesWritten += buffer.Length;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return WriteLegacyAsync(buffer, offset, count, cancellationToken);
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

    private void EnsureCapacity(int count)
    {
        if (count > _maximumLength - BytesWritten)
        {
            throw new InvalidDataException(_message);
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
        BytesWritten += count;
    }
}
