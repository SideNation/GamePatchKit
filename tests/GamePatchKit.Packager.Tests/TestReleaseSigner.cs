using System.Security.Cryptography;
using GamePatchKit.Core;
using GamePatchKit.Core.Signatures;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GamePatchKit.Packager.Tests;

public class TestReleaseSigner
{
    [Fact]
    public async Task SignAsync_ProducesAVerifiableSignatureOverTheCanonicalManifest()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);
        Ed25519ManifestSigner signer = SigningKeys.Signer();

        SignReleaseResult result = await SignAsync(fixture, published.Release.ManifestHash, signer);

        // The whole point of a signature is that an independent verifier accepts it, so this checks the bytes
        // with the primitive rather than re-deriving them the way the signer did.
        Assert.True(VerifyEd25519(
            signer.GetPublicKey(),
            published.Release.GetCanonicalBytes(),
            DecodeBase64Url(result.Signature.Signature)));
        Assert.True(result.Created);
        Assert.Equal(ManifestSignature.SupportedAlgorithm, result.Signature.Algorithm);
        Assert.Equal(ManifestSignature.SupportedSchemaVersion, result.Signature.SchemaVersion);
    }

    [Fact]
    public async Task SignAsync_DerivesKeyIdFromTheRawPublicKey()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        string expectedKeyId = "ed25519-" + Convert.ToHexString(SHA256.HashData(signer.GetPublicKey())).ToLowerInvariant();

        SignReleaseResult result = await SignAsync(fixture, published.Release.ManifestHash, signer);

        Assert.Equal(expectedKeyId, result.KeyId);
        Assert.Equal(expectedKeyId, signer.KeyId);
    }

    [Fact]
    public async Task SignAsync_WritesCanonicalSignatureBytesToTheImmutablePath()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);

        SignReleaseResult result = await SignAsync(fixture, published.Release.ManifestHash, SigningKeys.Signer());

        string path = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash));
        Assert.Equal(result.GetCanonicalBytes(), await File.ReadAllBytesAsync(path));
        Assert.Equal(
            GamePatchKit.Core.Json.CanonicalJsonWriter.Write(result.Signature.ToJson()),
            await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SignAsync_SameManifestAndKeyTwice_VerifiesAndReusesTheExistingBytes()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);
        SignReleaseResult first = await SignAsync(fixture, published.Release.ManifestHash, SigningKeys.Signer());
        string path = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash));
        DateTime writtenAtUtc = File.GetLastWriteTimeUtc(path);

        SignReleaseResult second = await SignAsync(fixture, published.Release.ManifestHash, SigningKeys.Signer());

        Assert.False(second.Created);
        Assert.Equal(first.GetCanonicalBytes(), second.GetCanonicalBytes());
        Assert.Equal(writtenAtUtc, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task SignAsync_DifferentKey_DoesNotReplaceThePublishedSignature()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);
        SignReleaseResult first = await SignAsync(fixture, published.Release.ManifestHash, SigningKeys.Signer());
        string path = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash));

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => SignAsync(fixture, published.Release.ManifestHash, SigningKeys.OtherSigner()));

        Assert.Equal(PackageErrorCodes.ImmutablePathConflict, exception.Errors[0].Code);
        Assert.Equal(first.GetCanonicalBytes(), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SignAsync_DryRun_ProducesTheSignatureWithoutWritingIt()
    {
        using var fixture = new PackageFixture();
        FilePackageResult published = await BuildAsync(fixture);

        SignReleaseResult dryRun = await ReleaseSigner.SignAsync(new SignReleaseRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            published.Release.ManifestHash,
            SigningKeys.Signer(),
            dryRun: true));

        string path = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, published.Release.ManifestHash));
        Assert.False(File.Exists(path));
        Assert.True(dryRun.Created);

        SignReleaseResult real = await SignAsync(fixture, published.Release.ManifestHash, SigningKeys.Signer());
        Assert.Equal(dryRun.GetCanonicalBytes(), real.GetCanonicalBytes());
    }

    [Fact]
    public async Task SignAsync_ManifestHashThatIsNotPublished_IsRejected()
    {
        using var fixture = new PackageFixture();
        await BuildAsync(fixture);

        PackageException exception = await Assert.ThrowsAsync<PackageException>(
            () => SignAsync(fixture, new string('a', 64), SigningKeys.Signer()));

        Assert.Equal(PackageErrorCodes.ManifestNotFound, exception.Errors[0].Code);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Ed25519ManifestSigner_KeyOfTheWrongLength_IsRejectedWithoutEchoingIt(int keyLength)
    {
        byte[] key = Enumerable.Repeat((byte)0x7a, keyLength).ToArray();

        PackageException exception = Assert.Throws<PackageException>(() => new Ed25519ManifestSigner(key));

        Assert.Equal(PackageErrorCodes.InvalidSigningKey, exception.Errors[0].Code);
        Assert.DoesNotContain("7a", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ed25519ManifestSigner_FromBase64UrlPrivateKey_AcceptsTheEncodedFormAndTrimsWhitespace()
    {
        byte[] key = SigningKeys.PrivateKey();
        string encoded = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Ed25519ManifestSigner signer = Ed25519ManifestSigner.FromBase64UrlPrivateKey("  " + encoded + "\n");

        Assert.Equal(new Ed25519ManifestSigner(key).KeyId, signer.KeyId);
    }

    [Fact]
    public void Ed25519ManifestSigner_PaddedOrNonBase64UrlKey_IsRejectedWithoutEchoingIt()
    {
        string padded = Convert.ToBase64String(SigningKeys.PrivateKey());

        PackageException exception = Assert.Throws<PackageException>(
            () => Ed25519ManifestSigner.FromBase64UrlPrivateKey(padded));

        Assert.Equal(PackageErrorCodes.InvalidSigningKey, exception.Errors[0].Code);
        Assert.DoesNotContain(padded[..8], exception.Message, StringComparison.Ordinal);
    }

    private static Task<FilePackageResult> BuildAsync(PackageFixture fixture)
    {
        fixture.WriteSource("data/config.json", "configuration");

        return new FilePackageBuilder(zstdCodec: null).BuildAsync(new FilePackageRequest(
            fixture.Config(compression: CompressionKind.None),
            fixture.OutputRoot));
    }

    private static Task<SignReleaseResult> SignAsync(PackageFixture fixture, string manifestHash, IManifestSigner signer)
    {
        return ReleaseSigner.SignAsync(new SignReleaseRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash,
            signer));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64 + new string('=', (4 - (base64.Length % 4)) % 4));
    }

    private static bool VerifyEd25519(byte[] publicKey, byte[] data, byte[] signature)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(signature);
    }
}
