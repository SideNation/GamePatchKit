using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

public class TestBundleCompactor
{
    private static readonly ArtifactPayloadObject[] _noOtherRetainedObjects = Array.Empty<ArtifactPayloadObject>();

    [Fact]
    public async Task CompactAsync_IdenticalPhysicalLayout_ReturnsNoOpWithoutPublishing()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        PackageConfig config = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult source = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));
        string[] manifestsBefore = ManifestDirectories(fixture);
        string[] bundlesBefore = BundleFiles(fixture);

        BundleCompactResult result = await new BundleCompactor().CompactAsync(
            new BundleCompactRequest(
                config,
                fixture.OutputRoot,
                fixture.Previous(source),
                new[] { "maps" },
                _noOtherRetainedObjects));

        Assert.False(result.Changed);
        Assert.Equal(source.Release.DataVersion, result.Release.DataVersion);
        Assert.Equal(source.Release.CompactVersion, result.Release.CompactVersion);
        Assert.Equal(source.Release.ManifestHash, result.Release.ManifestHash);
        Assert.Equal(manifestsBefore, ManifestDirectories(fixture));
        Assert.Equal(bundlesBefore, BundleFiles(fixture));
    }

    [Fact]
    public async Task CompactAsync_FileOverridesInSelectedGroup_CreatesNewBundleAndReusesFileGroup()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.bin", "core");
        fixture.WriteSource("maps/a.bin", "alpha");
        fixture.WriteSource("maps/b.bin", "beta");
        PackageConfig config = MixedConfig(fixture, CompressionKind.None);
        var packageBuilder = new FilePackageBuilder();
        FilePackageResult baseline = await packageBuilder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));
        ManifestArtifact.FileArtifact baselineCoreArtifact = FileArtifactForPath(baseline.Release.Manifest, "core/config.bin");
        fixture.WriteSource("maps/b.bin", "changed");
        FilePackageResult incremental = await packageBuilder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(baseline)));
        Assert.All(
            incremental.Release.Manifest.Files.Where(file => file.Group == "maps"),
            file => Assert.IsType<FileSource.FileReference>(file.Source));

        BundleCompactResult result = await new BundleCompactor().CompactAsync(
            new BundleCompactRequest(
                config,
                fixture.OutputRoot,
                fixture.Previous(incremental),
                new[] { "maps" },
                _noOtherRetainedObjects));

        Assert.True(result.Changed);
        Assert.Equal(incremental.Release.DataVersion, result.Release.DataVersion);
        Assert.Equal(incremental.Release.CompactVersion + 1, result.Release.CompactVersion);
        Assert.NotEqual(incremental.Release.ManifestHash, result.Release.ManifestHash);
        Assert.All(
            result.Release.Manifest.Files.Where(file => file.Group == "maps"),
            file => Assert.IsType<FileSource.BundleEntryReference>(file.Source));
        ManifestArtifact.FileArtifact compactedCoreArtifact = FileArtifactForPath(result.Release.Manifest, "core/config.bin");
        Assert.Equal(baselineCoreArtifact.ToJson(), compactedCoreArtifact.ToJson());
        Assert.Equal(1, result.ReusedFileArtifactCount);
        await PackagePayloadVerifier.VerifyAsync(fixture.OutputRoot, result.Release.Manifest);
    }

    [Fact]
    public async Task CompactAsync_CompressionChanges_RebuildsOnlySelectedBundleGroup()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("audio/a.bin", new string('a', 8_192));
        fixture.WriteSource("maps/a.bin", new string('m', 8_192));
        PackageConfig baselineConfig = TwoBundleGroupsConfig(fixture, mapsCompression: CompressionKind.None);
        FilePackageResult baseline = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(baselineConfig, fixture.OutputRoot));
        ManifestArtifact.BundleArtifact baselineAudio = BundleForGroup(baseline.Release.Manifest, "audio");
        PackageConfig compactConfig = TwoBundleGroupsConfig(fixture, mapsCompression: CompressionKind.Zstd);

        BundleCompactResult result = await new BundleCompactor().CompactAsync(
            new BundleCompactRequest(
                compactConfig,
                fixture.OutputRoot,
                fixture.Previous(baseline),
                new[] { "maps" },
                _noOtherRetainedObjects));

        Assert.True(result.Changed);
        Assert.Equal(CompressionKind.Zstd, BundleForGroup(result.Release.Manifest, "maps").Compression);
        Assert.Equal(baselineAudio.ToJson(), BundleForGroup(result.Release.Manifest, "audio").ToJson());
    }

    [Fact]
    public async Task CompactAsync_CorruptedSourceBundle_RejectsWithoutPublishingManifest()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        PackageConfig config = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult source = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));
        ManifestArtifact.BundleArtifact bundle = BundleForGroup(source.Release.Manifest, "maps");
        File.WriteAllBytes(fixture.OutputPath(bundle.Path), new byte[bundle.Size]);
        string[] manifestsBefore = ManifestDirectories(fixture);

        await Assert.ThrowsAsync<PackageException>(
            () => new BundleCompactor().CompactAsync(
                new BundleCompactRequest(
                    config,
                    fixture.OutputRoot,
                    fixture.Previous(source),
                    new[] { "maps" },
                    _noOtherRetainedObjects)));

        Assert.Equal(manifestsBefore, ManifestDirectories(fixture));
    }

    [Fact]
    public async Task CompactAsync_CandidateCollidesWithRetainedObject_RejectsPublish()
    {
        using var fixture = new PackageFixture();
        byte[] content = Enumerable.Range(0, 2_500).Select(value => (byte)value).ToArray();
        fixture.WriteSource("maps/large.bin", content);
        PackageConfig baselineConfig = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult source = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(baselineConfig, fixture.OutputRoot));
        ManifestFileEntry sourceFile = Assert.Single(source.Release.Manifest.Files);
        PackageConfig compactConfig = fixture.Config(
            CompressionKind.None,
            maxArtifactBytes: 1_000,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });
        string retainedPath = ContentAddressedPath.FilePartPath(fixture.PackageId, sourceFile.FileHash, partIndex: 0);
        var retainedObjects = new[]
        {
            new ArtifactPayloadObject(retainedPath, 1_000, new string('f', 64)),
        };
        string[] manifestsBefore = ManifestDirectories(fixture);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new BundleCompactor().CompactAsync(
                new BundleCompactRequest(
                    compactConfig,
                    fixture.OutputRoot,
                    fixture.Previous(source),
                    new[] { "maps" },
                    retainedObjects)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.ManifestInvalid);
        Assert.Equal(manifestsBefore, ManifestDirectories(fixture));
    }

    [Fact]
    public async Task CompactAsync_DuplicateTargetGroup_RejectsRequest()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        PackageConfig config = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult source = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new BundleCompactor().CompactAsync(
                new BundleCompactRequest(
                    config,
                    fixture.OutputRoot,
                    fixture.Previous(source),
                    new[] { "maps", "maps" },
                    _noOtherRetainedObjects)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.InvalidConfiguration);
    }

    [Fact]
    public async Task CompactAsync_CompressedManifestWithoutCodec_RejectsBeforeWriting()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", "map");
        PackageConfig config = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult source = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot));
        string[] manifestsBefore = ManifestDirectories(fixture);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new BundleCompactor(zstdCodec: null).CompactAsync(
                new BundleCompactRequest(
                    config,
                    fixture.OutputRoot,
                    fixture.Previous(source),
                    new[] { "maps" },
                    _noOtherRetainedObjects,
                    writeCompressedManifest: true)));

        Assert.Contains(exception.Errors, error => error.Code == PackageErrorCodes.MissingCompressionCodec);
        Assert.Equal(manifestsBefore, ManifestDirectories(fixture));
    }

    [Fact]
    public async Task CompactAsync_CompactVersionWouldExceedSafeInteger_RejectsPublish()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("maps/a.bin", new string('m', 8_192));
        PackageConfig baselineConfig = BundleConfig(fixture, CompressionKind.None);
        FilePackageResult baseline = await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(baselineConfig, fixture.OutputRoot));
        var maxVersionDraft = new ReleaseManifest(
            baseline.Release.Manifest.SchemaVersion,
            baseline.Release.Manifest.PackageId,
            baseline.Release.DataVersion,
            JsonNumbers.MaxSafeInteger,
            baseline.Release.Manifest.Groups,
            baseline.Release.Manifest.Artifacts,
            baseline.Release.Manifest.Files);
        FinalizedManifest maxVersionSource = ReleaseIdentity.Finalize(maxVersionDraft, JsonNumbers.MaxSafeInteger);
        PackageConfig compactConfig = BundleConfig(fixture, CompressionKind.Zstd);
        string[] manifestsBefore = ManifestDirectories(fixture);

        await Assert.ThrowsAsync<PackageException>(
            () => new BundleCompactor().CompactAsync(
                new BundleCompactRequest(
                    compactConfig,
                    fixture.OutputRoot,
                    fixture.Previous(maxVersionSource),
                    new[] { "maps" },
                    _noOtherRetainedObjects)));

        Assert.Equal(manifestsBefore, ManifestDirectories(fixture));
    }

    private static PackageConfig BundleConfig(PackageFixture fixture, CompressionKind compression)
    {
        return fixture.Config(
            compression,
            groups: new[] { fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false) });
    }

    private static PackageConfig MixedConfig(PackageFixture fixture, CompressionKind mapsCompression)
    {
        return fixture.Config(
            CompressionKind.None,
            groups: new[]
            {
                fixture.Group("core", "core/**/*", ArtifactMode.File, required: true),
                fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false, mapsCompression),
            });
    }

    private static PackageConfig TwoBundleGroupsConfig(PackageFixture fixture, CompressionKind mapsCompression)
    {
        return fixture.Config(
            CompressionKind.None,
            groups: new[]
            {
                fixture.Group("audio", "audio/**/*", ArtifactMode.Bundle, required: false, CompressionKind.None),
                fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false, mapsCompression),
            });
    }

    private static ManifestArtifact.FileArtifact FileArtifactForPath(ReleaseManifest manifest, string path)
    {
        ManifestFileEntry file = Assert.Single(manifest.Files, item => item.Path == path);
        var reference = Assert.IsType<FileSource.FileReference>(file.Source);
        return Assert.Single(
            manifest.Artifacts.OfType<ManifestArtifact.FileArtifact>(),
            artifact => artifact.PrimaryArtifactHash == reference.ArtifactHash);
    }

    private static ManifestArtifact.BundleArtifact BundleForGroup(ReleaseManifest manifest, string group)
    {
        return Assert.Single(manifest.Artifacts.OfType<ManifestArtifact.BundleArtifact>(), bundle => bundle.Group == group);
    }

    private static string[] ManifestDirectories(PackageFixture fixture)
    {
        string root = Path.Combine(fixture.OutputRoot, fixture.PackageId, "manifests");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
    }

    private static string[] BundleFiles(PackageFixture fixture)
    {
        string root = Path.Combine(fixture.OutputRoot, fixture.PackageId, "artifacts", "bundles");
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
    }
}