using GamePatchKit.Conformance;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// The full path the PRD describes: package with the real Packager, publish to a real tree, download through
// HttpArtifactTransport, activate through FileSystemRuntimeStorage, and read the result back through
// PackageRuntime's own OpenInstallationFileAsync contract rather than poking at the filesystem directly -
// with zstd compression actually turned on, which none of the other scenarios in this project exercise.
public class TestDotNetAdapterPackagerEndToEnd
{
    [Fact]
    public async Task PackageDownloadActivate_WithZstdCompression_InstalledBytesMatchSourceExactly()
    {
        using var fixture = new PublishedPackageFixture();
        byte[] coreBytes = new byte[64 * 1024];
        new Random(Seed: 1).NextBytes(coreBytes);
        byte[] mapsBytes = new byte[32 * 1024];
        new Random(Seed: 2).NextBytes(mapsBytes);
        fixture.WriteSource("core/data.bin", coreBytes);
        fixture.WriteSource("core/nested/extra.bin", "extra-core-file");
        fixture.WriteSource("maps/level1.bin", mapsBytes);

        FinalizedManifest release = await fixture.PublishAsync(
            new[] { fixture.Group("core", required: true), fixture.Group("maps", required: false) },
            CompressionKind.Zstd);
        using var harness = new AdapterHarness(fixture.PublishRoot);

        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        state = await harness.Runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" });

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "maps").Status);

        await AssertInstalledFileMatchesAsync(harness, state, "core", "core/data.bin", coreBytes);
        await AssertInstalledFileMatchesAsync(harness, state, "core", "core/nested/extra.bin", "extra-core-file"u8.ToArray());
        await AssertInstalledFileMatchesAsync(harness, state, "maps", "maps/level1.bin", mapsBytes);
    }

    private static async Task AssertInstalledFileMatchesAsync(
        AdapterHarness harness,
        PackageState state,
        string group,
        string relativePath,
        byte[] expectedBytes)
    {
        string installationKey = state.Groups.Single(g => g.Name == group).InstallationKey!;
        await using Stream? installed = await harness.Storage.OpenInstallationFileAsync(
            installationKey, relativePath, CancellationToken.None);
        Assert.NotNull(installed);
        using var buffer = new MemoryStream();
        await installed!.CopyToAsync(buffer);
        Assert.Equal(expectedBytes, buffer.ToArray());
    }
}
