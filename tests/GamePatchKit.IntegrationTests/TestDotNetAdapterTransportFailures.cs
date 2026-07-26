using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.IntegrationTests;

// A corrupted transport response must fail the operation without leaving corrupt bytes reachable as a cached
// object, and a real mid-download cancellation must leave whatever already finished and verified reusable on
// the next attempt instead of forcing a full re-download.
public class TestDotNetAdapterTransportFailures
{
    [Fact]
    public async Task InstallOrUpdateAsync_CorruptedArtifactResponse_FailsWithoutPoisoningTheCache()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        using var harness = new AdapterHarness(fixture.PublishRoot);
        string corePath = ManifestPayloadLookup.PayloadPathFor(release, "core/data.bin");
        harness.Server.Corrupt(corePath, bytes => bytes.Reverse().ToArray());

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release)));
        Assert.Equal(RuntimeErrorCodes.ArtifactCorrupted, exception.Error.Code);

        Assert.Null(
            await harness.Storage.OpenCachedArtifactAsync(release.Manifest.PackageId, corePath, CancellationToken.None));

        // Same transport, no longer corrupting: a poisoned cache entry would make this fail again.
        harness.Server.Corrupt(corePath, bytes => bytes);
        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_CancelledMidGroup_ReusesTheAlreadyVerifiedFileOnResume()
    {
        using var fixture = new PublishedPackageFixture();
        fixture.WriteSource("core/a.bin", "a-bytes");
        fixture.WriteSource("core/b.bin", "b-bytes");
        FinalizedManifest release = await fixture.PublishAsync(new[] { fixture.Group("core", required: true) });
        string aPath = ManifestPayloadLookup.PayloadPathFor(release, "core/a.bin");
        string bPath = ManifestPayloadLookup.PayloadPathFor(release, "core/b.bin");

        // Artifacts are planned in content-hash order, not file-path order, so which of the two is requested
        // first is discovered with an uncancelled, unheld probe run rather than assumed from file names.
        string firstRequested;
        using (var probe = new AdapterHarness(fixture.PublishRoot))
        {
            await probe.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));
            firstRequested = probe.Server.RequestOrder.First(path => path == aPath || path == bPath);
        }

        string secondRequested = firstRequested == aPath ? bPath : aPath;
        using var harness = new AdapterHarness(fixture.PublishRoot);
        // Held indefinitely rather than delayed: cancellation - not a race against a fixed wait - is what ends
        // this attempt, and only firing it once the first artifact is actually verified into cache proves the
        // resume reuses real, committed work rather than getting lucky on timing.
        IDisposable holdSecond = harness.Server.HoldUntilReleased(secondRequested);
        using var cancelSource = new CancellationTokenSource();
        Task<PackageState> installTask = harness.Runtime.InstallOrUpdateAsync(
            ManifestPayloadLookup.Target(release), cancellationToken: cancelSource.Token);

        await WaitUntilCachedAsync(harness, release.Manifest.PackageId, firstRequested);
        // Confirms the second artifact's request has actually reached the server and is parked on the hold
        // gate before cancelling - otherwise cancellation could fire from an earlier, unrelated point and the
        // test would still pass without ever exercising an in-flight HTTP request being cancelled.
        await WaitUntilRequestedAsync(harness.Server, secondRequested);
        cancelSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installTask);
        Assert.Equal(1, harness.Server.GetOpenCount(firstRequested));

        holdSecond.Dispose();
        PackageState state = await harness.Runtime.InstallOrUpdateAsync(ManifestPayloadLookup.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.Equal(1, harness.Server.GetOpenCount(firstRequested));
    }

    private static async Task WaitUntilCachedAsync(AdapterHarness harness, string packageId, string relativePath)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            Stream? cached = await harness.Storage.OpenCachedArtifactAsync(packageId, relativePath, CancellationToken.None);
            if (cached != null)
            {
                await cached.DisposeAsync();
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{relativePath}' was never committed to cache.");
    }

    private static async Task WaitUntilRequestedAsync(FakePublishServer server, string relativePath)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (server.GetOpenCount(relativePath) > 0)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{relativePath}' was never requested.");
    }
}
