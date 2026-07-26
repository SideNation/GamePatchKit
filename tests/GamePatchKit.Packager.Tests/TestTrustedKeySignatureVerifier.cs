using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager.Tests;

public class TestTrustedKeySignatureVerifier
{
    [Fact]
    public void Verify_SignatureFromATrustedKey_ReturnsTrue()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        byte[] manifestBytes = { 1, 2, 3 };
        var signature = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, signer.KeyId, Base64Url.Encode(signer.Sign(manifestBytes)));
        var verifier = new TrustedKeySignatureVerifier(new[] { signer.GetPublicKey() });

        Assert.True(verifier.Verify(manifestBytes, signature));
    }

    [Fact]
    public void Verify_UnknownKeyId_ReturnsFalseWithoutThrowing()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        Ed25519ManifestSigner otherSigner = SigningKeys.OtherSigner();
        byte[] manifestBytes = { 1, 2, 3 };
        var signature = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, otherSigner.KeyId, Base64Url.Encode(otherSigner.Sign(manifestBytes)));
        var verifier = new TrustedKeySignatureVerifier(new[] { signer.GetPublicKey() });

        Assert.False(verifier.Verify(manifestBytes, signature));
    }

    // A caller could hand Verify a ManifestSignature that never went through ManifestSignature.TryParse (which
    // is what normally guarantees the signature string decodes to exactly 64 bytes). IManifestSignatureVerifier's
    // contract says reject with false, throw only for an unusable trusted-key set - a malformed signature string
    // is bad input data, not that.
    [Fact]
    public void Verify_MalformedSignatureStringWithATrustedKeyId_ReturnsFalseRatherThanThrowing()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        var signature = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, signer.KeyId, "not-a-valid-base64url-signature");
        var verifier = new TrustedKeySignatureVerifier(new[] { signer.GetPublicKey() });

        Assert.False(verifier.Verify(new byte[] { 1, 2, 3 }, signature));
    }

    [Fact]
    public void Verify_TrustedKeyButBitFlippedManifest_ReturnsFalse()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        byte[] manifestBytes = { 1, 2, 3 };
        var signature = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, signer.KeyId, Base64Url.Encode(signer.Sign(manifestBytes)));
        var verifier = new TrustedKeySignatureVerifier(new[] { signer.GetPublicKey() });

        Assert.False(verifier.Verify(new byte[] { 1, 2, 4 }, signature));
    }

    [Fact]
    public void Verify_MultipleTrustedKeys_AcceptsEitherOnesSignature()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        Ed25519ManifestSigner otherSigner = SigningKeys.OtherSigner();
        byte[] manifestBytes = { 1, 2, 3 };
        var verifier = new TrustedKeySignatureVerifier(new[] { signer.GetPublicKey(), otherSigner.GetPublicKey() });

        var signedByFirst = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, signer.KeyId, Base64Url.Encode(signer.Sign(manifestBytes)));
        var signedByOther = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, otherSigner.KeyId, Base64Url.Encode(otherSigner.Sign(manifestBytes)));

        Assert.True(verifier.Verify(manifestBytes, signedByFirst));
        Assert.True(verifier.Verify(manifestBytes, signedByOther));
    }

    [Fact]
    public void Constructor_NoTrustedKeys_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TrustedKeySignatureVerifier(Array.Empty<byte[]>()));
    }

    [Fact]
    public void FromBase64UrlPublicKeys_ValidKey_BuildsAWorkingVerifier()
    {
        Ed25519ManifestSigner signer = SigningKeys.Signer();
        byte[] manifestBytes = { 1, 2, 3 };
        var signature = new ManifestSignature(1, ManifestSignature.SupportedAlgorithm, signer.KeyId, Base64Url.Encode(signer.Sign(manifestBytes)));

        TrustedKeySignatureVerifier verifier = TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(new[] { Base64Url.Encode(signer.GetPublicKey()) });

        Assert.True(verifier.Verify(manifestBytes, signature));
    }

    [Fact]
    public void FromBase64UrlPublicKeys_NullElement_ThrowsInvalidTrustedKeyRatherThanCrashing()
    {
        PackageException exception = Assert.Throws<PackageException>(
            () => TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(new string?[] { null }!));

        Assert.Equal(PackageErrorCodes.InvalidTrustedKey, exception.Errors[0].Code);
    }

    [Fact]
    public void FromBase64UrlPublicKeys_MalformedKey_ThrowsInvalidTrustedKey()
    {
        PackageException exception = Assert.Throws<PackageException>(
            () => TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(new[] { "not-a-valid-key" }));

        Assert.Equal(PackageErrorCodes.InvalidTrustedKey, exception.Errors[0].Code);
    }

    // Convert.FromBase64String does not reject non-zero unused bits in the final base64 group: "...GxwdHh8"
    // (the real, canonical encoding of bytes 0..31) and "...GxwdHh9" both decode to the identical 32 bytes.
    // Accepting the "Hh9" variant would mean a public key has more than one valid encoding.
    [Fact]
    public void FromBase64UrlPublicKeys_NonZeroUnusedPaddingBits_ThrowsInvalidTrustedKey()
    {
        const string nonCanonicalVariant = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh9";

        PackageException exception = Assert.Throws<PackageException>(
            () => TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(new[] { nonCanonicalVariant }));

        Assert.Equal(PackageErrorCodes.InvalidTrustedKey, exception.Errors[0].Code);
    }

    [Fact]
    public void FromBase64UrlPublicKeys_WrongLengthKey_ThrowsInvalidTrustedKey()
    {
        string tooShort = Base64Url.Encode(new byte[16]);

        PackageException exception = Assert.Throws<PackageException>(
            () => TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(new[] { tooShort }));

        Assert.Equal(PackageErrorCodes.InvalidTrustedKey, exception.Errors[0].Code);
    }
}
