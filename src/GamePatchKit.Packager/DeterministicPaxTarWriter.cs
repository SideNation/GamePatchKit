using System.Globalization;
using System.Text;

namespace GamePatchKit.Packager;

internal sealed class DeterministicPaxTarWriter : IAsyncDisposable
{
    private const int TarBlockSize = 512;
    private const int StreamBufferSize = 64 * 1024;
    private const long MaxOctalSize = 8_589_934_591L;

    private static readonly byte[] _endBlocks = new byte[TarBlockSize * 2];

    private readonly Stream _archive;
    private int _entryIndex;
    private bool _isDisposed;

    public DeterministicPaxTarWriter(Stream archive)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));

        if (!archive.CanWrite)
        {
            throw new ArgumentException("Archive stream must support writing.", nameof(archive));
        }
    }

    public static long ComputeArchiveSize(IEnumerable<(string RelativePath, long Size)> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        try
        {
            long archiveSize = TarBlockSize * 2;

            foreach ((string relativePath, long size) in entries)
            {
                if (string.IsNullOrEmpty(relativePath) || size < 0)
                {
                    throw new ArgumentException("Tar entries must have a path and non-negative size.", nameof(entries));
                }

                long attributeSize = CreateExtendedAttributes(relativePath, size).LongLength;
                archiveSize = checked(archiveSize
                    + TarBlockSize
                    + RoundUpToBlock(attributeSize)
                    + TarBlockSize
                    + RoundUpToBlock(size));
            }

            return archiveSize;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The deterministic tar size exceeds the supported stream length.", exception);
        }
    }

    public async Task WriteFileAsync(
        string relativePath,
        long size,
        Stream data,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (!data.CanRead)
        {
            throw new ArgumentException("Entry data stream must support reading.", nameof(data));
        }

        byte[] attributes = CreateExtendedAttributes(relativePath, size);
        string headerName = $"PaxHeaders/{_entryIndex:D8}";
        byte[] extendedHeader = CreateHeader(headerName, attributes.LongLength, typeFlag: (byte)'x');
        await _archive.WriteAsync(extendedHeader, cancellationToken).ConfigureAwait(false);
        await _archive.WriteAsync(attributes, cancellationToken).ConfigureAwait(false);
        await WritePaddingAsync(attributes.LongLength, cancellationToken).ConfigureAwait(false);

        string entryHeaderName = Encoding.UTF8.GetByteCount(relativePath) <= 100
            ? relativePath
            : $"PaxEntry/{_entryIndex:D8}";
        byte[] entryHeader = CreateHeader(entryHeaderName, size, typeFlag: (byte)'0');
        await _archive.WriteAsync(entryHeader, cancellationToken).ConfigureAwait(false);
        await CopyExactlyAsync(data, size, cancellationToken).ConfigureAwait(false);
        await WritePaddingAsync(size, cancellationToken).ConfigureAwait(false);
        _entryIndex++;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _archive.WriteAsync(_endBlocks).ConfigureAwait(false);
        await _archive.FlushAsync().ConfigureAwait(false);
    }

    private static byte[] CreateExtendedAttributes(string relativePath, long size)
    {
        using var stream = new MemoryStream();
        WritePaxRecord(stream, "path", relativePath);
        WritePaxRecord(stream, "size", size.ToString(CultureInfo.InvariantCulture));
        WritePaxRecord(stream, "mtime", "0");
        return stream.ToArray();
    }

    private static void WritePaxRecord(Stream destination, string key, string value)
    {
        byte[] body = Encoding.UTF8.GetBytes($"{key}={value}\n");
        int length = body.Length + 3;

        while (true)
        {
            int adjusted = body.Length
                + length.ToString(CultureInfo.InvariantCulture).Length
                + 1;

            if (adjusted == length)
            {
                break;
            }

            length = adjusted;
        }

        byte[] prefix = Encoding.ASCII.GetBytes(length.ToString(CultureInfo.InvariantCulture) + " ");
        destination.Write(prefix);
        destination.Write(body);
    }

    private static byte[] CreateHeader(string name, long size, byte typeFlag)
    {
        var header = new byte[TarBlockSize];
        WriteUtf8(header, offset: 0, length: 100, name);
        WriteOctal(header, offset: 100, length: 8, value: 420);
        WriteOctal(header, offset: 108, length: 8, value: 0);
        WriteOctal(header, offset: 116, length: 8, value: 0);
        WriteOctal(header, offset: 124, length: 12, value: size <= MaxOctalSize ? size : 0);
        WriteOctal(header, offset: 136, length: 12, value: 0);
        header.AsSpan(148, 8).Fill((byte)' ');
        header[156] = typeFlag;
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(header, 257);
        Encoding.ASCII.GetBytes("00").CopyTo(header, 263);
        WriteOctal(header, offset: 329, length: 8, value: 0);
        WriteOctal(header, offset: 337, length: 8, value: 0);

        int checksum = 0;
        foreach (byte value in header)
        {
            checksum += value;
        }

        WriteChecksum(header, checksum);
        return header;
    }

    private static void WriteUtf8(byte[] destination, int offset, int length, string value)
    {
        int written = Encoding.UTF8.GetBytes(value, destination.AsSpan(offset, length));
        if (written == 0)
        {
            throw new InvalidOperationException("A tar header name must not be empty.");
        }
    }

    private static void WriteOctal(byte[] destination, int offset, int length, long value)
    {
        string octal = Convert.ToString(value, 8);
        if (octal.Length > length - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit in a POSIX tar octal field.");
        }

        int padding = length - 1 - octal.Length;
        destination.AsSpan(offset, padding).Fill((byte)'0');
        Encoding.ASCII.GetBytes(octal).CopyTo(destination, offset + padding);
        destination[offset + length - 1] = 0;
    }

    private static void WriteChecksum(byte[] destination, int checksum)
    {
        string octal = Convert.ToString(checksum, 8);
        int padding = 6 - octal.Length;
        destination.AsSpan(148, padding).Fill((byte)'0');
        Encoding.ASCII.GetBytes(octal).CopyTo(destination, 148 + padding);
        destination[154] = 0;
        destination[155] = (byte)' ';
    }

    private async Task CopyExactlyAsync(Stream source, long expectedSize, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[StreamBufferSize];
        long remaining = expectedSize;

        while (remaining > 0)
        {
            int requested = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Tar entry data ended before its declared size.");
            }

            await _archive.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }

        if (await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Tar entry data exceeded its declared size.");
        }
    }

    private async Task WritePaddingAsync(long dataSize, CancellationToken cancellationToken)
    {
        int padding = (int)((TarBlockSize - (dataSize % TarBlockSize)) % TarBlockSize);
        if (padding > 0)
        {
            await _archive.WriteAsync(_endBlocks.AsMemory(0, padding), cancellationToken).ConfigureAwait(false);
        }
    }

    private static long RoundUpToBlock(long size)
    {
        return checked(((size + TarBlockSize - 1) / TarBlockSize) * TarBlockSize);
    }
}