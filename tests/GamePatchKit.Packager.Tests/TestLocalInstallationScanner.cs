using System.Security.Cryptography;
using System.Text;
using GamePatchKit.Core;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager.Tests;

public class TestLocalInstallationScanner
{
    [Fact]
    public async Task ScanInstalledFilesAsync_ReportsCanonicalPathsAndRealFileHashes()
    {
        using var fixture = new PackageFixture();
        string installRoot = Path.Combine(fixture.OutputRoot, "install");
        Write(installRoot, "data/config.json", "configuration");
        Write(installRoot, "data/nested/level.bin", "level bytes");

        IReadOnlyList<LocalFileState> states = await LocalInstallationScanner.ScanInstalledFilesAsync(installRoot);

        Assert.Equal(
            new[] { "data/config.json", "data/nested/level.bin" },
            states.Select(state => state.Path).OrderBy(path => path, StringComparer.Ordinal));
        Assert.Equal(
            Sha256Hex("configuration"),
            states.Single(state => state.Path == "data/config.json").FileHash);
    }

    [Fact]
    public async Task ScanInstalledFilesAsync_MissingRoot_IsAnEmptyInstallation()
    {
        using var fixture = new PackageFixture();

        IReadOnlyList<LocalFileState> states = await LocalInstallationScanner.ScanInstalledFilesAsync(
            Path.Combine(fixture.OutputRoot, "not-installed-yet"));

        Assert.Empty(states);
    }

    [Fact]
    public async Task ScanCachedObjectsAsync_ReportsObjectPathsThatMatchThePublishedLayout()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        FilePackageResult published = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.None), fixture.OutputRoot));
        ArtifactPayloadObject payload = published.Release.Manifest.EnumeratePayloadObjects().Single();

        IReadOnlyList<CachedArtifactObject> cached = await LocalInstallationScanner.ScanCachedObjectsAsync(fixture.OutputRoot);

        // A published output tree is itself a valid object cache, so the scanner has to produce exactly the
        // paths and hashes the manifest declares - otherwise planning would re-download everything.
        CachedArtifactObject scanned = cached.Single(entry => entry.Path == payload.Path);
        Assert.Equal(payload.ObjectHash, scanned.ObjectHash);
    }

    [Fact]
    public async Task ScanInstalledFilesAsync_SymlinkInTheTree_IsRejected()
    {
        using var fixture = new PackageFixture();
        string installRoot = Path.Combine(fixture.OutputRoot, "install");
        Write(installRoot, "data/config.json", "configuration");
        File.CreateSymbolicLink(
            Path.Combine(installRoot, "data", "alias.json"),
            Path.Combine(installRoot, "data", "config.json"));

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => LocalInstallationScanner.ScanInstalledFilesAsync(installRoot));

        Assert.Equal(PackageErrorCodes.UnsupportedEntry, exception.Errors[0].Code);
    }

    [Fact]
    public async Task Collect_RetainedRelease_ReportsEveryObjectPathItStillClaims()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        FilePackageResult retained = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.None), fixture.OutputRoot));

        IReadOnlyList<ArtifactPayloadObject> objects = RetainedReleaseInventory.Collect(
            new[] { fixture.Previous(retained) },
            fixture.PackageId);

        Assert.Equal(
            retained.Release.Manifest.EnumeratePayloadObjects().Select(payload => payload.Path),
            objects.Select(payload => payload.Path));
    }

    [Fact]
    public async Task Collect_ManifestWhoseHashDoesNotMatch_IsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("data/config.json", "configuration");
        FilePackageResult retained = await new FilePackageBuilder(zstdCodec: null).BuildAsync(
            new FilePackageRequest(fixture.Config(CompressionKind.None), fixture.OutputRoot));

        PackageException exception = Assert.Throws<PackageException>(() => RetainedReleaseInventory.Collect(
            new[] { new PreviousRelease(retained.Release.GetCanonicalBytes(), new string('b', 64)) },
            fixture.PackageId));

        Assert.Equal(ManifestErrorCodes.ManifestHashMismatch, exception.Errors[0].Code);
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Sha256Hex(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
