using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager.Tests;

public class TestReleaseVerifier
{
    [Fact]
    public async Task VerifyAsync_PublishedRelease_ReportsIdentityAndTotals()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        fixture.WriteSource("maps/level1.bin", "map bytes");
        FilePackageResult published = await BuildAsync(fixture);

        ReleaseVerifyReport report = await VerifyAsync(fixture, published.Release.ManifestHash);

        Assert.Equal(fixture.PackageId, report.PackageId);
        Assert.Equal(published.Release.DataVersion, report.DataVersion);
        Assert.Equal(published.Release.CompactVersion, report.CompactVersion);
        Assert.Equal(published.Release.ManifestHash, report.ManifestHash);
        Assert.Equal(2, report.Totals.FileCount);
        Assert.Equal(2, report.Totals.StoredObjectCount);
        Assert.Equal(SignatureState.Absent, report.SignatureState);
        Assert.Null(report.KeyId);
    }

    [Fact]
    public async Task VerifyAsync_ReportsPerGroupCountsAndBytes()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        fixture.WriteSource("maps/level1.bin", "map bytes");
        fixture.WriteSource("maps/level2.bin", "more map bytes");
        FilePackageResult published = await BuildAsync(fixture);

        ReleaseVerifyReport report = await VerifyAsync(fixture, published.Release.ManifestHash);

        GroupTotals core = report.Totals.Groups.Single(group => group.Name == "core");
        GroupTotals maps = report.Totals.Groups.Single(group => group.Name == "maps");
        Assert.Equal(1, core.FileCount);
        Assert.Equal("configuration".Length, core.FileBytes);
        Assert.True(core.Required);
        Assert.Equal(2, maps.FileCount);
        Assert.Equal("map bytes".Length + "more map bytes".Length, maps.FileBytes);
        Assert.False(maps.Required);
        Assert.Equal(report.Totals.FileBytes, core.FileBytes + maps.FileBytes);
    }

    [Fact]
    public async Task VerifyAsync_ManifestHashOfAnotherRelease_IsRejectedBeforeAnyPayloadIsRead()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult first = await BuildAsync(fixture);
        fixture.WriteSource("core/config.json", "changed configuration");
        FilePackageResult second = await BuildAsync(fixture);
        Assert.NotEqual(first.Release.ManifestHash, second.Release.ManifestHash);

        // The bytes are a real published manifest and the hash is a real published hash; they just do not
        // belong together. Recomputing the hash from the bytes instead of comparing against the reference
        // would accept this.
        byte[] manifestBytes = await ReleaseManifestReader.ReadPublishedBytesAsync(
            fixture.OutputRoot,
            fixture.PackageId,
            first.Release.ManifestHash);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new ReleaseVerifier(zstdCodec: null).VerifyAsync(new ReleaseVerifyRequest(
                fixture.OutputRoot,
                fixture.PackageId,
                second.Release.ManifestHash,
                manifestBytes)));

        Assert.Equal(ManifestErrorCodes.ManifestHashMismatch, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_CorruptedArtifactObject_IsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        var artifact = (ManifestArtifact.FileArtifact)published.Release.Manifest.Artifacts[0];
        string objectPath = fixture.OutputPath(artifact.GetPayloadObjects()[0].Path);
        await File.WriteAllTextAsync(objectPath, "corrupted but the same length!!");

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => VerifyAsync(fixture, published.Release.ManifestHash));

        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_UnknownManifestHash_ReportsManifestNotFound()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        await BuildAsync(fixture);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => ReleaseManifestReader.ReadPublishedBytesAsync(
                fixture.OutputRoot,
                fixture.PackageId,
                new string('0', 64)));

        Assert.Equal(PackageErrorCodes.ManifestNotFound, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_SignedRelease_ReportsPresentWithoutAVerifier()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        SignReleaseResult signed = await SignAsync(fixture, published.Release.ManifestHash);

        ReleaseVerifyReport report = await VerifyAsync(fixture, published.Release.ManifestHash);

        // A signature was found and is a valid signature document. Nothing here claims the 64 bytes are
        // cryptographically correct - that needs the verifier step 11 supplies.
        Assert.Equal(SignatureState.Present, report.SignatureState);
        Assert.Equal(signed.KeyId, report.KeyId);
    }

    [Fact]
    public async Task VerifyAsync_SuppliedVerifier_SeesThePublishedCanonicalBytes()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        SignReleaseResult signed = await SignAsync(fixture, published.Release.ManifestHash);
        var verifier = new RecordingSignatureVerifier(accept: true);

        ReleaseVerifyReport report = await VerifyAsync(fixture, published.Release.ManifestHash, verifier);

        Assert.Equal(SignatureState.Verified, report.SignatureState);
        Assert.Equal(published.Release.GetCanonicalBytes(), verifier.SignedBytes);
        Assert.Equal(signed.Signature.Signature, verifier.Signature!.Signature);
    }

    [Fact]
    public async Task VerifyAsync_RejectingVerifier_FailsVerification()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        await SignAsync(fixture, published.Release.ManifestHash);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => VerifyAsync(fixture, published.Release.ManifestHash, new RecordingSignatureVerifier(accept: false)));

        Assert.Equal(PackageErrorCodes.InvalidSignature, exception.Errors[0].Code);
    }

    [Fact]
    public async Task VerifyAsync_RequireSignatureOnAnUnsignedRelease_IsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => new ReleaseVerifier(zstdCodec: null).VerifyAsync(new ReleaseVerifyRequest(
                fixture.OutputRoot,
                fixture.PackageId,
                published.Release.ManifestHash,
                published.Release.GetCanonicalBytes(),
                new RecordingSignatureVerifier(accept: true),
                requireSignature: true)));

        Assert.Equal(PackageErrorCodes.InvalidSignature, exception.Errors[0].Code);
    }

    [Fact]
    public async Task RequireSignatureWithoutAVerifier_IsRefusedRatherThanDowngradedToAPresenceCheck()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);

        // Presence is not a signing gate: a manifest.sig holding any correctly shaped keyId and any 64 bytes
        // reaches SignatureState.Present. Letting a caller ask for "required" without supplying the means to
        // check would hand them a gate that accepts forgeries.
        Assert.Throws<ArgumentException>(() => new ReleaseVerifyRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            published.Release.ManifestHash,
            published.Release.GetCanonicalBytes(),
            signatureVerifier: null,
            requireSignature: true));
    }

    [Fact]
    public async Task VerifyAsync_ForgedSignatureDocument_IsNotReportedAsVerified()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        await SignAsync(fixture, published.Release.ManifestHash);
        WriteForgedSignature(fixture, published.Release.ManifestHash);

        // Without a verifier the forgery is structurally indistinguishable from a real signature, so the only
        // honest answer is Present - never Verified, and never a success that a signing gate could rely on.
        ReleaseVerifyReport report = await VerifyAsync(fixture, published.Release.ManifestHash);
        Assert.Equal(SignatureState.Present, report.SignatureState);

        // With a verifier that actually checks the bytes, it is rejected.
        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => VerifyAsync(fixture, published.Release.ManifestHash, new RecordingSignatureVerifier(accept: false)));
        Assert.Equal(PackageErrorCodes.InvalidSignature, exception.Errors[0].Code);
    }

    private static void WriteForgedSignature(PackageFixture fixture, string manifestHash)
    {
        string keyId = "ed25519-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(new byte[32])).ToLowerInvariant();
        string signature = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 64).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        File.WriteAllBytes(
            fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash)),
            GamePatchKit.Core.Json.CanonicalJsonWriter.Write(
                new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, keyId, signature).ToJson()));
    }

    [Fact]
    public async Task VerifyAsync_CorruptedSignatureDocument_IsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.WriteSource("core/config.json", "configuration");
        FilePackageResult published = await BuildAsync(fixture);
        await SignAsync(fixture, published.Release.ManifestHash);
        await File.WriteAllTextAsync(
            fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash)),
            "{\"schemaVersion\":1}");

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => VerifyAsync(fixture, published.Release.ManifestHash));

        Assert.Equal(PackageErrorCodes.InvalidSignature, exception.Errors[0].Code);
    }

    private static Task<FilePackageResult> BuildAsync(PackageFixture fixture)
    {
        PackageConfig config = fixture.Config(
            compression: CompressionKind.None,
            groups: new[]
            {
                fixture.Group("core", "core/**/*", ArtifactMode.File, required: true),
                fixture.Group("maps", "maps/**/*", ArtifactMode.File, required: false),
            });

        return new FilePackageBuilder(zstdCodec: null).BuildAsync(new FilePackageRequest(config, fixture.OutputRoot));
    }

    private static async Task<ReleaseVerifyReport> VerifyAsync(
        PackageFixture fixture,
        string manifestHash,
        IManifestSignatureVerifier? signatureVerifier = null)
    {
        byte[] manifestBytes = await ReleaseManifestReader.ReadPublishedBytesAsync(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash);

        return await new ReleaseVerifier(zstdCodec: null).VerifyAsync(new ReleaseVerifyRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash,
            manifestBytes,
            signatureVerifier));
    }

    private static Task<SignReleaseResult> SignAsync(PackageFixture fixture, string manifestHash)
    {
        return ReleaseSigner.SignAsync(new SignReleaseRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash,
            SigningKeys.Signer()));
    }

    private sealed class RecordingSignatureVerifier : IManifestSignatureVerifier
    {
        private readonly bool _accept;

        public byte[]? SignedBytes { get; private set; }

        public ManifestSignature? Signature { get; private set; }

        public RecordingSignatureVerifier(bool accept)
        {
            _accept = accept;
        }

        public bool Verify(byte[] canonicalManifestBytes, ManifestSignature signature)
        {
            SignedBytes = canonicalManifestBytes;
            Signature = signature;
            return _accept;
        }
    }
}
