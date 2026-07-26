using GamePatchKit.Cli.Configuration;
using GamePatchKit.Core.Configuration;

namespace GamePatchKit.Cli.Tests;

// Verification criterion 24, against the fixtures step 02 froze for exactly this purpose. Each invalid
// fixture breaks one rule, so the assertion is not just "rejected" but "rejected for that rule".
public class TestConfigInputContract
{
    [Theory]
    [InlineData("minimal.yml")]
    [InlineData("groups-and-compression.yml")]
    public void ValidFixture_PassesSchemaAndModelValidation(string fileName)
    {
        PackageConfig config = PackageConfigLoader.Load(FixturePath("valid", fileName));

        Assert.Equal("sample-game-client-data", config.PackageId);
        Assert.NotEmpty(config.Include);
    }

    [Fact]
    public void ValidFixture_KeepsScalarTypesTheSchemaExpects()
    {
        PackageConfig config = PackageConfigLoader.Load(FixturePath("valid", "groups-and-compression.yml"));

        // A YAML scalar has no declared type: these only reach the model correctly if plain scalars resolve
        // to the integer and boolean the schema requires rather than to strings.
        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal(10485760L, config.MaxArtifactBytes);
        Assert.True(config.Groups.Single(group => group.Name == "core").Required);
        Assert.False(config.Groups.Single(group => group.Name == "maps").Required);
    }

    [Theory]
    [InlineData("empty-document.yml", CliErrorCodes.YamlEmptyDocument)]
    [InlineData("multiple-documents.yml", CliErrorCodes.YamlMultipleDocuments)]
    [InlineData("non-mapping-root-sequence.yml", CliErrorCodes.YamlNonMappingRoot)]
    [InlineData("non-mapping-root-scalar.yml", CliErrorCodes.YamlNonMappingRoot)]
    [InlineData("anchor-alias.yml", CliErrorCodes.YamlAnchor)]
    [InlineData("merge-key.yml", CliErrorCodes.YamlAnchor)]
    [InlineData("custom-tag.yml", CliErrorCodes.YamlCustomTag)]
    [InlineData("duplicate-key.yml", CliErrorCodes.YamlDuplicateKey)]
    public void InvalidFixture_IsRejectedByTheRuleItBreaks(string fileName, string expectedCode)
    {
        CliException exception = Assert.Throws<CliException>(() => PackageConfigLoader.Load(FixturePath("invalid", fileName)));

        Assert.Equal(expectedCode, exception.Errors[0].Code);
        Assert.Equal(fileName, exception.Errors[0].RelativePath);
    }

    [Fact]
    public void MergeKeyWithoutAnAnchor_IsRejectedAsAMergeKey()
    {
        // The merge-key fixture declares an anchor to point at, so it fails on the anchor first. A merge key
        // whose value is inline has no anchor to trip over and has to be rejected on its own.
        CliException exception = Assert.Throws<CliException>(() => YamlConfigDocument.Parse(
            """
            groups:
              - name: core
                <<:
                  artifactMode: file
            """,
            "inline.yml"));

        Assert.Equal(CliErrorCodes.YamlMergeKey, exception.Errors[0].Code);
    }

    [Fact]
    public void AliasWithoutAnAnchorInScope_IsRejectedBeforeTheParserResolvesIt()
    {
        CliException exception = Assert.Throws<CliException>(() => YamlConfigDocument.Parse(
            "include: *missing\n",
            "inline.yml"));

        // Rejected as an alias, not as a YAML resolution failure: the rule is that aliases are not allowed,
        // regardless of whether this one would have resolved.
        Assert.Equal(CliErrorCodes.YamlAlias, exception.Errors[0].Code);
    }

