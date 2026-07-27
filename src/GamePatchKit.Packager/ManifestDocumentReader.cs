using System.Text;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Packager;

// Everything that has to hold about a manifest document before its payloads are worth touching: schema, the
// declared manifestHash, strict UTF-8 JSON with no duplicate keys, the expected packageId, canonical bytes and
// Core's semantic and reference rules - in that order, so the cheapest and most specific failure is reported.
//
// Shared on purpose. An incremental package reading its predecessor, a verify run and a sign run all have to
// answer the same question, and duplicating the order or dropping one of the steps in one of them is exactly
// how a manifest gets trusted on one path and rejected on another. Stage and error code are supplied by the
// caller because the same document means something different depending on who handed it over.
internal static class ManifestDocumentReader
{
    private const string SchemaStage = "schema";

    private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static ReleaseManifest Read(
        byte[] manifestBytes,
        string expectedPackageId,
        string expectedManifestHash,
        string stage,
        string invalidDocumentErrorCode)
    {
        // Schema failures keep their own stage: which document it was matters less than the fact that it never
        // got past schema validation.
        GamePatchKitSchemaValidator.ValidateReleaseManifest(manifestBytes, expectedPackageId, SchemaStage, invalidDocumentErrorCode);

        ValidationResult hashValidation = ReleaseIdentity.VerifyManifestHash(manifestBytes, expectedManifestHash);
        if (!hashValidation.IsValid)
        {
            throw new PackageException(hashValidation.Errors);
        }

        ReleaseManifest manifest = Parse(manifestBytes, expectedPackageId, stage, invalidDocumentErrorCode);

        if (manifest.PackageId != expectedPackageId)
        {
            throw Failure(stage, PackageErrorCodes.PackageIdMismatch, "The manifest belongs to a different package.", expectedPackageId);
        }

        byte[] canonicalBytes = ReleaseIdentity.ComputeCanonicalBytes(manifest);
        if (!manifestBytes.AsSpan().SequenceEqual(canonicalBytes))
        {
            throw Failure(stage, invalidDocumentErrorCode, "The manifest bytes are not canonical.", expectedPackageId);
        }

        ValidationResult semanticValidation = ManifestValidator.Validate(manifest);
        if (!semanticValidation.IsValid)
        {
            throw new PackageException(semanticValidation.Errors);
        }

        return manifest;
    }

    private static ReleaseManifest Parse(
        byte[] manifestBytes,
        string expectedPackageId,
        string stage,
        string invalidDocumentErrorCode)
    {
        try
        {
            var loadSettings = new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore,
            };
            JToken token = JToken.Parse(_strictUtf8.GetString(manifestBytes), loadSettings);

            if (token is not JObject obj)
            {
                throw Failure(stage, invalidDocumentErrorCode, "The manifest root must be an object.", expectedPackageId);
            }

            if (!ReleaseManifest.TryParse(obj, out ReleaseManifest? parsed, out IReadOnlyList<GamePatchKitError> parseErrors))
            {
                throw new PackageException(parseErrors);
            }

            return parsed!;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw Failure(stage, invalidDocumentErrorCode, "The manifest is not strict UTF-8 JSON.", expectedPackageId);
        }
    }

    private static PackageException Failure(string stage, string code, string message, string packageId)
    {
        return new PackageException(new GamePatchKitError(stage, code, message, packageId));
    }
}
