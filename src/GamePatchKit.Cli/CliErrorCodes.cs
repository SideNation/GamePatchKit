namespace GamePatchKit.Cli;

public static class CliErrorCodes
{
    public const string InvalidArguments = "cli.invalid-arguments";

    public const string FileNotFound = "cli.file-not-found";

    public const string Cancelled = "cli.cancelled";

    public const string ExecutionFailed = "cli.execution-failed";

    // One code per document rule in docs/contracts/gamepatchkit-yml.md, so a CI job can tell "you used an
    // anchor" from "you shipped two documents" without reading prose.
    public const string YamlSyntax = "yaml.syntax";

    public const string YamlEmptyDocument = "yaml.empty-document";

    public const string YamlMultipleDocuments = "yaml.multiple-documents";

    public const string YamlNonMappingRoot = "yaml.non-mapping-root";

    public const string YamlAnchor = "yaml.anchor";

    public const string YamlAlias = "yaml.alias";

    public const string YamlMergeKey = "yaml.merge-key";

    public const string YamlCustomTag = "yaml.custom-tag";

    public const string YamlDuplicateKey = "yaml.duplicate-key";

    public const string YamlNonScalarKey = "yaml.non-scalar-key";

    public const string YamlInvalidTaggedScalar = "yaml.invalid-tagged-scalar";
}
