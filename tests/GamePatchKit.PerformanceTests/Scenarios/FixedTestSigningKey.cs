using GamePatchKit.Packager;

namespace GamePatchKit.PerformanceTests.Scenarios;

// A fixed, test-only Ed25519 key pair for the "signed verify" scenario - never used to sign anything published,
// so deriving it from a trivial byte sequence (mirroring tests/GamePatchKit.Packager.Tests/SigningKeys.cs and
// the golden-vectors generator's own fixed test key) is deliberate rather than a shortcut around real key
// hygiene.
internal static class FixedTestSigningKey
{
    public static (string PrivateKeyBase64Url, string PublicKeyBase64Url) Generate()
    {
        var privateKeyBytes = new byte[Ed25519ManifestSigner.PrivateKeyByteLength];

        for (int i = 0; i < privateKeyBytes.Length; i++)
        {
            privateKeyBytes[i] = (byte)(i + 1);
        }

        var signer = new Ed25519ManifestSigner(privateKeyBytes);
        return (EncodeBase64Url(privateKeyBytes), EncodeBase64Url(signer.GetPublicKey()));
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
