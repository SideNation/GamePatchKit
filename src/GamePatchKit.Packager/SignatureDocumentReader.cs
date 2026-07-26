using System.Text;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Signatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Packager;

// Turns manifest.sig bytes into a ManifestSignature, or fails. Everything a signature document has to satisfy
// without a signature primitive: schema, strict UTF-8 JSON with no duplicate keys, Core's model rules
// (schemaVersion, algorithm, keyId shape, 64 signature bytes in unpadded base64url) and canonical bytes.
//
// Canonical form matters as much as the field values: manifest.sig sits on an immutable path, and re-signing
// compares bytes. Two documents with the same fields but different spacing would compare unequal and turn a
// legitimate reuse into a conflict.
internal static class SignatureDocumentReader
{
    private const string SchemaStage = "schema";

    private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static ManifestSignature Read(byte[] signatureBytes, string packageId, string stage)
    {
        GamePatchKitSchemaValidator.ValidateManifestSignature(
            signatureBytes,
            packageId,
            SchemaStage,
            PackageErrorCodes.InvalidSignature);

        ManifestSignature signature = Parse(signatureBytes, packageId, stage);

        if (!signatureBytes.AsSpan().SequenceEqual(CanonicalJsonWriter.Write(signature.ToJson())))
        {
            throw Failure(stage, "The manifest signature bytes are not canonical.", packageId);
        }

        return signature;
    }

    private static ManifestSignature Parse(byte[] signatureBytes, string packageId, string stage)
    {
        try
        {
            var loadSettings = new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore,
            };
            JToken token = JToken.Parse(_strictUtf8.GetString(signatureBytes), loadSettings);

            if (token is not JObject obj)
            {
                throw Failure(stage, "The manifest signature root must be an object.", packageId);
            }

            if (!ManifestSignature.TryParse(obj, out ManifestSignature? parsed, out IReadOnlyList<GamePatchKitError> errors))
            {
                throw new PackageException(errors);
            }

            return parsed!;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw Failure(stage, "The manifest signature is not strict UTF-8 JSON.", packageId);
        }
    }

    private static PackageException Failure(string stage, string message, string packageId)
    {
        return new PackageException(
            new GamePatchKitError(stage, PackageErrorCodes.InvalidSignature, message, packageId));
    }
}
