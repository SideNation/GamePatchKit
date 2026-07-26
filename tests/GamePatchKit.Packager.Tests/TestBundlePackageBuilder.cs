using System.Formats.Tar;
using System.Security.Cryptography;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

public class TestBundlePackageBuilder
{
    [Fact]
    public async Task BuildAsync_SameBundleInputTwice_ProducesIdenticalPaxBytes()
    {
        using var firstFixture = new PackageFixture();
        using var secondFixture = new PackageFixture();
        firstFixture.WriteSource("maps/a.bin", "alpha");
        firstFixture.WriteSource("maps/b.bin", "beta");
        secondFixture.WriteSource("maps/a.bin", "alpha");
        secondFixture.WriteSource("maps/b.bin", "beta");
        PackageConfig firstConfig = BundleConfig(firstFixture, CompressionKind.None);
        PackageConfig secondConfig = BundleConfig(secondFixture, CompressionKind.None);

        FilePackageResult first = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(firstConfig, firstFixture.OutputRoot));
        FilePackageResult second = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(secondConfig, secondFixture.OutputRoot));

        ManifestArtifact.BundleArtifact firstBundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(first.Release.Manifest.Artifacts));
        ManifestArtifact.BundleArtifact secondBundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(second.Release.Manifest.Artifacts));
        Assert.Equal("ec4d4fa68091291c8ea847f1c21acbaa59a4f7b61af6d9d39ee12425e0182aa0", firstBundle.ArtifactHash);
        Assert.Equal(firstBundle.ArtifactHash, secondBundle.ArtifactHash);
        Assert.Equal(
            File.ReadAllBytes(firstFixture.OutputPath(firstBundle.Path)),
            File.ReadAllBytes(secondFixture.OutputPath(secondBundle.Path)));
        Assert.Equal(first.Release.GetCanonicalBytes(), second.Release.GetCanonicalBytes());

        await using FileStream archive = File.OpenRead(firstFixture.OutputPath(firstBundle.Path));
        using var reader = new TarReader(archive, leaveOpen: true);
        TarEntry firstEntry = Assert.IsType<PaxTarEntry>(await reader.GetNextEntryAsync());
        TarEntry secondEntry = Assert.IsType<PaxTarEntry>(await reader.GetNextEntryAsync());
        Assert.Equal(new[] { "maps/a.bin", "maps/b.bin" }, new[] { firstEntry.Name, secondEntry.Name });
        Assert.Equal(DateTimeOffset.UnixEpoch, firstEntry.ModificationTime);
        Assert.Equal(0, firstEntry.Uid);
        Assert.Equal(0, firstEntry.Gid);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, firstEntry.Mode);
        Assert.Null(await reader.GetNextEntryAsync());
    }

    [Fact]
    public async Task BuildAsync_BundlePayloadWouldExceedLimit_SplitsAtEntryBoundary()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", new byte[700]);
        fixture.WriteSource("maps/b.bin", new byte[700]);
        PackageConfig config = BundleConfig(fixture, CompressionKind.None, maxArtifactBytes: 4_000);

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        ManifestArtifact.BundleArtifact[] bundles = result.Release.Manifest.Artifacts
            .OfType<ManifestArtifact.BundleArtifact>()
            .ToArray();
        Assert.Equal(2, bundles.Length);
        Assert.All(bundles, bundle => Assert.True(bundle.Size <= config.MaxArtifactBytes));
        Assert.All(bundles, bundle => Assert.Single(bundle.Entries));
    }

    [Fact]
    public async Task BuildAsync_SingleBundleEntryCannotFit_FallsBackToFileParts()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/large.bin", new byte[2_500]);
        PackageConfig config = BundleConfig(fixture, CompressionKind.None, maxArtifactBytes: 1_000);

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        ManifestArtifact.FileArtifact artifact = Assert.IsType<ManifestArtifact.FileArtifact>(
            Assert.Single(result.Release.Manifest.Artifacts));
        var parts = Assert.IsType<FilePayload.Parts>(artifact.Payload);
        Assert.Equal(new long[] { 1_000, 1_000, 500 }, parts.PartList.Select(part => part.Size));
        Assert.IsType<FileSource.FileReference>(Assert.Single(result.Release.Manifest.Files).Source);
    }

    [Fact]
    public async Task BuildAsync_TwoBundleGroups_NeverMixesEntries()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        fixture.WriteSource("audio/a.bin", "audio");
        PackageConfig config = fixture.Config(
            CompressionKind.None,
            groups: new[]
            {
                fixture.Group("audio", "audio/**/*", ArtifactMode.Bundle, required: false),
                fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false),
            });

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        ManifestArtifact.BundleArtifact[] bundles = result.Release.Manifest.Artifacts
            .OfType<ManifestArtifact.BundleArtifact>()
            .ToArray();
        Assert.Equal(2, bundles.Length);
        Assert.All(bundles, bundle => Assert.All(bundle.Entries, entry => Assert.StartsWith(bundle.Group + "/", entry.Path)));
    }

    [Fact]
    public async Task BuildAsync_LongUtf8Path_PreservesFullPaxEntryPath()
    {
        using var fixture = new PackageFixture();
        string relativePath = $"maps/{new string('a', 60)}/{new string('b', 60)}/데이터.bin";
        fixture.WriteSource(relativePath, "map");

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(BundleConfig(fixture, CompressionKind.None), fixture.OutputRoot));

        ManifestArtifact.BundleArtifact bundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(result.Release.Manifest.Artifacts));
        await using FileStream archive = File.OpenRead(fixture.OutputPath(bundle.Path));
        using var reader = new TarReader(archive);
        TarEntry entry = Assert.IsType<PaxTarEntry>(await reader.GetNextEntryAsync());
        Assert.Equal(relativePath, entry.Name);
        Assert.Null(await reader.GetNextEntryAsync());
    }

    [Fact]
    public async Task BuildAsync_CompressibleEntryLargerThanLimitAsTar_CreatesBoundedZstdBundle()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/large.bin", new string('m', 64 * 1024));
        PackageConfig config = BundleConfig(fixture, CompressionKind.Zstd, maxArtifactBytes: 2_048);

        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        ManifestArtifact.BundleArtifact bundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(result.Release.Manifest.Artifacts));
        Assert.Equal(CompressionKind.Zstd, bundle.Compression);
        Assert.True(bundle.Size <= config.MaxArtifactBytes);
        await PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, result.Release.Manifest);
    }

    [Fact]
    public async Task VerifyAsync_CorruptedBundle_RejectsPayload()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        FilePackageResult result = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(BundleConfig(fixture, CompressionKind.None), fixture.OutputRoot));
        ManifestArtifact.BundleArtifact bundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(result.Release.Manifest.Artifacts));
        string bundlePath = fixture.OutputPath(bundle.Path);
        byte[] bytes = File.ReadAllBytes(bundlePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(bundlePath, bytes);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, result.Release.Manifest));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task VerifyAsync_NonCanonicalTarMetadata_RejectsPayload()
    {
        using var fixture = new PackageFixture();
        byte[] content = "map"u8.ToArray();
        string temporaryPath = Path.Combine(fixture.OutputRoot, "non-canonical.tar");

        await using (var archive = File.Create(temporaryPath))
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "maps/a.bin")
            {
                DataStream = new MemoryStream(content, writable: false),
                Gid = 0,
                GroupName = string.Empty,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                ModificationTime = DateTimeOffset.UnixEpoch.AddSeconds(1),
                Uid = 0,
                UserName = string.Empty,
            };
            await writer.WriteEntryAsync(entry);
        }

        byte[] bundleBytes = await File.ReadAllBytesAsync(temporaryPath);
        string bundleHash = Sha256(bundleBytes);
        string bundlePath = ContentAddressedPath.BundleArtifactPath(
            fixture.PackageId,
            "maps",
            bundleHash,
            CompressionKind.None);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.OutputPath(bundlePath))!);
        File.Move(temporaryPath, fixture.OutputPath(bundlePath));
        var artifact = new ManifestArtifact.BundleArtifact(
            "maps",
            bundlePath,
            bundleBytes.Length,
            bundleHash,
            CompressionKind.None,
            new[] { new BundleEntry("maps/a.bin") });
        var file = new ManifestFileEntry(
            "maps/a.bin",
            "maps",
            content.Length,
            Sha256(content),
            new FileSource.BundleEntryReference(bundleHash, "maps/a.bin"));
        var manifest = new ReleaseManifest(
            1,
            fixture.PackageId,
            DataVersionFormat.Prefix + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("maps", required: false) },
            new ManifestArtifact[] { artifact },
            new[] { file });

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, manifest));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task VerifyAsync_ExtraTrailingTarBlock_RejectsPayload()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        FilePackageResult valid = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(BundleConfig(fixture, CompressionKind.None), fixture.OutputRoot));
        ManifestArtifact.BundleArtifact validBundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(valid.Release.Manifest.Artifacts));
        byte[] validBytes = await File.ReadAllBytesAsync(fixture.OutputPath(validBundle.Path));
        byte[] bytesWithExtraBlock = validBytes.Concat(new byte[512]).ToArray();
        ReleaseManifest changedManifest = await StoreRewrittenBundleAsync(fixture, valid.Release, bytesWithExtraBlock);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, changedManifest));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task VerifyAsync_DifferentPaxHeaderName_RejectsPayload()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        FilePackageResult valid = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(BundleConfig(fixture, CompressionKind.None), fixture.OutputRoot));
        ManifestArtifact.BundleArtifact validBundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(valid.Release.Manifest.Artifacts));
        byte[] changedBytes = await File.ReadAllBytesAsync(fixture.OutputPath(validBundle.Path));
        changedBytes[0] = (byte)'X';
        RewriteTarChecksum(changedBytes, headerOffset: 0);
        ReleaseManifest changedManifest = await StoreRewrittenBundleAsync(fixture, valid.Release, changedBytes);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, changedManifest));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ArtifactCorrupted);
    }

    [Fact]
    public async Task VerifyAsync_ZstdExpandsPastDeclaredTarSize_StopsDecoderOutput()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        FilePackageResult valid = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(BundleConfig(fixture, CompressionKind.Zstd), fixture.OutputRoot));
        var codec = new OversizedDecompressionCodec();

        await Assert.ThrowsAsync<PackageException>(
            () => PackagePayloadVerifier.VerifyAsync(
                fixture.OutputRoot,
                valid.Release.Manifest,
                codec));

        Assert.True(codec.WasOutputRejected);
    }

    private static PackageConfig BundleConfig(
        PackageFixture fixture,
        CompressionKind compression,
        long maxArtifactBytes = PackageConfig.DefaultMaxArtifactBytes)
    {
        return fixture.Config(
            compression,
            maxArtifactBytes,
            new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static async Task<ReleaseManifest> StoreRewrittenBundleAsync(
        PackageFixture fixture,
        FinalizedManifest valid,
        byte[] changedBytes)
    {
        ManifestArtifact.BundleArtifact validBundle = Assert.IsType<ManifestArtifact.BundleArtifact>(
            Assert.Single(valid.Manifest.Artifacts));
        string changedHash = Sha256(changedBytes);
        string changedPath = ContentAddressedPath.BundleArtifactPath(
            fixture.PackageId,
            validBundle.Group,
            changedHash,
            CompressionKind.None);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.OutputPath(changedPath))!);
        await File.WriteAllBytesAsync(fixture.OutputPath(changedPath), changedBytes);
        var changedBundle = new ManifestArtifact.BundleArtifact(
            validBundle.Group,
            changedPath,
            changedBytes.Length,
            changedHash,
            CompressionKind.None,
            validBundle.Entries);
        List<ManifestFileEntry> changedFiles = valid.Manifest.Files
            .Select(file => new ManifestFileEntry(
                file.Path,
                file.Group,
                file.Size,
                file.FileHash,
                new FileSource.BundleEntryReference(changedHash, file.Path)))
            .ToList();
        return new ReleaseManifest(
            1,
            fixture.PackageId,
            valid.DataVersion,
            valid.CompactVersion,
            valid.Manifest.Groups,
            new ManifestArtifact[] { changedBundle },
            changedFiles);
    }

    private static void RewriteTarChecksum(byte[] bytes, int headerOffset)
    {
        bytes.AsSpan(headerOffset + 148, 8).Fill((byte)' ');
        int checksum = 0;

        for (int index = headerOffset; index < headerOffset + 512; index++)
        {
            checksum += bytes[index];
        }

        string octal = Convert.ToString(checksum, 8).PadLeft(6, '0');
        System.Text.Encoding.ASCII.GetBytes(octal).CopyTo(bytes, headerOffset + 148);
        bytes[headerOffset + 154] = 0;
        bytes[headerOffset + 155] = (byte)' ';
    }

    private sealed class OversizedDecompressionCodec : ICompressionCodec
    {
        public string CodecId => CompressionCodecIds.Zstd;

        public bool WasOutputRejected { get; private set; }

        public Task CompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            try
            {
                await destination.WriteAsync(new byte[1024 * 1024], cancellationToken);
            }
            catch (InvalidDataException)
            {
                WasOutputRejected = true;
                throw;
            }
        }
    }
}