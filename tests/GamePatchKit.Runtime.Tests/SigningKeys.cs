using GamePatchKit.Core.Json;
using GamePatchKit.Core.Signatures;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GamePatchKit.Runtime.Tests;

// Fixed Ed25519 test keys, mirroring tests/GamePatchKit.Packager.Tests/SigningKeys.cs - never used to sign
// anything published, so the private bytes being in the repository is deliberate.
internal static class SigningKeys
{
    public static byte[] PrivateKey()
    {
        return Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    }

    public static byte[] OtherPrivateKey()
    {
        return Enumerable.Range(1, 32).Select(value => (byte)(value + 100)).ToArray();
    }

    public static byte[] PublicKey(byte[] privateKey)
    {
        return new Ed25519PrivateKeyParameters(privateKey, 0).GeneratePublicKey().GetEncoded();
    }

    public static byte[] RawSign(byte[] privateKey, byte[] manifestBytes)
    {
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
        return signer.GenerateSignature();
    }

    // Builds canonical manifest.sig bytes for privateKey's signature over manifestBytes - what
    // FakeArtifactTransport.AddSignature needs to publish a signature a PackageRuntime can fetch and verify.
    // signatureBytesOverride lets a test publish a document naming the real keyId but tampered signature bytes.
    public static byte[] BuildSignatureDocument(byte[] privateKey, byte[] signatureBytes)
    {
        string keyId = Ed25519Signatures.DeriveKeyId(PublicKey(privateKey));
        string base64UrlSignature = Convert.ToBase64String(signatureBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = new ManifestSignature(ManifestSignature.SupportedSchemaVersion, ManifestSignature.SupportedAlgorithm, keyId, base64UrlSignature);

        return CanonicalJsonWriter.Write(signature.ToJson());
    }

    public static byte[] SignManifest(byte[] privateKey, byte[] manifestBytes)
    {
        return BuildSignatureDocument(privateKey, RawSign(privateKey, manifestBytes));
    }
}
