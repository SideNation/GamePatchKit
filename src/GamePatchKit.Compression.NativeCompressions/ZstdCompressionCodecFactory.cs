using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Core;
using NativeCompressions;

namespace GamePatchKit.Compression.NativeCompressions
{
    public static class ZstdCompressionCodecFactory
    {
        public static ICompressionCodec Create()
        {
            return new ZstdCompressionCodec();
        }
    }

    internal sealed class ZstdCompressionCodec : ICompressionCodec
    {
        private const int CompressionLevel = 3;
        private const int CompressionWorkerCount = 0;
        private const int StreamBufferSize = 65_536;

        private static readonly ZstandardCompressionOptions _compressionOptions = new ZstandardCompressionOptions
        {
            CompressionLevel = CompressionLevel,
            ContentSizeFlag = false,
            ChecksumFlag = true,
            DictIDFlag = false,
            NbWorkers = CompressionWorkerCount,
        };

        private static readonly ZstandardDecompressionOptions _decompressionOptions = ZstandardDecompressionOptions.Default;

        public string CodecId => CompressionCodecIds.Zstd;

        public async Task CompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            await using (var compressionStream = new ZstandardStream(destination, _compressionOptions, leaveOpen: true))
            {
                await compressionStream.WriteAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                await source.CopyToAsync(compressionStream, StreamBufferSize, cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            await using (var decompressionStream = new ZstandardStream(source, _decompressionOptions, leaveOpen: true))
            {
                await decompressionStream.CopyToAsync(destination, StreamBufferSize, cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
