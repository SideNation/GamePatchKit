using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// Full-stack scenarios over a real filesystem and a real (fake-transport) HTTP publish tree: required-only
// install, optional follow-up, reconnect-without-download, stale transition, group-order independence, and
// no partial activation when one group in a batch fails.
public class TestDotNetAdapterLifecycle
{
    [Fact]
    public async Task InstallOrUpdateAsync_InitialInstall_OnlyDownloadsRequiredGroups()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[] { fixture.Group("core", required: true), fixture.Group("maps", required: false) });
        using var harness = new AdapterHarness(fixture.PublishRoot);

        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(0, harness.Server.GetOpenCount(ManifestPayloadLookup.PayloadPathFor(release, "maps/level1.bin")));
    }

    [Fact]
    public async Task InstallOptionalGroupsAsync_FollowUpInstall_MakesTheGroupReady()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[] { fixture.Group("core", required: true), fixture.Group("maps", required: false) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        PackageState initial = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        PackageState state = await harness.Runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" });

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(initial.StateRevision + 1, state.StateRevision);

        await using Stream? installed = await harness.Storage.OpenInstallationFileAsync(
            state.Groups.Single(group => group.Name == "maps").InstallationKey!,
            "maps/level1.bin",
            CancellationToken.None);
        using var reader = new StreamReader(installed!);
        Assert.Equal("maps-v1", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task InstallOrUpdateAsync_UnchangedOptionalGroup_ReconnectsWithoutRedownloading()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        PackageConfigGroup[] groups =
        {
            fixture.Group("core", required: true), fixture.Group("maps", required: false),
        };
        FinalizedManifest release1 = await fixture.PublishAsync(groups);
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release1));
        await harness.Runtime.InstallOptionalGroupsAsync(release1.Manifest.PackageId, new[] { "maps" });
        string mapsPath = ManifestPayloadLookup.PayloadPathFor(release1, "maps/level1.bin");
        Assert.Equal(1, harness.Server.GetOpenCount(mapsPath));

        // core changes, maps does not - a fresh (non-incremental) build still gives the unchanged file the
        // same content-addressed path, which is what lets Runtime recognize it without a new download.
        fixture.WriteSource("core/data.bin", "core-v2");
        FinalizedManifest release2 = await fixture.PublishAsync(groups);
        Assert.NotEqual(release1.ManifestHash, release2.ManifestHash);

        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release2));

        PackageGroupState maps = state.Groups.Single(group => group.Name == "maps");
        Assert.Equal(PackageGroupStatus.Ready, maps.Status);
        Assert.Equal(release2.ManifestHash, maps.VerifiedManifestHash);
        Assert.Equal(1, harness.Server.GetOpenCount(mapsPath));
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.True(harness.Server.GetOpenCount(ManifestPayloadLookup.PayloadPathFor(release2, "core/data.bin")) >= 1);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_ChangedOptionalGroupNotRequested_BecomesStaleRatherThanNotInstalled()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        PackageConfigGroup[] groups =
        {
            fixture.Group("core", required: true), fixture.Group("maps", required: false),
        };
        FinalizedManifest release1 = await fixture.PublishAsync(groups);
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release1));
        PackageState afterOptionalInstall = await harness.Runtime.InstallOptionalGroupsAsync(
            release1.Manifest.PackageId, new[] { "maps" });
        string previousInstallationKey = afterOptionalInstall.Groups.Single(group => group.Name == "maps").InstallationKey!;

        fixture.WriteSource("core/data.bin", "core-v2");
        fixture.WriteSource("maps/level1.bin", "maps-v2");
        FinalizedManifest release2 = await fixture.PublishAsync(groups);

        // A global update that never mentions "maps" - the old data is preserved but must not be exposed as
        // active once it no longer matches the manifest it would be served under.
        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release2));

        PackageGroupState maps = state.Groups.Single(group => group.Name == "maps");
        Assert.Equal(PackageGroupStatus.Stale, maps.Status);
        Assert.Equal(release1.ManifestHash, maps.VerifiedManifestHash);
        Assert.Equal(previousInstallationKey, maps.InstallationKey);
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
    }

    [Theory]
    [InlineData("maps", "audio")]
    [InlineData("audio", "maps")]
    public async Task InstallOptionalGroupsAsync_RequestOrderDoesNotAffectTheOutcome(string first, string second)
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        fixture.WriteSource("audio/track1.bin", "audio-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[]
            {
                fixture.Group("core", required: true),
                fixture.Group("maps", required: false),
                fixture.Group("audio", required: false),
            });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        PackageState initial = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        PackageState state = await harness.Runtime.InstallOptionalGroupsAsync(
            release.Manifest.PackageId, new[] { first, second });

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "audio").Status);
        Assert.Equal(initial.StateRevision + 1, state.StateRevision);
    }

    [Fact]
    public async Task InstallOptionalGroupsAsync_OneGroupFailsInTheBatch_LeavesTheOtherGroupUnactivated()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("audio/track1.bin", "audio-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[]
            {
                fixture.Group("core", required: true),
                fixture.Group("audio", required: false),
                fixture.Group("maps", required: false),
            });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        // Groups are processed in ordinal order ("audio" before "maps"), so audio succeeds fully before maps
        // exhausts its 3 retry attempts and fails the whole batch.
        string mapsPath = ManifestPayloadLookup.PayloadPathFor(release, "maps/level1.bin");
        harness.Server.FailNextRequests(mapsPath, times: 3);
        byte[]? stateBefore = await harness.Storage.ReadPackageStateAsync(release.Manifest.PackageId, CancellationToken.None);

        await Assert.ThrowsAsync<RuntimeException>(
            () => harness.Runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "audio", "maps" }));

        byte[]? stateAfter = await harness.Storage.ReadPackageStateAsync(release.Manifest.PackageId, CancellationToken.None);
        Assert.Equal(stateBefore, stateAfter);
        Assert.True(harness.Server.GetOpenCount(ManifestPayloadLookup.PayloadPathFor(release, "audio/track1.bin")) >= 1);

        // Once the transport is healthy again, both groups can still be completed - the failed batch did not
        // leave anything stuck.
        PackageState recovered = await harness.Runtime.InstallOptionalGroupsAsync(
            release.Manifest.PackageId, new[] { "audio", "maps" });
        Assert.Equal(PackageGroupStatus.Ready, recovered.Groups.Single(group => group.Name == "audio").Status);
        Assert.Equal(PackageGroupStatus.Ready, recovered.Groups.Single(group => group.Name == "maps").Status);
    }
}
