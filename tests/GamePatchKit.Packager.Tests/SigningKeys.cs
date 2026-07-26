namespace GamePatchKit.Packager.Tests;

// Fixed Ed25519 test keys. Never used to sign anything published, so the private bytes being in the
// repository is deliberate: signing tests need the same key on every machine to compare key ids and
// signature bytes across runs.
internal static class SigningKeys
{
    public static byte[] PrivateKey()
    {
        return Enumerable.Range(1, Ed25519ManifestSigner.PrivateKeyByteLength).Select(value => (byte)value).ToArray();
    }

    public static byte[] OtherPrivateKey()
    {
        return Enumerable.Range(1, Ed25519ManifestSigner.PrivateKeyByteLength).Select(value => (byte)(value + 100)).ToArray();
    }

    public static Ed25519ManifestSigner Signer()
    {
        return new Ed25519ManifestSigner(PrivateKey());
    }

    public static Ed25519ManifestSigner OtherSigner()
    {
        return new Ed25519ManifestSigner(OtherPrivateKey());
    }
}
