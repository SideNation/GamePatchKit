using System.Reflection;
using System.Text;
using System.Text.Json;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Json.Schema;

namespace GamePatchKit.Packager;

internal static class GamePatchKitSchemaValidator
{
    private const string Stage = "schema";
    private const string PackageConfigResourceName = "GamePatchKit.Packager.Schemas.package-config.schema.json";
    private const string ReleaseManifestResourceName = "GamePatchKit.Packager.Schemas.release-manifest.schema.json";
    private const string ManifestSignatureResourceName = "GamePatchKit.Packager.Schemas.manifest-signature.schema.json";

    private static readonly Lazy<JsonSchema> _packageConfigSchema = new Lazy<JsonSchema>(
        () => LoadSchema(PackageConfigResourceName));
    private static readonly Lazy<JsonSchema> _releaseManifestSchema = new Lazy<JsonSchema>(
        () => LoadSchema(ReleaseManifestResourceName));
    private static readonly Lazy<JsonSchema> _manifestSignatureSchema = new Lazy<JsonSchema>(
        () => LoadSchema(ManifestSignatureResourceName));

    public static void ValidatePackageConfig(PackageConfig config)
    {
        byte[] bytes = CanonicalJsonWriter.Write(config.ToJson());
        Validate(
            _packageConfigSchema.Value,
            bytes,
            PackageErrorCodes.InvalidConfiguration,
            "The package configuration does not match package-config.schema.json.",
            config.PackageId);
    }

    public static void ValidateReleaseManifest(byte[] bytes, string packageId)
    {
        ValidateReleaseManifest(bytes, packageId, Stage, PackageErrorCodes.InvalidPreviousManifest);
    }

    public static void ValidateReleaseManifest(byte[] bytes, string? packageId, string stage, string errorCode)
    {
        Validate(
            _releaseManifestSchema.Value,
            bytes,
            errorCode,
            "The release manifest does not match release-manifest.schema.json.",
            packageId,
            stage);
    }

    public static void ValidateManifestSignature(byte[] bytes, string? packageId, string stage, string errorCode)
    {
        Validate(
            _manifestSignatureSchema.Value,
            bytes,
            errorCode,
            "The manifest signature does not match manifest-signature.schema.json.",
            packageId,
            stage);
    }

    public static void ValidatePackageConfigDocument(byte[] bytes, string? packageId, string stage, string errorCode)
    {
        Validate(
            _packageConfigSchema.Value,
            bytes,
            errorCode,
            "The package configuration does not match package-config.schema.json.",
            packageId,
            stage);
    }

    private static void Validate(
        JsonSchema schema,
        byte[] bytes,
        string errorCode,
        string message,
        string? packageId,
        string stage = Stage)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            EvaluationResults result = schema.Evaluate(document.RootElement);

            if (!result.IsValid)
            {
                throw Failure(errorCode, message, packageId, stage);
            }
        }
        catch (PackageException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Failure(errorCode, message, packageId, stage);
        }
    }

    private static JsonSchema LoadSchema(string resourceName)
    {
        Assembly assembly = typeof(GamePatchKitSchemaValidator).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded schema resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private static PackageException Failure(string code, string message, string? packageId, string stage)
    {
        return new PackageException(new GamePatchKitError(stage, code, message, packageId));
    }
}
