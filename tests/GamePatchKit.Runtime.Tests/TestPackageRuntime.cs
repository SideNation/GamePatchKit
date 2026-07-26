using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Runtime.Tests;

public class TestPackageRuntime
{
    [Fact]
    public async Task InitialInstallDownloadsRequiredOnlyThenInstallsOptionalGroup()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);

        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, initial.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(initial, "core").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "maps").Status);
        Assert.Equal(1, storage.ReplaceCount);
        Assert.Equal(1, transport.ArtifactOpenCount[PayloadPath(release, "data/core.bin")]);
        Assert.False(transport.ArtifactOpenCount.ContainsKey(PayloadPath(release, "data/maps.bin")));

        PackageState withMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });

        Assert.Equal(2, withMaps.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(withMaps, "maps").Status);
        Assert.Equal(initial.Active.ManifestHash, withMaps.Active.ManifestHash);
        Assert.Equal(2, storage.ReplaceCount);
    }

    [Fact]
    public async Task MultiGroupOptionalBatchCommitsOnceOrNotAtAll()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease(includeAudio: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes,
            RuntimeFixture.AudioBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldStateBytes = storage.StateBytes!.ToArray();

        storage.FailPromotionForGroup = "audio";
        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps", "audio" }));

        Assert.Equal(RuntimeErrorCodes.StagingFailed, exception.Error.Code);
        Assert.Equal(oldStateBytes, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "audio").Status);
        Assert.Equal(PackageGroupStatus.NotInstalled, Group(initial, "maps").Status);

        storage.FailPromotionForGroup = null;
        PackageState installed = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "audio", "maps" });

        Assert.Equal(2, installed.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(installed, "audio").Status);
        Assert.Equal(PackageGroupStatus.Ready, Group(installed, "maps").Status);
        Assert.Equal(2, storage.ReplaceCount);
    }

    [Fact]
    public async Task CancellationKeepsVerifiedCacheAndNextRunReusesIt()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease(mapsRequired: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        string corePath = PayloadPath(release, "data/core.bin");
        string mapsPath = PayloadPath(release, "data/maps.bin");
        using var cancellation = new CancellationTokenSource();
        transport.BeforeArtifactOpen = path =>
        {
            if (path == mapsPath)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.InstallOrUpdateAsync(
                RuntimeFixture.Target(release),
                cancellationToken: cancellation.Token));

        Assert.Null(storage.StateBytes);
        Assert.True(storage.Cache.ContainsKey(corePath));

        transport.BeforeArtifactOpen = null;
        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Group(state, "core").Status);
        Assert.Equal(PackageGroupStatus.Ready, Group(state, "maps").Status);
        Assert.Equal(1, transport.ArtifactOpenCount[corePath]);
        Assert.Equal(2, transport.ArtifactOpenCount[mapsPath]);
    }

    [Fact]
    public async Task GlobalUpdateRelinksUnchangedOptionalMarksChangedStaleAndPreparesRequiredTransition()
    {
        FinalizedManifest first = RuntimeFixture.CreateRelease();
        byte[] changedCore = System.Text.Encoding.UTF8.GetBytes("core-data-v2");
        byte[] changedMaps = System.Text.Encoding.UTF8.GetBytes("maps-data-v2");
        FinalizedManifest coreUpdate = RuntimeFixture.CreateRelease(coreBytes: changedCore);
        FinalizedManifest mapsUpdate = RuntimeFixture.CreateRelease(
            coreBytes: changedCore,
            mapsBytes: changedMaps);
        FinalizedManifest mapsRequired = RuntimeFixture.CreateRelease(
            coreBytes: changedCore,
            mapsBytes: changedMaps,
            mapsRequired: true);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, first, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        AddRelease(transport, coreUpdate, changedCore, RuntimeFixture.MapsBytes);
        AddRelease(transport, mapsUpdate, changedCore, changedMaps);
        AddRelease(transport, mapsRequired, changedCore, changedMaps);
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(first));
        PackageState firstWithMaps = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });
        string mapsInstallation = Group(firstWithMaps, "maps").InstallationKey!;
        int mapsDownloads = transport.ArtifactOpenCount[PayloadPath(first, "data/maps.bin")];

        PackageState relinked = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(coreUpdate));

        Assert.Equal(PackageGroupStatus.Ready, Group(relinked, "maps").Status);
        Assert.Equal(mapsInstallation, Group(relinked, "maps").InstallationKey);
        Assert.Equal(coreUpdate.ManifestHash, Group(relinked, "maps").VerifiedManifestHash);
        Assert.Equal(mapsDownloads, transport.ArtifactOpenCount[PayloadPath(first, "data/maps.bin")]);

        PackageState stale = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(mapsUpdate));

        Assert.Equal(PackageGroupStatus.Stale, Group(stale, "maps").Status);
        Assert.Equal(mapsInstallation, Group(stale, "maps").InstallationKey);
        Assert.False(transport.ArtifactOpenCount.ContainsKey(PayloadPath(mapsUpdate, "data/maps.bin")));

        PackageState required = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(mapsRequired));

        Assert.Equal(PackageGroupStatus.Ready, Group(required, "maps").Status);
        Assert.NotEqual(mapsInstallation, Group(required, "maps").InstallationKey);
        Assert.Equal(1, transport.ArtifactOpenCount[PayloadPath(mapsRequired, "data/maps.bin")]);
    }

    [Fact]
    public async Task StateConflictReplansWithoutOverwritingConcurrentBatch()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        string concurrentMaps = storage.AddInstallation(
            "maps",
            ("data/maps.bin", RuntimeFixture.MapsBytes));
        var concurrent = new PackageState(
            PackageState.CurrentSchemaVersion,
            stateRevision: 2,
            RuntimeFixture.PackageId,
            initial.Active,
            new[]
            {
                Group(initial, "core"),
                new PackageGroupState(
                    "maps",
                    PackageGroupStatus.Ready,
                    release.ManifestHash,
                    concurrentMaps),
            });
        storage.BeforeWriterLock = () =>
        {
            storage.StateBytes = PackageStateSerializer.Serialize(concurrent);
        };

        PackageState result = await runtime.InstallOptionalGroupsAsync(
            RuntimeFixture.PackageId,
            new[] { "maps" });

        Assert.Equal(2, result.StateRevision);
        Assert.Equal(concurrentMaps, Group(result, "maps").InstallationKey);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task StateReplacementFailureKeepsOldState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldState = storage.StateBytes!.ToArray();
        storage.FailNextReplace = true;

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);
        Assert.Equal(oldState, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task WriterLockFailureKeepsOldState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        byte[] oldState = storage.StateBytes!.ToArray();
        storage.FailNextWriterLock = true;

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.ActivationFailed, exception.Error.Code);
        Assert.Equal(oldState, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task StagingFailureKeepsStateAbsentAndVerifiedCacheReusable()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage
        {
            FailStagingFilePath = "data/core.bin",
        };
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        string corePayloadPath = PayloadPath(release, "data/core.bin");

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.StagingFailed, exception.Error.Code);
        Assert.Null(storage.StateBytes);
        Assert.True(storage.Cache.ContainsKey(corePayloadPath));

        storage.FailStagingFilePath = null;
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, transport.ArtifactOpenCount[corePayloadPath]);
    }

    [Fact]
    public async Task RejectsManifestHashMismatchBeforeDownloading()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        transport.AddManifest(
            release.ManifestHash,
            release.GetCanonicalBytes().Concat(new byte[] { (byte)'\n' }).ToArray());
        var runtime = new PackageRuntime(transport, storage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.ManifestInvalid, exception.Error.Code);
        Assert.Empty(transport.ArtifactOpenCount);
        Assert.Null(storage.StateBytes);
    }

    [Fact]
    public async Task Signature_TrustedAndValid_AllowsInstallWhenRequired()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), release.GetCanonicalBytes()));
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Group(state, "core").Status);
    }

    [Fact]
    public async Task Signature_MissingWithRequireSignature_IsRejected()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
        Assert.Null(storage.StateBytes);
    }

    [Fact]
    public async Task Signature_MissingWithoutRequireSignature_IsToleratedWhenKeysAreOnlyOpportunisticallyTrusted()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: false);

        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Group(state, "core").Status);
    }

    [Fact]
    public async Task Signature_FromAnUntrustedKey_IsRejectedEvenWithoutRequireSignature()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        // Signed with a key that is never added to the trust set.
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.OtherPrivateKey(), release.GetCanonicalBytes()));
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: false);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
        Assert.Null(storage.StateBytes);
    }

    [Fact]
    public async Task Signature_BitFlippedSignatureBytes_IsRejected()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        byte[] signatureBytes = SigningKeys.RawSign(SigningKeys.PrivateKey(), release.GetCanonicalBytes());
        signatureBytes[0] ^= 0x01;
        transport.AddSignature(release.ManifestHash, SigningKeys.BuildSignatureDocument(SigningKeys.PrivateKey(), signatureBytes));
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    [Fact]
    public async Task Signature_CorruptedDocumentBytes_IsRejected()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1}"));
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    [Fact]
    public void Constructor_RequireSignatureWithoutTrustedKeys_Throws()
    {
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();

        Assert.Throws<ArgumentException>(() => new PackageRuntime(transport, storage, requireSignature: true));
    }

    [Fact]
    public async Task Signature_TransientFailuresThenSuccess_RetriesAndVerifies()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), release.GetCanonicalBytes()));
        transport.FailSignatureTransiently(release.ManifestHash, count: 2);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        PackageState state = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Group(state, "core").Status);
        Assert.Equal(3, transport.SignatureOpenCount[release.ManifestHash]);
    }

    // Documents the current, deliberate behavior rather than leaving it as an untested side effect: a transient
    // failure fetching the signature that exhausts every retry is treated exactly like "no signature exists"
    // (tolerated when not required, rejected when required) rather than surfacing as a distinct transport
    // failure - the same conflation already covered by Signature_MissingWithoutRequireSignature_IsTolerated...
    // and Signature_MissingWithRequireSignature_IsRejected for a signature that was simply never registered.
    [Fact]
    public async Task Signature_TransientFailuresExhausted_IsAHardTransportFailureNotAnAbsence()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), release.GetCanonicalBytes()));
        transport.FailSignatureTransiently(release.ManifestHash, count: 3);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        // requireSignature: false on purpose - every retryable attempt failed transiently, which is never a
        // "this release was never signed" signal, so this must be a hard failure even when a signature is not
        // otherwise required. Only a confirmed-absent failure (IsNotFound) is tolerated.
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: false);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.TransportFailed, exception.Error.Code);
        Assert.Equal(3, transport.SignatureOpenCount[release.ManifestHash]);
    }

    [Fact]
    public async Task Signature_NonTransientFailureNotConfirmedAbsent_IsAHardFailureEvenWithoutRequireSignature()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), release.GetCanonicalBytes()));
        // Simulates a real transport returning something other than a confirmed 404 (e.g. 401/403) while
        // fetching manifest.sig - the transport contract does not let this be told apart from "briefly
        // unreachable", so it must never be silently accepted as "this release was never signed".
        transport.FailSignatureWithUnconfirmedError(release.ManifestHash);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: false);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.TransportFailed, exception.Error.Code);
    }

    [Fact]
    public async Task Signature_OversizedResponse_IsRejectedForItsSizeNotJustAsMalformed()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, new byte[5000]);
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
        // A 5000-byte array of zero bytes would also fail to parse as JSON, so asserting only the error code
        // would pass even if the byte-limit check were removed entirely (it would just fail at the "document
        // is invalid" check instead). Pinning the byte-limit message is what actually proves the size check ran.
        Assert.Contains("byte limit", exception.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signature_TamperedAfterInitialInstall_IsCaughtOnOptionalGroupReVerification()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(release.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), release.GetCanonicalBytes()));
        var trustedKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()) });
        var runtime = new PackageRuntime(transport, storage, trustedSigningKeys: trustedKeys, requireSignature: true);
        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        // Replaces the now-active release's signature with a tampered one, as if the key had been compromised
        // and the trust relationship revoked after this release was already installed. InstallOptionalGroupsAsync
        // re-fetches and re-validates the currently active manifest (including its signature) before planning,
        // rather than trusting the locally cached state - proving that path is not a signature-check bypass.
        byte[] tampered = SigningKeys.RawSign(SigningKeys.PrivateKey(), release.GetCanonicalBytes());
        tampered[0] ^= 0x01;
        transport.AddSignature(release.ManifestHash, SigningKeys.BuildSignatureDocument(SigningKeys.PrivateKey(), tampered));

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(release.Manifest.PackageId, new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);
    }

    [Fact]
    public async Task Signature_KeyRotation_OldKeyReleaseRejectedOnceRemovedFromTrustButNewKeyReleaseAccepted()
    {
        FinalizedManifest oldRelease = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        AddRelease(transport, oldRelease, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(oldRelease.ManifestHash, SigningKeys.SignManifest(SigningKeys.PrivateKey(), oldRelease.GetCanonicalBytes()));

        // Rotation window: both old and new key are trusted, and the old-key release still installs.
        var rotationWindowKeys = new TrustedSigningKeys(
            new[] { SigningKeys.PublicKey(SigningKeys.PrivateKey()), SigningKeys.PublicKey(SigningKeys.OtherPrivateKey()) });
        var duringRotation = new PackageRuntime(transport, new FakeRuntimeStorage(), trustedSigningKeys: rotationWindowKeys, requireSignature: true);
        PackageState duringRotationState = await duringRotation.InstallOrUpdateAsync(RuntimeFixture.Target(oldRelease));
        Assert.Equal(PackageGroupStatus.Ready, Group(duringRotationState, "core").Status);

        // A new release signed with the new key, after the old key has been removed from the trust list.
        FinalizedManifest newRelease = RuntimeFixture.CreateRelease(compactVersion: 1);
        AddRelease(transport, newRelease, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        transport.AddSignature(newRelease.ManifestHash, SigningKeys.SignManifest(SigningKeys.OtherPrivateKey(), newRelease.GetCanonicalBytes()));
        var afterRotationKeys = new TrustedSigningKeys(new[] { SigningKeys.PublicKey(SigningKeys.OtherPrivateKey()) });

        var afterRotationForOldRelease = new PackageRuntime(transport, new FakeRuntimeStorage(), trustedSigningKeys: afterRotationKeys, requireSignature: true);
        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => afterRotationForOldRelease.InstallOrUpdateAsync(RuntimeFixture.Target(oldRelease)));
        Assert.Equal(RuntimeErrorCodes.SignatureInvalid, exception.Error.Code);

        var afterRotationForNewRelease = new PackageRuntime(transport, new FakeRuntimeStorage(), trustedSigningKeys: afterRotationKeys, requireSignature: true);
        PackageState afterRotationState = await afterRotationForNewRelease.InstallOrUpdateAsync(RuntimeFixture.Target(newRelease));
        Assert.Equal(PackageGroupStatus.Ready, Group(afterRotationState, "core").Status);
    }

    [Fact]
    public async Task CorruptCacheObjectIsDownloadedAgain()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        string corePath = PayloadPath(release, "data/core.bin");
        storage.PutCache(corePath, new byte[] { 1, 2, 3 });
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, transport.ArtifactOpenCount[corePath]);
        Assert.Equal(RuntimeFixture.CoreBytes, storage.Cache[corePath]);
    }

    [Fact]
    public async Task RetriesTransientArtifactFailure()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        string corePath = PayloadPath(release, "data/core.bin");
        transport.FailTransiently(corePath, count: 1);
        var runtime = new PackageRuntime(transport, storage);

        await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(2, transport.ArtifactOpenCount[corePath]);
    }

    [Fact]
    public async Task MissingCodecFailsBeforeArtifactsAreDownloadedAndInjectedCodecHandlesMixedGroup()
    {
        FinalizedManifest release = CreateMixedCompressionRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        var withoutCodec = new PackageRuntime(transport, storage);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => withoutCodec.InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.MissingCompressionCodec, exception.Error.Code);
        Assert.Empty(transport.ArtifactOpenCount);

        var withCodec = new PackageRuntime(
            transport,
            storage,
            new[] { new PassThroughZstdCodec() });
        PackageState state = await withCodec.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(PackageGroupStatus.Ready, Assert.Single(state.Groups).Status);
        Assert.Equal(2, transport.ArtifactOpenCount.Count);
    }

    [Fact]
    public async Task CorruptStateRecoversForTrustedGlobalTargetButBlocksOptionalRequest()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage
        {
            StateBytes = System.Text.Encoding.UTF8.GetBytes("{broken"),
        };
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);

        await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        PackageState recovered = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, recovered.StateRevision);
        Assert.Equal(PackageGroupStatus.Ready, Group(recovered, "core").Status);
    }

    [Fact]
    public async Task StateRevisionSafeIntegerLimitFailsWithoutReplacingState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        var atLimit = new PackageState(
            PackageState.CurrentSchemaVersion,
            GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger,
            initial.PackageId,
            initial.Active,
            initial.Groups);
        storage.StateBytes = PackageStateSerializer.Serialize(atLimit);
        byte[] stateAtLimit = storage.StateBytes.ToArray();

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.StateInvalid, exception.Error.Code);
        Assert.Equal(stateAtLimit, storage.StateBytes);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task NoOpAtStateRevisionSafeIntegerLimitDoesNotCreateAnotherRevision()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        var atLimit = new PackageState(
            PackageState.CurrentSchemaVersion,
            GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger,
            initial.PackageId,
            initial.Active,
            initial.Groups);
        storage.StateBytes = PackageStateSerializer.Serialize(atLimit);

        PackageState result = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(GamePatchKit.Core.Json.JsonNumbers.MaxSafeInteger, result.StateRevision);
        Assert.Equal(1, storage.ReplaceCount);
    }

    [Fact]
    public async Task MissingReferencedInstallationIsNotAcceptedAsActiveState()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(transport, release, RuntimeFixture.CoreBytes, RuntimeFixture.MapsBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));
        storage.RemoveInstallation(Group(initial, "core").InstallationKey!);

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => runtime.InstallOptionalGroupsAsync(
                RuntimeFixture.PackageId,
                new[] { "maps" }));

        Assert.Equal(RuntimeErrorCodes.StateInvalid, exception.Error.Code);

        PackageState recovered = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(release));

        Assert.Equal(1, recovered.StateRevision);
        Assert.NotEqual(
            Group(initial, "core").InstallationKey,
            Group(recovered, "core").InstallationKey);
    }

    [Fact]
    public async Task DeletedFileForcesExactInstallationWithoutDownloadingUnchangedFile()
    {
        FinalizedManifest first = CreateCoreFilesRelease(includeSecond: true);
        FinalizedManifest second = CreateCoreFilesRelease(includeSecond: false);
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            first,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        AddRelease(transport, second, RuntimeFixture.CoreBytes);
        var runtime = new PackageRuntime(transport, storage);
        PackageState initial = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(first));
        string initialKey = Group(initial, "core").InstallationKey!;
        string remainingPath = PayloadPath(first, "data/core.bin");
        int originalDownloads = transport.ArtifactOpenCount[remainingPath];

        PackageState updated = await runtime.InstallOrUpdateAsync(RuntimeFixture.Target(second));

        Assert.NotEqual(initialKey, Group(updated, "core").InstallationKey);
        Assert.Equal(originalDownloads, transport.ArtifactOpenCount[remainingPath]);
    }

    [Fact]
    public async Task SelectedInstalledGroupStillRequiresItsDeclaredCodec()
    {
        FinalizedManifest release = CreateMixedCompressionRelease();
        var transport = new FakeArtifactTransport();
        var storage = new FakeRuntimeStorage();
        AddRelease(
            transport,
            release,
            RuntimeFixture.CoreBytes,
            RuntimeFixture.MapsBytes);
        await new PackageRuntime(
            transport,
            storage,
            new[] { new PassThroughZstdCodec() })
            .InstallOrUpdateAsync(RuntimeFixture.Target(release));

        RuntimeException exception = await Assert.ThrowsAsync<RuntimeException>(
            () => new PackageRuntime(transport, storage)
                .InstallOrUpdateAsync(RuntimeFixture.Target(release)));

        Assert.Equal(RuntimeErrorCodes.MissingCompressionCodec, exception.Error.Code);
        Assert.Equal(1, storage.ReplaceCount);
    }

    private static FinalizedManifest CreateMixedCompressionRelease()
    {
        string firstHash = RuntimeFixture.Hash(RuntimeFixture.CoreBytes);
        string secondHash = RuntimeFixture.Hash(RuntimeFixture.MapsBytes);
        var group = new ManifestGroupEntry("core", required: true);
        var artifacts = new List<ManifestArtifact>
        {
            new ManifestArtifact.FileArtifact(
                CompressionKind.None,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        firstHash,
                        CompressionKind.None),
                    RuntimeFixture.CoreBytes.LongLength,
                    firstHash)),
            new ManifestArtifact.FileArtifact(
                CompressionKind.Zstd,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(
                        RuntimeFixture.PackageId,
                        secondHash,
                        CompressionKind.Zstd),
                    RuntimeFixture.MapsBytes.LongLength,
                    secondHash)),
        };
        var files = new List<ManifestFileEntry>
        {
            new(
                "data/core.bin",
                "core",
                RuntimeFixture.CoreBytes.LongLength,
                firstHash,
                new FileSource.FileReference(firstHash)),
            new(
                "data/maps.bin",
                "core",
                RuntimeFixture.MapsBytes.LongLength,
                secondHash,
                new FileSource.FileReference(secondHash)),
        };
        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(RuntimeFixture.PackageId),
                right.ContentAddressedSortKey(RuntimeFixture.PackageId)));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { group },
            artifacts,
            files);
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return ReleaseIdentity.Finalize(draft, compactVersion: 0);
    }

    private static FinalizedManifest CreateCoreFilesRelease(bool includeSecond)
    {
        var bytesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["data/core.bin"] = RuntimeFixture.CoreBytes,
        };

        if (includeSecond)
        {
            bytesByPath.Add("data/legacy.bin", RuntimeFixture.MapsBytes);
        }

        var artifacts = new List<ManifestArtifact>();
        var files = new List<ManifestFileEntry>();

        foreach (KeyValuePair<string, byte[]> pair in bytesByPath)
        {
            string hash = RuntimeFixture.Hash(pair.Value);
            artifacts.Add(
                new ManifestArtifact.FileArtifact(
                    CompressionKind.None,
                    new FilePayload.Single(
                        ContentAddressedPath.FileSinglePayloadPath(
                            RuntimeFixture.PackageId,
                            hash,
                            CompressionKind.None),
                        pair.Value.LongLength,
                        hash)));
            files.Add(
                new ManifestFileEntry(
                    pair.Key,
                    "core",
                    pair.Value.LongLength,
                    hash,
                    new FileSource.FileReference(hash)));
        }

        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(RuntimeFixture.PackageId),
                right.ContentAddressedSortKey(RuntimeFixture.PackageId)));
        files.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.Path,
                right.Path));
        var draft = new ReleaseManifest(
            1,
            RuntimeFixture.PackageId,
            "v1-" + new string('0', 64),
            0,
            new[] { new ManifestGroupEntry("core", required: true) },
            artifacts,
            files);
        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return ReleaseIdentity.Finalize(draft, compactVersion: 0);
    }

    private static void AddRelease(
        FakeArtifactTransport transport,
        FinalizedManifest release,
        params byte[][] fileBytes)
    {
        transport.AddRelease(release);

        for (int index = 0; index < release.Manifest.Files.Count; index++)
        {
            ManifestFileEntry file = release.Manifest.Files[index];
            byte[] bytes = fileBytes.Single(bytes => RuntimeFixture.Hash(bytes) == file.FileHash);
            ManifestArtifact.FileArtifact artifact = release.Manifest.Artifacts
                .OfType<ManifestArtifact.FileArtifact>()
                .Single(
                    candidate => candidate.PrimaryArtifactHash
                        == ((FileSource.FileReference)file.Source).ArtifactHash);
            transport.AddArtifact(
                Assert.Single(artifact.GetPayloadObjects()).Path,
                bytes);
        }
    }

    private static PackageGroupState Group(PackageState state, string name)
    {
        return state.Groups.Single(group => group.Name == name);
    }

    private static string PayloadPath(FinalizedManifest release, string filePath)
    {
        ManifestFileEntry file = release.Manifest.Files.Single(file => file.Path == filePath);
        string hash = ((FileSource.FileReference)file.Source).ArtifactHash;
        return Assert.Single(
            release.Manifest.Artifacts
                .OfType<ManifestArtifact.FileArtifact>()
                .Single(artifact => artifact.PrimaryArtifactHash == hash)
                .GetPayloadObjects()).Path;
    }
}
