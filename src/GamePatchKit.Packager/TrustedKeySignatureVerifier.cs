using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager;

// The concrete IManifestSignatureVerifier backing CLI's verify command: a fixed set of trusted raw Ed25519
// public keys, keyed by their derived fingerprint so a manifest.sig's keyId picks out exactly the key that
// must have produced it. Trusting more than one key at once is what lets a rotation have a window where both
// the old and new signing key verify.
public sealed class TrustedKeySignatureVerifier : IManifestSignatureVerifier
{
    private const string Stage = "manifest-verify";

    private readonly IReadOnlyDictionary<string, byte[]> _publicKeysByKeyId;

    public TrustedKeySignatureVerifier(IEnumerable<byte[]> trustedPublicKeys)
    {
        if (trustedPublicKeys == null)
        {
            throw new ArgumentNullException(nameof(trustedPublicKeys));
        }

        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (byte[] publicKey in trustedPublicKeys)
        {
            if (publicKey == null)
            {
                throw new ArgumentException("trustedPublicKeys must not contain null.", nameof(trustedPublicKeys));
            }

            string keyId = Ed25519Signatures.DeriveKeyId(publicKey);
            map[keyId] = (byte[])publicKey.Clone();
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("At least one trusted public key is required.", nameof(trustedPublicKeys));
        }

        _publicKeysByKeyId = map;
    }

    // Decodes each key from unpadded base64url - the encoding the PRD fixes for public keys - so a host can
    // build a trust set directly from configuration or command-line strings without depending on this
    // assembly's internal Base64Url helper.
    public static TrustedKeySignatureVerifier FromBase64UrlPublicKeys(IEnumerable<string> encodedPublicKeys)
    {
        if (encodedPublicKeys == null)
        {
            throw new ArgumentNullException(nameof(encodedPublicKeys));
        }

        var keys = new List<byte[]>();

        foreach (string encoded in encodedPublicKeys)
        {
            if (encoded == null
                || !Base64Url.TryDecode(encoded.Trim(), out byte[] publicKey)
                || publicKey.Length != Ed25519Signatures.PublicKeyByteLength)
            {
                throw new PackageException(new GamePatchKitError(
                    Stage,
                    PackageErrorCodes.InvalidTrustedKey,
                    $"A trusted public key must be unpadded base64url encoding exactly {Ed25519Signatures.PublicKeyByteLength} bytes."));
            }

            keys.Add(publicKey);
        }

        return new TrustedKeySignatureVerifier(keys);
    }

    public bool Verify(byte[] canonicalManifestBytes, ManifestSignature signature)
    {
        if (canonicalManifestBytes == null)
        {
            throw new ArgumentNullException(nameof(canonicalManifestBytes));
        }

        if (signature == null)
        {
            throw new ArgumentNullException(nameof(signature));
        }

        if (!_publicKeysByKeyId.TryGetValue(signature.KeyId, out byte[]? publicKey))
        {
            return false;
        }

        return signature.TryGetSignatureBytes(out byte[] signatureBytes)
            && Ed25519Signatures.Verify(publicKey, canonicalManifestBytes, signatureBytes);
    }
}
