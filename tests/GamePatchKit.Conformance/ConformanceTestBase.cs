using System.Security.Cryptography;
using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Signatures;
using GamePatchKit.Packager;
using GamePatchKit.Runtime;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Conformance;

// Every scenario here runs identically against whichever adapter a concrete subclass wires up. Passing for both
// the in-memory reference adapter and a real adapter (e.g. GamePatchKit.DotNet) is itself the proof of PRD
// verification criterion 14 - the scenarios do not additionally compare DownloadPlan object graphs, since
// DownloadPlanner is a pure Core function that never sees an adapter to begin with; what an adapter CAN make
// diverge is whether the resulting PackageState and thrown errors end up the same.
public abstract class ConformanceTestBase : IDisposable
{
    protected ConformanceFixture Fixture { get; }

    protected ConformanceTestBase()
    {
        Fixture = new ConformanceFixture();
    }

    // May return the same instance on every call (as the in-memory adapter does) or a fresh instance each call
    // (as the DotNet adapter does, constructing a new FileSystemRuntimeStorage per call) - either way, every
    // instance returned within one test must observe and mutate the same underlying persistent state, standing
    // in for "two callers/processes sharing one package".
    protected abstract IArtifactTransport CreateTransport();

    protected abstract IRuntimeStorage CreateStorage();

    // Unlike CreateStorage(), always returns a storage instance with its OWN independent backing store, used
    // only for a throwaway probe run that must not affect the state the rest of the test depends on.
    protected abstract IRuntimeStorage CreateIsolatedStorage();

    // Makes a just-published release visible to whatever CreateTransport() returns. A no-op for adapters that
    // read straight from the publish tree on every request (DotNet, via a real HTTP server); required for
    // adapters that need bytes registered up front (the in-memory reference transport).
    protected abstract void RegisterRelease(FinalizedManifest release);

    // Makes raw (possibly semantically invalid) manifest bytes reachable under a specific packageId/manifestHash,
    // bypassing ConformanceFixture/FilePackageBuilder entirely. Needed only by the unsigned negative-vector
    // scenario, which feeds hand-authored invalid fixtures through the real adapter pipeline.
    protected abstract Task RegisterRawManifestAsync(string packageId, string manifestHash, byte[] manifestBytes);

    // Makes a manifest.sig document reachable at the path OpenManifestSignatureAsync will request for
    // manifestHash - the signed-extension counterpart to RegisterRawManifestAsync.
    protected abstract Task RegisterSignatureAsync(string packageId, string manifestHash, byte[] signatureBytes);

    public virtual void Dispose()
    {
        Fixture.Dispose();
    }

    protected static PackageRuntime CreateRuntime(
        IArtifactTransport transport,
        IRuntimeStorage storage,
        TrustedSigningKeys? trustedSigningKeys = null,
        bool requireSignature = false)
    {
        return new PackageRuntime(
            transport,
            storage,
            new[] { ZstdCompressionCodecFactory.Create() },
            trustedSigningKeys,
            requireSignature);
    }

    protected static TargetManifestReference Target(FinalizedManifest release)
    {
        return ManifestPayloadLookup.Target(release);
    }

    protected static string PayloadPathFor(FinalizedManifest release, string filePath)
    {
        return ManifestPayloadLookup.PayloadPathFor(release, filePath);
    }

