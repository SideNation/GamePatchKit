using System.IO;
using System.Text.Json;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Signatures;
using Json.Schema;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.SchemaFixtures;

// Validates tests/fixtures/{package-config,release-manifest,manifest-signature}/{valid,invalid}
// against both schemas/*.schema.json (via JsonSchema.Net, test-only) and Core's own model validation -
// proving the schema files themselves are correct, independent of Core's hand-written parsing/validation.
public class TestSchemaFixtures
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // JsonSchema.Net registers each schema's $id in a shared global registry as it is loaded; calling
    // JsonSchema.FromFile more than once for the same file (once per test method here) registers the same
    // $id as separate objects and corrupts $ref resolution between them. Load each file exactly once.
    private static readonly Dictionary<string, JsonSchema> _schemaCache = new();

    public static IEnumerable<object[]> Models()
    {
        yield return new object[] { "package-config", "package-config.schema.json" };
        yield return new object[] { "release-manifest", "release-manifest.schema.json" };
        yield return new object[] { "manifest-signature", "manifest-signature.schema.json" };
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void ValidFixturesPassSchemaAndCoreValidation(string fixtureFolder, string schemaFileName)
    {
        JsonSchema schema = LoadSchema(schemaFileName);

        foreach (string path in FixturePaths(fixtureFolder, "valid"))
        {
            string text = File.ReadAllText(path);

            EvaluationResults schemaResult = schema.Evaluate(JsonDocument.Parse(text).RootElement);
            Assert.True(schemaResult.IsValid, $"{path} should pass schema validation");

            Assert.True(CoreAccepts(fixtureFolder, (JObject)JToken.Parse(text), out string coreError), $"{path} should pass Core validation: {coreError}");
        }
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void InvalidFixturesFailSchemaOrCoreValidation(string fixtureFolder, string schemaFileName)
    {
        JsonSchema schema = LoadSchema(schemaFileName);

        foreach (string path in FixturePaths(fixtureFolder, "invalid"))
        {
            string text = File.ReadAllText(path);

            bool schemaValid = schema.Evaluate(JsonDocument.Parse(text).RootElement).IsValid;
            bool coreValid = CoreAccepts(fixtureFolder, (JObject)JToken.Parse(text), out _);

            Assert.False(schemaValid && coreValid, $"{path} should fail schema validation, Core validation, or both");
        }
    }

    private static bool CoreAccepts(string fixtureFolder, JObject obj, out string error)
    {
        switch (fixtureFolder)
        {
            case "package-config":
            {
                bool ok = PackageConfig.TryParse(obj, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> parseErrors);
                if (!ok)
                {
                    error = string.Join("; ", parseErrors);
                    return false;
                }

                ValidationResult validation = PackageConfigValidator.Validate(config!);
                error = string.Join("; ", validation.Errors);
                return validation.IsValid;
            }

            case "release-manifest":
            {
                bool ok = ReleaseManifest.TryParse(obj, out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> parseErrors);
                if (!ok)
                {
                    error = string.Join("; ", parseErrors);
                    return false;
                }

                ValidationResult validation = ManifestValidator.Validate(manifest!);
                error = string.Join("; ", validation.Errors);
                return validation.IsValid;
            }

            case "manifest-signature":
            {
                bool ok = ManifestSignature.TryParse(obj, out _, out IReadOnlyList<GamePatchKitError> parseErrors);
                error = string.Join("; ", parseErrors);
                return ok;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureFolder), fixtureFolder, "Unknown fixture folder.");
        }
    }

    private static JsonSchema LoadSchema(string schemaFileName)
    {
        lock (_schemaCache)
        {
            if (!_schemaCache.TryGetValue(schemaFileName, out JsonSchema? schema))
            {
                schema = JsonSchema.FromFile(Path.Combine(RepositoryRoot, "schemas", schemaFileName));
                _schemaCache[schemaFileName] = schema;
            }

            return schema;
        }
    }

    private static IEnumerable<string> FixturePaths(string fixtureFolder, string validOrInvalid)
    {
        string directory = Path.Combine(RepositoryRoot, "tests", "fixtures", fixtureFolder, validOrInvalid);
        return Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GamePatchKit.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("GamePatchKit.sln not found above " + AppContext.BaseDirectory);
    }
}
