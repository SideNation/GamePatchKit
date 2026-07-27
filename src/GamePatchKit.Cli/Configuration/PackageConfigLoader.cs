using GamePatchKit.Core.Configuration;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Configuration;

// gamepatchkit.yml on disk to a validated PackageConfig, in the order docs/contracts/gamepatchkit-yml.md
// fixes: YAML document rules, then package-config.schema.json, then Core's model rules. Each layer only sees
// input the one before it accepted, so an error always names the most specific rule that was broken.
internal static class PackageConfigLoader
{
    public const string DefaultFileName = "gamepatchkit.yml";

    public static PackageConfig Load(string path)
    {
        string displayPath = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            throw new CliException(
                CliErrorCodes.FileNotFound,
                "The package configuration file does not exist.",
                displayPath);
        }

        JObject document = YamlConfigDocument.Parse(File.ReadAllText(path), displayPath);
        return PackageConfigReader.Read(document);
    }
}
