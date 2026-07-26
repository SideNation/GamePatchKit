using System.Runtime.InteropServices;
using System.Text;
using GamePatchKit.Core;

namespace GamePatchKit.Compression.NativeCompressions.Tests;

public class TestZstdPlatformSmoke
{
    [Fact]
    [Trait("Category", "PlatformSmoke")]
    public async Task SupportedDesktopRuntimeLoadsNativeLibraryAndRoundTrips()
    {
        Assert.True(IsSupportedDesktopRuntime(), $"Unsupported smoke-test runtime: {RuntimeInformation.RuntimeIdentifier}");

        byte[] sourceBytes = Encoding.UTF8.GetBytes("GamePatchKit zstd platform smoke test");
        ICompressionCodec codec = ZstdCompressionCodecFactory.Create();
        await using var source = new MemoryStream(sourceBytes);
        await using var compressed = new MemoryStream();

        await codec.CompressAsync(source, compressed, CancellationToken.None);

        compressed.Position = 0;
        await using var restored = new MemoryStream();
        await codec.DecompressAsync(compressed, restored, CancellationToken.None);

        Assert.Equal(sourceBytes, restored.ToArray());
    }

    private static bool IsSupportedDesktopRuntime()
    {
        bool isSupportedOperatingSystem =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        bool isSupportedArchitecture =
            RuntimeInformation.OSArchitecture == Architecture.X64 ||
            RuntimeInformation.OSArchitecture == Architecture.Arm64;

        return isSupportedOperatingSystem && isSupportedArchitecture;
    }
}
