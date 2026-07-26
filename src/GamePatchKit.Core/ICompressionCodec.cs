using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Core
{
    // Streaming compression contract implemented by adapters (default:
    // GamePatchKit.Compression.NativeCompressions). Core defines the identifier and the shape only and never
    // references a concrete codec, which is what keeps Packager and Runtime free of a compression library
    // dependency and lets a host inject its own implementation.
    public interface ICompressionCodec
    {
        // Matches the manifest's compression.codecId - see CompressionCodecIds.
        string CodecId { get; }

        Task CompressAsync(Stream source, Stream destination, CancellationToken cancellationToken);

        Task DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken);
    }
}
