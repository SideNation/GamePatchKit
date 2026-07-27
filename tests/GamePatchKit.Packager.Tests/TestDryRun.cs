using System.Security.Cryptography;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

public class TestDryRun
{
    [Fact]
    public async Task BuildAsync_DryRun_LeavesTheOutputTreeUntouched()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        IReadOnlyDictionary<string, string> before = OutputTree(fixture);

        await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, dryRun: true));

        Assert.Equal(before, OutputTree(fixture));
    }

    [Fact]
    public async Task BuildAsync_DryRun_ReportsTheSameIdentityAndCountsAsTheRealRun()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        fixture.WriteSource("data/level.bin", "level bytes");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder(zstdCodec: null);

        FilePackageResult dryRun = await builder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, dryRun: true));
        FilePackageResult real = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));

        // Artifacts have to be produced to know their hashes, so a dry run knows the real answer - it just
        // does not publish it.
        Assert.Equal(real.Release.DataVersion, dryRun.Release.DataVersion);
        Assert.Equal(real.Release.CompactVersion, dryRun.Release.CompactVersion);
        Assert.Equal(real.Release.ManifestHash, dryRun.Release.ManifestHash);
        Assert.Equal(real.Report.CreatedFileArtifactCount, dryRun.Report.CreatedFileArtifactCount);
        Assert.Equal(real.Report.AddedFiles, dryRun.Report.AddedFiles);
    }

    [Fact]
    public async Task BuildAsync_DryRunOnTopOfAPublishedRelease_DoesNotDisturbIt()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        PackageConfig config = fixture.Config(compression: CompressionKind.None);
        var builder = new FilePackageBuilder(zstdCodec: null);
        FilePackageResult published = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        IReadOnlyDictionary<string, string> before = OutputTree(fixture);
        fixture.WriteSource("data/config.json", "changed configuration");

        FilePackageResult dryRun = await builder.BuildAsync(new FilePackageRequest(
            config,
            fixture.OutputRoot,
            fixture.Previous(published),
            dryRun: true));

        Assert.NotEqual(published.Release.ManifestHash, dryRun.Release.ManifestHash);
        Assert.Equal(before, OutputTree(fixture));
    }

    [Fact]
    public async Task CompactAsync_DryRun_DecidesTheCompactWithoutPublishingIt()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.bin", "core");
        fixture.WriteSource("maps/a.bin", "alpha");
        fixture.WriteSource("maps/b.bin", "beta");
        PackageConfig config = fixture.Config(
            CompressionKind.None,
            groups: new[]
            {
                fixture.Group("core", "core/**/*", ArtifactMode.File, required: true),
                fixture.Group("maps", "maps/**/*", ArtifactMode.Bundle, required: false),
            });
        var builder = new FilePackageBuilder(zstdCodec: null);
        FilePackageResult baseline = await builder.BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
        fixture.WriteSource("maps/b.bin", "changed");
        FilePackageResult incremental = await builder.BuildAsync(
            new FilePackageRequest(config, fixture.OutputRoot, fixture.Previous(baseline)));
        IReadOnlyDictionary<string, string> before = OutputTree(fixture);

        BundleCompactResult result = await new BundleCompactor(zstdCodec: null).CompactAsync(new BundleCompactRequest(
            config,
            fixture.OutputRoot,
            fixture.Previous(incremental),
            new[] { "maps" },
            Array.Empty<ArtifactPayloadObject>(),
            dryRun: true));

        Assert.True(result.Changed);
        Assert.Equal(incremental.Release.CompactVersion + 1, result.Release.CompactVersion);
        Assert.Equal(before, OutputTree(fixture));
    }

    // Relative path to content hash for every file under the output root, so a comparison catches a new file,
    // a deleted one and a rewritten one alike.
    private static IReadOnlyDictionary<string, string> OutputTree(PackageFixture fixture)
    {
        if (!Directory.Exists(fixture.OutputRoot))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return Directory.EnumerateFiles(fixture.OutputRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(fixture.OutputRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
    }
}