    [Fact]
    public async Task RequiredOnlyInstall_InstallsRequiredAndLeavesOptionalNotInstalled()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        Fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release);
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage());

        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        PackageGroupState core = state.Groups.Single(group => group.Name == "core");
        Assert.Equal(PackageGroupStatus.Ready, core.Status);
        Assert.False(string.IsNullOrEmpty(core.InstallationKey));
        Assert.Equal(PackageGroupStatus.NotInstalled, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(1, state.StateRevision);
    }

    [Fact]
    public async Task OptionalGroupInstall_TransitionsNotInstalledToReady()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        Fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release);
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage());
        await runtime.InstallOrUpdateAsync(Target(release));

        PackageState state = await runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" });

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(2, state.StateRevision);
    }

    [Fact]
    public async Task OptionalGroupReconnect_UnchangedContent_ReadyWithoutRedownload()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        Fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release1 = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release1);
        var countingTransport = new CountingArtifactTransportDecorator(CreateTransport());
        PackageRuntime runtime = CreateRuntime(countingTransport, CreateStorage());
        await runtime.InstallOrUpdateAsync(Target(release1));
        await runtime.InstallOptionalGroupsAsync(release1.Manifest.PackageId, new[] { "maps" });
        string mapsPayloadPath = PayloadPathFor(release1, "maps/level1.bin");
        Assert.Equal(1, countingTransport.GetOpenCount(mapsPayloadPath));

        Fixture.WriteSource("core/data.bin", "core-v2");
        FinalizedManifest release2 = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release2);

        PackageState state = await runtime.InstallOrUpdateAsync(Target(release2));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "maps").Status);
        Assert.Equal(1, countingTransport.GetOpenCount(mapsPayloadPath));
    }

    [Fact]
    public async Task OptionalGroupReconnect_ChangedContent_BecomesStaleThenReinstallSucceeds()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        Fixture.WriteSource("maps/level1.bin", "maps-v1");
        FinalizedManifest release1 = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release1);
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage());
        await runtime.InstallOrUpdateAsync(Target(release1));
        await runtime.InstallOptionalGroupsAsync(release1.Manifest.PackageId, new[] { "maps" });

        Fixture.WriteSource("maps/level1.bin", "maps-v2-different-bytes");
        FinalizedManifest release2 = await Fixture.PublishAsync(
            new[] { Fixture.Group("core", required: true), Fixture.Group("maps", required: false) });
        RegisterRelease(release2);

        PackageState staleState = await runtime.InstallOrUpdateAsync(Target(release2));
        PackageGroupState staleMaps = staleState.Groups.Single(group => group.Name == "maps");
        Assert.Equal(PackageGroupStatus.Stale, staleMaps.Status);
        Assert.Equal(release1.ManifestHash, staleMaps.VerifiedManifestHash);

        PackageState readyState = await runtime.InstallOptionalGroupsAsync(release2.Manifest.PackageId, new[] { "maps" });
        PackageGroupState readyMaps = readyState.Groups.Single(group => group.Name == "maps");
        Assert.Equal(PackageGroupStatus.Ready, readyMaps.Status);
        Assert.Equal(release2.ManifestHash, readyMaps.VerifiedManifestHash);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_CancelledMidDownload_ResumeReusesAlreadyVerifiedCache()
    {
        Fixture.WriteSource("core/a.bin", "a-bytes-of-content");
        Fixture.WriteSource("core/b.bin", "b-bytes-of-content");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        string aPath = PayloadPathFor(release, "core/a.bin");
        string bPath = PayloadPathFor(release, "core/b.bin");

        // Artifacts are planned in content-hash order, not file-path order: an uncancelled, isolated probe run
        // discovers which of the two is requested first instead of assuming it from file names. Isolated
        // storage keeps this probe from actually installing anything the real run below depends on.
        var probeCounting = new CountingArtifactTransportDecorator(CreateTransport());
        PackageRuntime probeRuntime = CreateRuntime(probeCounting, CreateIsolatedStorage());
        await probeRuntime.InstallOrUpdateAsync(Target(release));
        string firstRequested = probeCounting.GetOpenCount(aPath) > 0 ? aPath : bPath;
        string secondRequested = firstRequested == aPath ? bPath : aPath;

        IRuntimeStorage storage = CreateStorage();
        var holding = new HoldingArtifactTransportDecorator(CreateTransport());
        var counting = new CountingArtifactTransportDecorator(holding);
        PackageRuntime runtime = CreateRuntime(counting, storage);
        IDisposable hold = holding.HoldUntilReleased(secondRequested);
        using var cancelSource = new CancellationTokenSource();

        Task<PackageState> installTask = runtime.InstallOrUpdateAsync(Target(release), cancellationToken: cancelSource.Token);

        await WaitUntilCachedAsync(storage, release.Manifest.PackageId, firstRequested);
        await WaitUntilRequestedAsync(counting, secondRequested);
        cancelSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installTask);
        Assert.Equal(1, counting.GetOpenCount(firstRequested));

        hold.Dispose();
        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
        Assert.Equal(1, counting.GetOpenCount(firstRequested));
    }

    [Fact]
    public async Task CorruptedArtifactResponse_FailsWithoutPoisoningCache()
    {
        Fixture.WriteSource("core/data.bin", "core-v1-content");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        string corePath = PayloadPathFor(release, "core/data.bin");
        var corrupting = new CorruptingArtifactTransportDecorator(CreateTransport());
        corrupting.CorruptNextOpen(corePath);
        IRuntimeStorage storage = CreateStorage();
        PackageRuntime corruptingRuntime = CreateRuntime(corrupting, storage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => corruptingRuntime.InstallOrUpdateAsync(Target(release)));
        Assert.Equal(RuntimeErrorCodes.ArtifactCorrupted, exception.Error.Code);
        Assert.Null(await storage.OpenCachedArtifactAsync(release.Manifest.PackageId, corePath, CancellationToken.None));

        PackageRuntime runtime = CreateRuntime(CreateTransport(), storage);
        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
    }

    // FailingReplaceStorageDecorator fails before ever calling the real ReplacePackageStateAsync, so this
    // proves Runtime does not advance its own notion of state when the storage call fails outright - it does
    // NOT exercise a real adapter's internal partial-write atomicity (e.g. a crash between writing a temp file
    // and the atomic rename that publishes it). That is inherently adapter-specific - a generic decorator
    // wrapping the IRuntimeStorage interface cannot interrupt an adapter's own implementation mid-write - and
    // is covered separately at the adapter level (see GamePatchKit.DotNet.Tests' interrupted-replace-leaves-
    // old-state test).
    [Fact]
    public async Task StateReplaceFailureInjection_KeepsPreviousStateObservableAndCanRetry()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release1 = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release1);
        IRuntimeStorage storage = CreateStorage();
        PackageRuntime runtime = CreateRuntime(CreateTransport(), storage);
        await runtime.InstallOrUpdateAsync(Target(release1));
        byte[]? stateBefore = await storage.ReadPackageStateAsync(release1.Manifest.PackageId, CancellationToken.None);

        Fixture.WriteSource("core/data.bin", "core-v2");
        FinalizedManifest release2 = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release2);
        var failingStorage = new FailingReplaceStorageDecorator(storage, failuresBeforeSuccess: 1);
        PackageRuntime failingRuntime = CreateRuntime(CreateTransport(), failingStorage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => failingRuntime.InstallOrUpdateAsync(Target(release2)));
        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);

        byte[]? stateAfter = await storage.ReadPackageStateAsync(release1.Manifest.PackageId, CancellationToken.None);
        Assert.Equal(stateBefore, stateAfter);

        PackageState recovered = await runtime.InstallOrUpdateAsync(Target(release2));
        Assert.Equal(PackageGroupStatus.Ready, recovered.Groups.Single(group => group.Name == "core").Status);
    }

    [Fact]
    public async Task MultiGroupBatch_OneGroupPromotionFails_NoPartialReadyStateThenRetrySucceeds()
    {
        Fixture.WriteSource("alpha/a.bin", "alpha-content");
        Fixture.WriteSource("beta/b.bin", "beta-content");
        FinalizedManifest release = await Fixture.PublishAsync(
            new[] { Fixture.Group("alpha", required: true), Fixture.Group("beta", required: true) });
        RegisterRelease(release);
        IRuntimeStorage storage = CreateStorage();
        var failingStorage = new FailingGroupPromotionStorageDecorator(storage, failingGroup: "beta", failuresBeforeSuccess: 1);
        PackageRuntime failingRuntime = CreateRuntime(CreateTransport(), failingStorage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => failingRuntime.InstallOrUpdateAsync(Target(release)));
        // Confirms the failure is attributed to "beta" specifically - the group whose promotion was made to
        // fail - not some unrelated earlier step, before trusting that its absence proves anything.
        Assert.Equal("beta", exception.Error.Group);
        Assert.Null(await storage.ReadPackageStateAsync(release.Manifest.PackageId, CancellationToken.None));

        PackageRuntime runtime = CreateRuntime(CreateTransport(), storage);
        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "alpha").Status);
        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "beta").Status);
        Assert.Equal(1, state.StateRevision);
    }

    [Fact]
    public async Task ConcurrentOptionalGroupInstalls_OnDifferentGroups_BothLandInFinalStateWithoutLostUpdate()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        Fixture.WriteSource("maps-a/a.bin", "maps-a-content");
        Fixture.WriteSource("maps-b/b.bin", "maps-b-content");
        FinalizedManifest release = await Fixture.PublishAsync(
            new[]
            {
                Fixture.Group("core", required: true),
                Fixture.Group("maps-a", required: false),
                Fixture.Group("maps-b", required: false),
            });
        RegisterRelease(release);
        IRuntimeStorage storage = CreateStorage();
        PackageRuntime setupRuntime = CreateRuntime(CreateTransport(), storage);
        await setupRuntime.InstallOrUpdateAsync(Target(release));

        string mapsAPayloadPath = PayloadPathFor(release, "maps-a/a.bin");
        var holding = new HoldingArtifactTransportDecorator(CreateTransport());
        var counting = new CountingArtifactTransportDecorator(holding);
        PackageRuntime runtimeA = CreateRuntime(counting, storage);
        PackageRuntime runtimeB = CreateRuntime(CreateTransport(), storage);

        // Holds caller A mid-download - well before it ever attempts to commit - so caller B can commit its
        // own unrelated group change first. Releasing A afterward proves it detects the resulting revision
        // conflict and retries onto the new state instead of losing B's update or corrupting its own.
        IDisposable hold = holding.HoldUntilReleased(mapsAPayloadPath);
        Task<PackageState> taskA = runtimeA.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps-a" });
        await WaitUntilRequestedAsync(counting, mapsAPayloadPath);

        PackageState stateB = await runtimeB.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps-b" });
        Assert.Equal(PackageGroupStatus.Ready, stateB.Groups.Single(group => group.Name == "maps-b").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, stateB.Groups.Single(group => group.Name == "maps-a").Status);

        hold.Dispose();
        PackageState stateA = await taskA;

        Assert.Equal(PackageGroupStatus.Ready, stateA.Groups.Single(group => group.Name == "maps-a").Status);
        Assert.Equal(PackageGroupStatus.Ready, stateA.Groups.Single(group => group.Name == "maps-b").Status);
        Assert.Equal(3, stateA.StateRevision);
    }

    [Fact]
    public async Task WriterLock_TwoConcurrentCallers_NeverHeldSimultaneously()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);

        var observer = new SharedLockObserver();
        var storageA = new ExclusivityTrackingStorageDecorator(CreateStorage(), observer);
        var storageB = new ExclusivityTrackingStorageDecorator(CreateStorage(), observer);
        PackageRuntime first = CreateRuntime(CreateTransport(), storageA);
        PackageRuntime second = CreateRuntime(CreateTransport(), storageB);

        Task firstEntered = observer.ArmHoldOnNextEntry();
        Task<PackageState> firstInstall = first.InstallOrUpdateAsync(Target(release));
        await firstEntered;

        Task<PackageState> secondInstall = second.InstallOrUpdateAsync(Target(release));
        // Waits for proof the second writer has actually started racing for the lock (not a fixed delay guess)
        // before asserting it hasn't completed - otherwise a slow second attempt that simply hasn't reached the
        // lock yet, under a broken no-op lock, could make this assertion pass for the wrong reason.
        await WaitUntilAsync(() => observer.AttemptCount >= 2);
        Assert.False(secondInstall.IsCompleted, "the second writer completed while the first still held the lock");

        observer.ReleaseHold();
        PackageState[] results = await Task.WhenAll(firstInstall, secondInstall);

        Assert.All(
            results,
            state => Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status));
        Assert.Equal(1, observer.MaxObservedConcurrentLocks);
    }

    [Fact]
    public async Task BundleGroupWithZstdCompression_InstallsAndReconstructsSourceBytes()
    {
        Fixture.WriteSource("assets/a.txt", "asset-file-a-content");
        Fixture.WriteSource("assets/b.txt", "asset-file-b-content");
        Fixture.WriteSource("assets/c.txt", "asset-file-c-content");
        FinalizedManifest release = await Fixture.PublishAsync(
            new[] { Fixture.Group("assets", required: true, ArtifactMode.Bundle) },
            CompressionKind.Zstd);
        RegisterRelease(release);
        IRuntimeStorage storage = CreateStorage();
        PackageRuntime runtime = CreateRuntime(CreateTransport(), storage);

        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        PackageGroupState assets = state.Groups.Single(group => group.Name == "assets");
        Assert.Equal(PackageGroupStatus.Ready, assets.Status);
        await AssertInstalledFileMatchesSourceAsync(storage, assets.InstallationKey!, "assets/a.txt", "asset-file-a-content");
        await AssertInstalledFileMatchesSourceAsync(storage, assets.InstallationKey!, "assets/b.txt", "asset-file-b-content");
        await AssertInstalledFileMatchesSourceAsync(storage, assets.InstallationKey!, "assets/c.txt", "asset-file-c-content");
    }

    [Fact]
    public async Task MultipartFile_InstallsAndReconstructsSourceBytes()
    {
        string largeContent = new string('x', 500);
        Fixture.WriteSource("large/big.bin", largeContent);
        FinalizedManifest release = await Fixture.PublishAsync(
            new[] { Fixture.Group("large", required: true) },
            CompressionKind.None,
            maxArtifactBytes: 100);
        RegisterRelease(release);
        IRuntimeStorage storage = CreateStorage();
        PackageRuntime runtime = CreateRuntime(CreateTransport(), storage);

        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        PackageGroupState large = state.Groups.Single(group => group.Name == "large");
        Assert.Equal(PackageGroupStatus.Ready, large.Status);
        var artifact = release.Manifest.Artifacts.OfType<ManifestArtifact.FileArtifact>().Single();
        Assert.True(artifact.GetPayloadObjects().Count > 1, "expected the oversized file to be split into multiple parts");
        await AssertInstalledFileMatchesSourceAsync(storage, large.InstallationKey!, "large/big.bin", largeContent);
    }

    public static IEnumerable<object[]> InvalidManifestFixtureResourceNames()
    {
        const string prefix = "GamePatchKit.Conformance.InvalidManifestFixtures.";

        return typeof(ConformanceTestBase).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new object[] { name });
    }

    [Theory]
    [MemberData(nameof(InvalidManifestFixtureResourceNames))]
    public async Task UnsignedInvalidManifestFixture_IsRejected(string resourceName)
    {
        byte[] rawBytes = ReadEmbeddedResource(resourceName);
        var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(rawBytes));
        string packageId = json.Value<string>("packageId")!;
        string dataVersion = json.Value<string>("dataVersion")!;

        // The fixture files are pretty-printed, not canonical JCS bytes - serving them as-is would make
        // ReleaseManifest's canonical-byte-equality check reject every fixture that parses far before
        // ManifestValidator ever runs, so a parseable fixture's actual semantic defect (duplicate/reference/
        // sort) would never be what's proven. A fixture that fails to parse at all (e.g. an unrecognized
        // artifact discriminator) has no ReleaseManifest to canonicalize, so it is served as-is; TryParse
        // failure is itself the rejection this suite is proving for that fixture.
        byte[] bytesToServe = rawBytes;

        if (ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _))
        {
            Assert.False(
                ManifestValidator.Validate(manifest!).IsValid,
                $"'{resourceName}' parsed successfully and was expected to fail ManifestValidator semantic validation.");
            bytesToServe = ReleaseIdentity.ComputeCanonicalBytes(manifest!);
        }

        // Hashed from the exact bytes being served, so the manifestHash check trivially passes and the
        // fixture's actual defect - not an incidental hash mismatch - is what the rejection proves.
        string manifestHash = Convert.ToHexString(SHA256.HashData(bytesToServe)).ToLowerInvariant();
        await RegisterRawManifestAsync(packageId, manifestHash, bytesToServe);
        var target = new TargetManifestReference(packageId, dataVersion, manifestHash);
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage());

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(target));
        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);
    }

    // Fixed test keys, never used to sign anything published - same pattern as
    // tests/GamePatchKit.Packager.Tests/SigningKeys.cs, kept local since it is only 32 bytes each.
    private static readonly byte[] _testPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] _otherTestPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)(value + 100)).ToArray();

    [Fact]
    public async Task SignedRelease_TrustedAndValid_InstallSucceedsWhenRequired()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        var signer = new Ed25519ManifestSigner(_testPrivateKey);
        await RegisterSignatureAsync(
            release.Manifest.PackageId,
            release.ManifestHash,
            BuildSignatureDocument(signer, release.GetCanonicalBytes()));
        var trustedKeys = new TrustedSigningKeys(new[] { signer.GetPublicKey() });
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage(), trustedKeys, requireSignature: true);

        PackageState state = await runtime.InstallOrUpdateAsync(Target(release));

        Assert.Equal(PackageGroupStatus.Ready, state.Groups.Single(group => group.Name == "core").Status);
    }

    [Fact]
    public async Task SignedRelease_MissingSignatureWithRequireSignature_IsRejected()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        var trustedKeys = new TrustedSigningKeys(new[] { new Ed25519ManifestSigner(_testPrivateKey).GetPublicKey() });
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage(), trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    [Fact]
    public async Task SignedRelease_FromAnUntrustedKey_IsRejectedEvenWithoutRequireSignature()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        var untrustedSigner = new Ed25519ManifestSigner(_otherTestPrivateKey);
        await RegisterSignatureAsync(
            release.Manifest.PackageId,
            release.ManifestHash,
            BuildSignatureDocument(untrustedSigner, release.GetCanonicalBytes()));
        var trustedKeys = new TrustedSigningKeys(new[] { new Ed25519ManifestSigner(_testPrivateKey).GetPublicKey() });
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage(), trustedKeys, requireSignature: false);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    [Fact]
    public async Task SignedRelease_CorruptedSignatureBytes_IsRejected()
    {
        Fixture.WriteSource("core/data.bin", "core-v1");
        FinalizedManifest release = await Fixture.PublishAsync(new[] { Fixture.Group("core", required: true) });
        RegisterRelease(release);
        var signer = new Ed25519ManifestSigner(_testPrivateKey);
        byte[] signatureBytes = signer.Sign(release.GetCanonicalBytes());
        signatureBytes[0] ^= 0x01;
        await RegisterSignatureAsync(
            release.Manifest.PackageId,
            release.ManifestHash,
            BuildSignatureDocument(signer.KeyId, signatureBytes));
        var trustedKeys = new TrustedSigningKeys(new[] { signer.GetPublicKey() });
        PackageRuntime runtime = CreateRuntime(CreateTransport(), CreateStorage(), trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    private static byte[] BuildSignatureDocument(Ed25519ManifestSigner signer, byte[] manifestBytes)
    {
        return BuildSignatureDocument(signer.KeyId, signer.Sign(manifestBytes));
    }

    private static byte[] BuildSignatureDocument(string keyId, byte[] signatureBytes)
    {
        string base64UrlSignature = Convert.ToBase64String(signatureBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = new ManifestSignature(ManifestSignature.SupportedSchemaVersion, ManifestSignature.SupportedAlgorithm, keyId, base64UrlSignature);
        return CanonicalJsonWriter.Write(signature.ToJson());
    }

    private static byte[] ReadEmbeddedResource(string resourceName)
    {
        using Stream stream = typeof(ConformanceTestBase).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task AssertInstalledFileMatchesSourceAsync(
        IRuntimeStorage storage,
        string installationKey,
        string relativePath,
        string expectedContent)
    {
        Stream? stream = await storage.OpenInstallationFileAsync(installationKey, relativePath, CancellationToken.None);
        Assert.NotNull(stream);

        await using (stream)
        {
            using var reader = new StreamReader(stream!);
            string actual = await reader.ReadToEndAsync();
            Assert.Equal(expectedContent, actual);
        }
    }

    private static async Task WaitUntilCachedAsync(IRuntimeStorage storage, string packageId, string relativePath)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            Stream? cached = await storage.OpenCachedArtifactAsync(packageId, relativePath, CancellationToken.None);

            if (cached != null)
            {
                await cached.DisposeAsync();
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{relativePath}' was never committed to cache.");
    }

    private static async Task WaitUntilRequestedAsync(CountingArtifactTransportDecorator counting, string relativePath)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (counting.GetOpenCount(relativePath) > 0)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"'{relativePath}' was never requested.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was never satisfied.");
    }
}
