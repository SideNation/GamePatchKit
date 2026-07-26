using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

// A stored object's length and SHA-256 are pinned by the manifest, so the only thing a payload can still do
// without limit is expand. These check that the declared size is enforced while decoding rather than compared
// once the expansion has already happened - the difference between rejecting a bomb and running it first.
public class TestDecodeBounds
{
    [Fact]
    public async Task VerifyAsync_ArtifactThatDecodesPastItsDeclaredSize_IsRejectedWithoutDecodingItAll()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/small.bin", "tiny");
        var identity = new IdentityZstdCodec();
        FilePackageResult published = await new FilePackageBuilder(identity).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.Zstd), fixture.OutputRoot));
        long declaredSize = published.Release.Manifest.Files.Single().Size;

        // Same stored bytes and the same object hash - only the decoder misbehaves, which is exactly the
        // shape of a compressed object that expands far past what the manifest says it holds.
        var bomb = new ExpandingZstdCodec(totalBytes: 64 * 1024 * 1024);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, published.Release.Manifest, bomb));

        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, exception.Errors[0].Code);

        // Stopped at the declared size, not after producing everything it wanted to.
        Assert.True(
            bomb.BytesProduced <= declaredSize + ExpandingZstdCodec.ChunkSize,
            $"decoder produced {bomb.BytesProduced} bytes for a {declaredSize} byte file");
    }

    [Fact]
    public async Task VerifyAsync_ArtifactThatDecodesToLessThanItsDeclaredSize_IsStillRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/small.bin", "tiny");
        FilePackageResult published = await new FilePackageBuilder(new IdentityZstdCodec()).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.Zstd), fixture.OutputRoot));

        // The bound must not become the only check: a short decode still has to fail on size and hash.
        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(
                fixture.OutputRoot,
                published.Release.Manifest,
                new ExpandingZstdCodec(totalBytes: 1)));

        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_HonestCodec_StillVerifies()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/small.bin", "tiny");
        FilePackageResult published = await new FilePackageBuilder(new IdentityZstdCodec()).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.Zstd), fixture.OutputRoot));

        // The bound is exactly the declared size, so a file that decodes to precisely that must still pass.
        await PackagePayloadVerifier.VerifyAsync(
            fixture.OutputRoot,
            published.Release.Manifest,
            new IdentityZstdCodec());
    }

    [Fact]
    public async Task ReadPublishedBytesAsync_OversizedManifest_IsRejectedWithoutAllocatingIt()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/small.bin", "tiny");
        FilePackageResult published = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.None), fixture.OutputRoot));
        string manifestPath = fixture.OutputPath(
            PackageLayout.ManifestPath(fixture.PackageId, published.Release.ManifestHash));

        // Sparse: the point is the declared length, and writing 64 MiB of real bytes would only slow the
        // test down to prove the same thing.
        await using (var file = new FileStream(manifestPath, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(PackageLayout.MaximumManifestBytes + 1);
        }

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => ReleaseManifestReader.ReadPublishedBytesAsync(
                fixture.OutputRoot,
                fixture.PackageId,
                published.Release.ManifestHash));

        Assert.Equal(PackageErrorCodes.InvalidManifestDocument, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_OversizedSignature_IsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/small.bin", "tiny");
        FilePackageResult published = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.None), fixture.OutputRoot));
        string signaturePath = fixture.OutputPath(
            PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash));
        await File.WriteAllBytesAsync(
            signaturePath,
            Enumerable.Repeat((byte)' ', (int)PackageLayout.MaximumSignatureBytes + 1).ToArray());

        // A canonical manifest.sig is a fixed 225 bytes, so nothing near this limit can be one.
        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new ReleaseVerifier(zstdCodec: null).VerifyAsync(new ReleaseVerifyRequest(
                fixture.OutputRoot,
                fixture.PackageId,
                published.Release.ManifestHash,
                published.Release.GetCanonicalBytes())));

        Assert.Equal(PackageErrorCodes.InvalidSignature, exception.Errors[0].Code);

        // Rejected on length, before being parsed. Without the bound the whole file is read first and the
        // schema turns it down instead, which reports the same code - so the message is what distinguishes
        // "never read it" from "read it, then disliked it".
        Assert.Contains("limit", exception.Errors[0].Message, StringComparison.Ordinal);
    }

    // Passes bytes straight through under the zstd codec id, so a package can be built and verified without
    // a real codec while still travelling the compressed code path.
    private sealed class IdentityZstdCodec : ICompressionCodec
    {
        public string CodecId => CompressionCodecIds.Zstd;

        public Task CompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            return source.CopyToAsync(destination, cancellationToken);
        }

        public Task DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            return source.CopyToAsync(destination, cancellationToken);
        }
    }

    // Ignores its input and produces a fixed number of bytes, counting how many it managed to write.
    private sealed class ExpandingZstdCodec : ICompressionCodec
    {
        public const int ChunkSize = 64 * 1024;

        private readonly long _totalBytes;

        public long BytesProduced { get; private set; }

        public ExpandingZstdCodec(long totalBytes)
        {
            _totalBytes = totalBytes;
        }

        public string CodecId => CompressionCodecIds.Zstd;

        public Task CompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            return source.CopyToAsync(destination, cancellationToken);
        }

        public async Task DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            var chunk = new byte[ChunkSize];

            while (BytesProduced < _totalBytes)
            {
                int count = (int)Math.Min(ChunkSize, _totalBytes - BytesProduced);
                await destination.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                BytesProduced += count;
            }
        }
    }
}
