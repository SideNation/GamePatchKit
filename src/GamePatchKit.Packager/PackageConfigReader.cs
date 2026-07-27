using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Packager;

// Turns already-parsed, JSON-compatible configuration data into a validated PackageConfig: schema first, then
// Core's model rules.
//
// The host decides where the data came from - gamepatchkit.yml for the CLI, something else for another
// caller - and this decides whether it is a configuration. Splitting it there is what keeps the YAML document
// rules out of the Packager and the schema out of every host.
public static class PackageConfigReader
{
    private const string SchemaStage = "schema";

    public static PackageConfig Read(JObject document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // packageId is only known once the document parses, so schema errors carry no package scope. Reading
        // it out of unvalidated data to decorate an error would be trusting the thing being rejected.
        GamePatchKitSchemaValidator.ValidatePackageConfigDocument(
            CanonicalJsonWriter.Write(document),
            packageId: null,
            SchemaStage,
            PackageErrorCodes.InvalidConfiguration);

        if (!PackageConfig.TryParse(document, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> parseErrors))
        {
            throw new PackageException(parseErrors);
        }

        ValidationResult validation = PackageConfigValidator.Validate(config!);
        if (!validation.IsValid)
        {
            throw new PackageException(validation.Errors);
        }

        return config!;
    }
}