    [Fact]
    public void BuiltInTag_DecidesTheTypeRatherThanTheSpelling()
    {
        Newtonsoft.Json.Linq.JObject document = YamlConfigDocument.Parse(
            "a: !!str 1\nb: !!str true\nc: !!int 7\nd: !!bool true\ne: !!null ~\n",
            "inline.yml");

        // Asserting the token type, not a string cast: (string?) on an integer token happily yields "1", so a
        // cast-based assertion passes while the value is the wrong type and the schema later rejects it.
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, document["a"]!.Type);
        Assert.Equal("1", (string?)document["a"]);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, document["b"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, document["c"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Boolean, document["d"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, document["e"]!.Type);
    }

    [Theory]
    [InlineData("packageId: !Custom value\n")]
    [InlineData("packageId: !!float 1.5\n")]
    [InlineData("packageId: !!binary aGk=\n")]
    [InlineData("groups: !!str []\n")]
    [InlineData("include: !!map\n  - a\n")]
    public void TagThisContractDoesNotDefine_IsRejected(string yaml)
    {
        // '!!float' and '!!binary' are built-in YAML tags, but canonical JSON cannot carry either, and a tag
        // that names the wrong kind of node is no more meaningful than an application tag.
        CliException exception = Assert.Throws<CliException>(() => YamlConfigDocument.Parse(yaml, "inline.yml"));

        Assert.Equal(CliErrorCodes.YamlCustomTag, exception.Errors[0].Code);
    }

    [Theory]
    [InlineData("packageId: !!int notanumber\n")]
    [InlineData("packageId: !!bool yes\n")]
    [InlineData("packageId: !!null 5\n")]
    [InlineData("packageId: !!int 9007199254740992\n")]
    public void TaggedScalarThatDoesNotHoldThatType_IsRejected(string yaml)
    {
        CliException exception = Assert.Throws<CliException>(() => YamlConfigDocument.Parse(yaml, "inline.yml"));

        Assert.Equal(CliErrorCodes.YamlInvalidTaggedScalar, exception.Errors[0].Code);
    }

    [Theory]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775807")]
    [InlineData("9007199254740992")]
    [InlineData("-9007199254740992")]
    [InlineData("99999999999999999999999999")]
    public void IntegerOutsideTheSafeRange_StaysAStringInsteadOfCrashing(string literal)
    {
        // long.MinValue is the sharp one: it parses as a long, and taking its absolute value throws, so a
        // bounds check written as Math.Abs(...) <= limit terminated the process instead of rejecting the
        // configuration.
        Newtonsoft.Json.Linq.JObject document = YamlConfigDocument.Parse($"maxArtifactBytes: {literal}\n", "inline.yml");

        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, document["maxArtifactBytes"]!.Type);
    }

    [Theory]
    [InlineData("9007199254740991")]
    [InlineData("-9007199254740991")]
    [InlineData("0")]
    public void IntegerOnTheSafeBoundary_StaysAnInteger(string literal)
    {
        Newtonsoft.Json.Linq.JObject document = YamlConfigDocument.Parse($"maxArtifactBytes: {literal}\n", "inline.yml");

        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, document["maxArtifactBytes"]!.Type);
    }

    [Fact]
    public void QuotedScalar_StaysAString()
    {
        Newtonsoft.Json.Linq.JObject document = YamlConfigDocument.Parse(
            "a: \"1\"\nb: 1\nc: 'true'\nd: true\ne:\n",
            "inline.yml");

        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, document["a"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, document["b"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, document["c"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Boolean, document["d"]!.Type);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, document["e"]!.Type);
    }

    [Fact]
    public void MalformedYaml_IsASyntaxError()
    {
        CliException exception = Assert.Throws<CliException>(
            () => YamlConfigDocument.Parse("include: [unterminated\n", "inline.yml"));

        Assert.Equal(CliErrorCodes.YamlSyntax, exception.Errors[0].Code);
    }

    [Fact]
    public void MissingConfigFile_IsReportedAsAMissingFile()
    {
        CliException exception = Assert.Throws<CliException>(
            () => PackageConfigLoader.Load(FixturePath("valid", "does-not-exist.yml")));

        Assert.Equal(CliErrorCodes.FileNotFound, exception.Errors[0].Code);
    }

    [Fact]
    public void SchemaValidFileWithAnInvalidPackageId_IsRejectedByTheModel()
    {
        using var fixture = new CliFixture();
        fixture.WriteConfig("""
            schemaVersion: 1
            packageId: NotKebabCase
            inputRoot: ./game-data
            include:
              - "**/*"
            compression:
              kind: none
            groups: []
            """);

        Exception exception = Record.Exception(() => PackageConfigLoader.Load(fixture.ConfigPath))!;

        // The document is well-formed YAML and the packageId is a string, so this only fails once the schema
        // pattern or the model's kebab-case rule is applied.
        Assert.Contains("package", ErrorCodeOf(exception), StringComparison.Ordinal);
    }

    private static string ErrorCodeOf(Exception exception)
    {
        return exception switch
        {
            CliException cli => cli.Errors[0].Code,
            GamePatchKit.Packager.PackageException package => package.Errors[0].Code,
            _ => throw new InvalidOperationException("Unexpected exception: " + exception),
        };
    }

    private static string FixturePath(string kind, string fileName)
    {
        return Path.Combine(CliFixture.RepositoryRoot(), "tests", "fixtures", "gamepatchkit-yml", kind, fileName);
    }
}
