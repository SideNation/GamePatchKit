using System.Text;
using GamePatchKit.Core.Manifests;
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// State recovery only kicks in when a trusted target is available (InstallOrUpdateAsync); InstallOptionalGroupsAsync
// has no target to recover from and must fail outright. Also proves the writer lock genuinely serializes two
// PackageRuntime instances hitting the same runtime root, not just two calls on one instance.
public class TestDotNetAdapterRecovery
{
    [Fact]
    public async Task InstallOrUpdateAsync_CorruptedStateBytes_RecoversFromTheTrustedTargetAndResetsOptionalGroups()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[] { fixture.Group("core", required: true), fixture.Group("maps", required: false) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        await harness.Runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" });

        await WriteRawStateAsync(harness, release.Manifest.PackageId, Encoding.UTF8.GetBytes("not valid json"));

        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);

        // The old state - the only record of maps being Ready - was discarded, not consulted, so recovery
        // cannot claim maps is still installed even though its data is still sitting on disk untouched.
        Assert.Equal(PackageGroupStatus.NotInstalled, state.Groups.Single(group => group.Name == "maps").Status);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_UnsupportedSchemaVersion_RecoversFromTheTrustedTarget()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        string unsupportedSchema =
            $$"""
            {"schemaVersion":999,"stateRevision":1,"packageId":"{{release.Manifest.PackageId}}","active":{"dataVersion":"{{release.Manifest.DataVersion}}","manifestHash":"{{release.ManifestHash}}"},"groups":[]}
            """;
        await WriteRawStateAsync(harness, release.Manifest.PackageId, Encoding.UTF8.GetBytes(unsupportedSchema));

        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
    }

    [Fact]
    public async Task InstallOptionalGroupsAsync_CorruptedStateWithNoTrustedTarget_ThrowsStateInvalid()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await fixture.PublishAsync(
            new[] { fixture.Group("core", required: true), fixture.Group("maps", required: false) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        await WriteRawStateAsync(harness, release.Manifest.PackageId, Encoding.UTF8.GetBytes("not valid json"));

        // InstallOptionalGroupsAsync takes no TargetManifestReference - there is nothing trustworthy to
        // rebuild from, so it must refuse rather than guess at an active release from cache or directory names.
        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => harness.Runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.StateInvalid, exception.Error.Code);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_StateReplaceFails_KeepsThePreviousStateAndCanRetry()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release1 = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release1));
        byte[]? stateBefore = await harness.Storage.ReadPackageStateAsync(release1.Manifest.PackageId, CancellationToken.None);

        fixture.WriteSource("core/data.bin", "core-v2");
        FinalizedManifest release2 = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        var failingStorage = new FailingReplaceStorageDecorator(harness.Storage, failuresBeforeSuccess: 1);
        var runtimeWithFailingCommit = new PackageRuntime(
            new HttpArtifactTransport(harness.HttpClient), failingStorage, DefaultCompressionCodecs.Create());

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtimeWithFailingCommit.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release2)));
        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);

        byte[]? stateAfter = await harness.Storage.ReadPackageStateAsync(release1.Manifest.PackageId, CancellationToken.None);
        Assert.Equal(stateBefore, stateAfter);

        // The injected failure was one-shot: a normal attempt through the real storage still completes.
        PackageState recovered = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release2));
        Assert.Equal(PackageGroupStatus.Ready, recovered.Groups.Single(group => group.Name == "core").Status);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_TwoConcurrentInstancesOnTheSameTarget_NeverHoldTheWriterLockSimultaneously()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        using var harness = new AdapterHarness(fixture.PublishRoot);

        // Both runtimes target the same manifest, so Runtime's own optimistic-concurrency retry (read state,
        // compare against a snapshot, replace) would converge to the same correct single-revision outcome even
        // with a no-op lock - asserting on the outcome alone would not prove the lock does anything. Sharing
        // one storage instance would not prove it either: an instance-local (in-memory) lock would serialize
        // two calls on the same object without providing any real cross-process guarantee. Two separate
        // FileSystemRuntimeStorage instances against the same runtime root, sharing only a SharedLockObserver,
        // stand in for two real processes.
        var observer = new SharedLockObserver();
        var firstStorage = new ExclusivityTrackingStorageDecorator(new FileSystemRuntimeStorage(harness.RuntimeRoot), observer);
        var secondStorage = new ExclusivityTrackingStorageDecorator(new FileSystemRuntimeStorage(harness.RuntimeRoot), observer);
        using var secondClient = new HttpClient(harness.Server) { BaseAddress = harness.HttpClient.BaseAddress };
        var first = new PackageRuntime(new HttpArtifactTransport(harness.HttpClient), firstStorage, DefaultCompressionCodecs.Create());
        var second = new PackageRuntime(new HttpArtifactTransport(secondClient), secondStorage, DefaultCompressionCodecs.Create());

        // Force a real overlap window instead of hoping Task.WhenAll schedules the two into contention: hold
        // the first writer inside its lock-held span - the real OS-level lock is already acquired by this
        // point - and confirm the second's own attempt does not also get through while it waits.
        Task firstEntered = observer.ArmHoldOnNextEntry();
        Task<PackageState> firstInstall = first.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        await firstEntered;

        Task<PackageState> secondInstall = second.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        await Task.Delay(200);
        Assert.False(secondInstall.IsCompleted, "the second writer completed while the first still held the lock");

        observer.ReleaseHold();
        PackageState[] results = await Task.WhenAll(firstInstall, secondInstall);

        Assert.All(
            results,
            state => Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status));
        Assert.Equal(1, observer.MaxObservedConcurrentLocks);

        byte[]? finalStateBytes = await harness.Storage.ReadPackageStateAsync(release.Manifest.PackageId, CancellationToken.None);
        Assert.NotNull(finalStateBytes);
        Assert.True(PackageStateSerializer.TryDeserialize(finalStateBytes!, out PackageState? finalState, out _));
        Assert.Equal(1, finalState!.StateRevision);
    }

    private static async Task WriteRawStateAsync(AdapterHarness harness, string packageId, byte[] bytes)
    {
        string statePath = Path.Combine(harness.RuntimeRoot, "packages", packageId, "state", "package-state.json");
        await File.WriteAllBytesAsync(statePath, bytes);
    }
}
