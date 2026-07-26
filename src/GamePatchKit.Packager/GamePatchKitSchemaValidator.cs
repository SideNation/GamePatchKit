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

    private static readonly Lazy<JsonSchema> _packageConfigSchema = new Lazy<JsonSchema>(
        () => LoadSchema(PackageConfigResourceName));
    private static readonly Lazy<JsonSchema> _releaseManifestSchema = new Lazy<JsonSchema>(
        () => LoadSchema(ReleaseManifestResourceName));

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
        Validate(
            _releaseManifestSchema.Value,
            bytes,
            PackageErrorCodes.InvalidPreviousManifest,
            "The release manifest does not match release-manifest.schema.json.",
            packageId);
    }

    private static void Validate(
        JsonSchema schema,
        byte[] bytes,
        string errorCode,
        string message,
        string packageId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            EvaluationResults result = schema.Evaluate(document.RootElement);

            if (!result.IsValid)
            {
                throw Failure(errorCode, message, packageId);
            }
        }
        catch (PackageException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Failure(errorCode, message, packageId);
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

    private static PackageException Failure(string code, string message, string packageId)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId));
    }
}
