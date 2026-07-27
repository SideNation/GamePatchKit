namespace GamePatchKit.Packager;

// The injected signing key handle. Implementations hold private key material - in memory, in an HSM, in a
// remote signing service - and the sign API never sees it: it hands over the bytes to sign and gets back a
// signature plus the fingerprint of the key that produced it.
public interface IManifestSigner
{
    // 'ed25519-' followed by the lowercase hex64 SHA-256 of the raw 32-byte public key. Derived from the key,
    // never an operator-chosen alias, so a manifest.sig names exactly one key.
    string KeyId { get; }

    // Signs the canonical manifest bytes as published. Must return the raw 64-byte Ed25519 signature.
    byte[] Sign(byte[] canonicalManifestBytes);
}
