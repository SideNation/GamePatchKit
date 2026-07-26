using System;
using System.Buffers;
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
        private const string InvalidFrameMessage = "The zstd frame is invalid.";
        private const string StalledDecoderMessage = "The zstd decoder made no progress.";
        private const string TruncatedFrameMessage = "The zstd frame ended before completion.";

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
            byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
            byte[] destinationBuffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);

            try
            {
                using var decoder = new ZstandardDecoder(_decompressionOptions);
                int sourceOffset = 0;
                int sourceCount = 0;
                OperationStatus status = OperationStatus.NeedMoreData;

                while (status != OperationStatus.Done)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (sourceOffset == sourceCount && status != OperationStatus.DestinationTooSmall)
                    {
                        sourceCount = await source.ReadAsync(
                            sourceBuffer.AsMemory(0, StreamBufferSize),
                            cancellationToken).ConfigureAwait(false);
                        sourceOffset = 0;

                        if (sourceCount == 0)
                        {
                            throw new InvalidDataException(TruncatedFrameMessage);
                        }
                    }

                    status = decoder.Decompress(
                        sourceBuffer.AsSpan(sourceOffset, sourceCount - sourceOffset),
                        destinationBuffer.AsSpan(0, StreamBufferSize),
                        out int bytesConsumed,
                        out int bytesWritten);

                    if (status == OperationStatus.InvalidData)
                    {
                        throw new InvalidDataException(InvalidFrameMessage);
                    }

                    sourceOffset += bytesConsumed;

                    if (bytesWritten > 0)
                    {
                        await destination.WriteAsync(
                            destinationBuffer.AsMemory(0, bytesWritten),
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (status != OperationStatus.Done && bytesConsumed == 0 && bytesWritten == 0 &&
                        !(status == OperationStatus.NeedMoreData && sourceOffset == sourceCount))
                    {
                        throw new InvalidDataException(StalledDecoderMessage);
                    }
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sourceBuffer, clearArray: false);
                ArrayPool<byte>.Shared.Return(destinationBuffer, clearArray: false);
            }
        }
    }
}
