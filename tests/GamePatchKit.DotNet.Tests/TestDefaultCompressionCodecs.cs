using System.Text;
using GamePatchKit.Core;

namespace GamePatchKit.DotNet.Tests;

public class TestDefaultCompressionCodecs
{
    [Fact]
    public async Task Create_ReturnsAWorkingZstdCodec()
    {
        IReadOnlyList<ICompressionCodec> codecs = DefaultCompressionCodecs.Create();
        ICompressionCodec codec = Assert.Single(codecs);
        Assert.Equal(CompressionCodecIds.Zstd, codec.CodecId);

        byte[] original = Encoding.UTF8.GetBytes("round-trips through the default codec");
        using var source = new MemoryStream(original);
        using var compressed = new MemoryStream();
        await codec.CompressAsync(source, compressed, CancellationToken.None);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await codec.DecompressAsync(compressed, decompressed, CancellationToken.None);

        Assert.Equal(original, decompressed.ToArray());
    }
}
