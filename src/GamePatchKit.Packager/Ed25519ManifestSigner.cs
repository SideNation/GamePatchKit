using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Signatures;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GamePatchKit.Packager;

// Ed25519 signing over a raw 32-byte private key held in memory.
//
// The concrete signature library stays behind this class: constructors take bytes or text, Sign returns
// bytes, and nothing in the public surface names the implementation. That is what lets step 11 give Core
// (netstandard2.1) verification built on the same primitive without either side depending on the other's
// framework.
public sealed class Ed25519ManifestSigner : IManifestSigner
{
    public const int PrivateKeyByteLength = 32;

    public const int PublicKeyByteLength = 32;

    private const string Stage = "manifest-sign";

    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly byte[] _publicKey;

    public Ed25519ManifestSigner(byte[] privateKey)
    {
        if (privateKey == null)
        {
            throw new ArgumentNullException(nameof(privateKey));
        }

        // The length is the only thing that can be said about the key material, and it is said without the
        // value: any Ed25519 32-byte string is a valid private key.
        if (privateKey.Length != PrivateKeyByteLength)
        {
            throw KeyFailure($"An Ed25519 private key must be exactly {PrivateKeyByteLength} bytes.");
        }

        _privateKey = new Ed25519PrivateKeyParameters(privateKey, 0);
        _publicKey = _privateKey.GeneratePublicKey().GetEncoded();
        KeyId = DeriveKeyId(_publicKey);
    }

    public string KeyId { get; }

    // Loads a key written as unpadded base64url, the same encoding the PRD fixes for public keys and
    // signatures. Failures describe the format and never echo the input.
    public static Ed25519ManifestSigner FromBase64UrlPrivateKey(string encodedPrivateKey)
    {
        if (encodedPrivateKey == null)
        {
            throw new ArgumentNullException(nameof(encodedPrivateKey));
        }

        if (!Base64Url.TryDecode(encodedPrivateKey.Trim(), out byte[] privateKey))
        {
            throw KeyFailure("An Ed25519 private key must be unpadded base64url.");
        }

        return new Ed25519ManifestSigner(privateKey);
    }

    // The raw 32-byte public key. Safe to publish: it is what a verifier needs, and KeyId is its fingerprint.
    public byte[] GetPublicKey()
    {
        return (byte[])_publicKey.Clone();
    }

    public static string DeriveKeyId(byte[] publicKey)
    {
        if (publicKey == null)
        {
            throw new ArgumentNullException(nameof(publicKey));
        }

        if (publicKey.Length != PublicKeyByteLength)
        {
            throw KeyFailure($"An Ed25519 public key must be exactly {PublicKeyByteLength} bytes.");
        }

        return "ed25519-" + Sha256Hash.ComputeHex(publicKey);
    }

    public byte[] Sign(byte[] canonicalManifestBytes)
    {
        if (canonicalManifestBytes == null)
        {
            throw new ArgumentNullException(nameof(canonicalManifestBytes));
        }

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, _privateKey);
        signer.BlockUpdate(canonicalManifestBytes, 0, canonicalManifestBytes.Length);
        byte[] signature = signer.GenerateSignature();

        if (signature.Length != ManifestSignature.SignatureByteLength)
        {
            throw new PackageException(new GamePatchKitError(
                Stage,
                PackageErrorCodes.InvalidSignature,
                $"Ed25519 signing produced {signature.Length} bytes instead of {ManifestSignature.SignatureByteLength}."));
        }

        return signature;
    }

    private static PackageException KeyFailure(string message)
    {
        return new PackageException(new GamePatchKitError(Stage, PackageErrorCodes.InvalidSigningKey, message));
    }
}
