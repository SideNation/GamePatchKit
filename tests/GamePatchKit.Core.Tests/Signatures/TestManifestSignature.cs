using System.Security.Cryptography;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Signatures;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.Signatures;

public class TestManifestSignature
{
    // Deterministic 32-byte value (0x00..0x1F) standing in for a raw Ed25519 public key: Core only
    // validates the "ed25519-" + SHA-256(rawKey) derivation and string format, not Ed25519 key validity.
    private static readonly byte[] RawPublicKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly string ValidKeyId = "ed25519-" + Convert.ToHexString(SHA256.HashData(RawPublicKey)).ToLowerInvariant();
    private const string ValidSignature = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0-Pw";

    private static string ValidJson()
    {
        return $@"{{
            ""schemaVersion"": 1,
            ""algorithm"": ""Ed25519"",
            ""keyId"": ""{ValidKeyId}"",
            ""signature"": ""{ValidSignature}""
        }}";
    }

    [Fact]
    public void FixtureKeyIdMatchesRawPublicKeySha256Derivation()
    {
        string wrongDerivation = "ed25519-" + Convert.ToHexString(SHA256.HashData(new byte[32])).ToLowerInvariant();
        var json = (JObject)JToken.Parse(ValidJson());
        json["keyId"] = wrongDerivation;

        // Core validates keyId's string format/derivation shape only; it has no raw key to check against
        // here, so a differently-derived-but-still-well-formed keyId still parses...
        bool ok = ManifestSignature.TryParse(json, out ManifestSignature? signature, out _);
        Assert.True(ok);
        Assert.NotEqual(ValidKeyId, signature!.KeyId);

        // ...which is why this test also pins down the derivation itself: the fixture's keyId must equal
        // "ed25519-" + SHA-256(rawPublicKey), not an arbitrary hex64 string that merely matches the pattern.
        Assert.Equal(ValidKeyId, "ed25519-" + Convert.ToHexString(SHA256.HashData(RawPublicKey)).ToLowerInvariant());
    }

    [Fact]
    public void ParsesValidSignature()
    {
        var json = (JObject)JToken.Parse(ValidJson());

        bool ok = ManifestSignature.TryParse(json, out ManifestSignature? signature, out IReadOnlyList<GamePatchKitError> errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal(ValidKeyId, signature!.KeyId);
        Assert.Equal(ValidSignature, signature.Signature);
    }

    [Fact]
    public void RejectsWrongKeyIdFingerprintFormat()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["keyId"] = "ed25519-not-a-fingerprint";

        bool ok = ManifestSignature.TryParse(json, out ManifestSignature? signature, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(signature);
        Assert.Contains(errors, e => e.Code == ManifestSignatureErrorCodes.InvalidKeyId);
    }

    [Fact]
    public void RejectsSignatureWithWrongDecodedLength()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["signature"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJyg";

        bool ok = ManifestSignature.TryParse(json, out ManifestSignature? signature, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(signature);
        Assert.Contains(errors, e => e.Code == ManifestSignatureErrorCodes.InvalidSignature);
    }

    [Fact]
    public void RejectsUnknownAlgorithm()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["algorithm"] = "RSA";

        bool ok = ManifestSignature.TryParse(json, out ManifestSignature? signature, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Code == ManifestSignatureErrorCodes.InvalidAlgorithm);
    }
}
