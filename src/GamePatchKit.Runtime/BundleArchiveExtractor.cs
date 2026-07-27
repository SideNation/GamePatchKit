using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime
{
    internal static class BundleArchiveExtractor
    {
        private const int TarBlockSize = 512;
        private const int StreamBufferSize = 64 * 1024;
        private const long MaxOctalSize = 8_589_934_591L;

        private static readonly byte[] _zeroBlocks = new byte[TarBlockSize * 2];

        public static long ComputeArchiveSize(
            ManifestArtifact.BundleArtifact artifact,
            IReadOnlyDictionary<string, ManifestFileEntry> filesByPath)
        {
            try
            {
                long size = TarBlockSize * 2;

                foreach (BundleEntry entry in artifact.Entries)
                {
                    ManifestFileEntry file = filesByPath[entry.Path];
                    long attributesSize = CreateExtendedAttributes(entry.Path, file.Size).LongLength;
                    size = checked(
                        size
                        + TarBlockSize
                        + RoundUp(attributesSize)
                        + TarBlockSize
                        + RoundUp(file.Size));
                }

                return size;
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("The bundle archive size exceeds the supported range.", exception);
            }
        }

        public static async Task ExtractAsync(
            Stream archive,
            ManifestArtifact.BundleArtifact artifact,
            IReadOnlyDictionary<string, ManifestFileEntry> filesByPath,
            IRuntimeStagingArea staging,
            ISet<string> writtenPaths,
            CancellationToken cancellationToken)
        {
            if (filesByPath.Count != artifact.Entries.Count)
            {
                throw new InvalidDataException("Bundle entries do not match manifest file references.");
            }

            for (int index = 0; index < artifact.Entries.Count; index++)
            {
                BundleEntry entry = artifact.Entries[index];
                ManifestFileEntry file = filesByPath[entry.Path];
                byte[] attributes = CreateExtendedAttributes(entry.Path, file.Size);
                string extendedHeaderName = $"PaxHeaders/{index:D8}";
                await ExpectAsync(
                    archive,
                    CreateHeader(extendedHeaderName, attributes.LongLength, (byte)'x'),
                    cancellationToken).ConfigureAwait(false);
                await ExpectAsync(archive, attributes, cancellationToken).ConfigureAwait(false);
                await ExpectZerosAsync(archive, PaddingSize(attributes.LongLength), cancellationToken).ConfigureAwait(false);

                string entryHeaderName = Encoding.UTF8.GetByteCount(entry.Path) <= 100
                    ? entry.Path
                    : $"PaxEntry/{index:D8}";
                await ExpectAsync(
                    archive,
                    CreateHeader(entryHeaderName, file.Size, (byte)'0'),
                    cancellationToken).ConfigureAwait(false);

                if (!writtenPaths.Add(file.Path))
                {
                    throw new InvalidDataException($"Bundle entry '{file.Path}' was staged more than once.");
                }

                await using Stream destination = await staging
                    .CreateFileAsync(file.Path, cancellationToken)
                    .ConfigureAwait(false);
                using var verifiedDestination = new RuntimeHashingWriteStream(destination, file.Size);
                await CopyExactlyAsync(
                    archive,
                    verifiedDestination,
                    file.Size,
                    cancellationToken).ConfigureAwait(false);
                await verifiedDestination.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (verifiedDestination.BytesWritten != file.Size
                    || verifiedDestination.FinalizeHash() != file.FileHash)
                {
                    throw new InvalidDataException($"Bundle entry '{file.Path}' failed file verification.");
                }

                await ExpectZerosAsync(archive, PaddingSize(file.Size), cancellationToken).ConfigureAwait(false);
            }

            await ExpectAsync(archive, _zeroBlocks, cancellationToken).ConfigureAwait(false);

            var trailing = new byte[1];
            if (await archive.ReadAsync(trailing, 0, 1, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException("Bundle archive has bytes after its two end blocks.");
            }
        }

        private static byte[] CreateExtendedAttributes(string path, long size)
        {
            using var stream = new MemoryStream();
            WritePaxRecord(stream, "path", path);
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
            destination.Write(prefix, 0, prefix.Length);
            destination.Write(body, 0, body.Length);
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

            for (int index = 148; index < 156; index++)
            {
                header[index] = (byte)' ';
            }

            header[156] = typeFlag;
            CopyAscii(header, 257, "ustar\0");
            CopyAscii(header, 263, "00");
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

        private static void WriteUtf8(
            byte[] destination,
            int offset,
            int length,
            string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length == 0 || bytes.Length > length)
            {
                throw new InvalidDataException("Bundle header name does not fit its canonical field.");
            }

            Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
        }

        private static void WriteOctal(
            byte[] destination,
            int offset,
            int length,
            long value)
        {
            string octal = Convert.ToString(value, 8);
            if (octal.Length > length - 1)
            {
                throw new InvalidDataException("Bundle metadata does not fit its canonical octal field.");
            }

            int padding = length - 1 - octal.Length;
            for (int index = 0; index < padding; index++)
            {
                destination[offset + index] = (byte)'0';
            }

            CopyAscii(destination, offset + padding, octal);
            destination[offset + length - 1] = 0;
        }

        private static void WriteChecksum(byte[] destination, int checksum)
        {
            string octal = Convert.ToString(checksum, 8);
            int padding = 6 - octal.Length;
            for (int index = 0; index < padding; index++)
            {
                destination[148 + index] = (byte)'0';
            }

            CopyAscii(destination, 148 + padding, octal);
            destination[154] = 0;
            destination[155] = (byte)' ';
        }

        private static void CopyAscii(byte[] destination, int offset, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
        }

        private static async Task CopyExactlyAsync(
            Stream source,
            Stream destination,
            long length,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[StreamBufferSize];
            long remaining = length;

            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await source
                    .ReadAsync(buffer, 0, requested, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Bundle entry ended before its declared size.");
                }

                await destination
                    .WriteAsync(buffer, 0, read, cancellationToken)
                    .ConfigureAwait(false);
                remaining -= read;
            }
        }

        private static async Task ExpectAsync(
            Stream source,
            byte[] expected,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[Math.Min(StreamBufferSize, expected.Length)];
            int offset = 0;

            while (offset < expected.Length)
            {
                int requested = Math.Min(buffer.Length, expected.Length - offset);
                int read = await source
                    .ReadAsync(buffer, 0, requested, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Bundle archive ended before its canonical structure.");
                }

                for (int index = 0; index < read; index++)
                {
                    if (buffer[index] != expected[offset + index])
                    {
                        throw new InvalidDataException("Bundle archive violates the deterministic PAX byte contract.");
                    }
                }

                offset += read;
            }
        }

        private static Task ExpectZerosAsync(
            Stream source,
            int length,
            CancellationToken cancellationToken)
        {
            return length == 0
                ? Task.CompletedTask
                : ExpectAsync(source, new byte[length], cancellationToken);
        }

        private static int PaddingSize(long size)
        {
            return (int)((TarBlockSize - (size % TarBlockSize)) % TarBlockSize);
        }

        private static long RoundUp(long size)
        {
            return checked(((size + TarBlockSize - 1) / TarBlockSize) * TarBlockSize);
        }
    }
}
