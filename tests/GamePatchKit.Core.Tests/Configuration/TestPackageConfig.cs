using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.Configuration;

public class TestPackageConfig
{
    private const string MinimalJson = @"{
        ""schemaVersion"": 1,
        ""packageId"": ""sample-game-client-data"",
        ""inputRoot"": ""./game-data"",
        ""include"": [""**/*.json""],
        ""compression"": { ""kind"": ""none"" },
        ""groups"": []
    }";

    private const string GroupsAndCompressionJson = @"{
        ""schemaVersion"": 1,
        ""packageId"": ""sample-game-client-data"",
        ""inputRoot"": ""./game-data"",
        ""include"": [""**/*""],
        ""exclude"": [""**/*.tmp""],
        ""maxArtifactBytes"": 10485760,
        ""defaultArtifactMode"": ""file"",
        ""compression"": { ""kind"": ""zstd"", ""codecId"": ""zstd"" },
        ""groups"": [
            { ""name"": ""core"", ""include"": [""core/**/*""], ""artifactMode"": ""file"", ""required"": true },
            { ""name"": ""maps"", ""include"": [""maps/**/*""], ""artifactMode"": ""bundle"", ""required"": false, ""compression"": { ""kind"": ""none"" } }
        ]
    }";

    [Fact]
    public void ParsesMinimalValidConfig()
    {
        var json = (JObject)JToken.Parse(MinimalJson);

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal("sample-game-client-data", config!.PackageId);
        Assert.Equal(PackageConfig.DefaultMaxArtifactBytes, config.MaxArtifactBytes);
        Assert.Equal(ArtifactMode.File, config.DefaultArtifactMode);
        Assert.Empty(config.Groups);
        Assert.True(PackageConfigValidator.Validate(config).IsValid);
    }

    [Fact]
    public void ParsesGroupsAndCompressionConfig()
    {
        var json = (JObject)JToken.Parse(GroupsAndCompressionJson);

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal(2, config!.Groups.Count);
        Assert.Equal(CompressionKind.Zstd, config.Compression);
        Assert.Equal(ArtifactMode.Bundle, config.Groups[1].ArtifactMode);
        Assert.Equal(CompressionKind.None, config.Groups[1].Compression);
        Assert.Null(config.Groups[0].Compression);
        Assert.True(PackageConfigValidator.Validate(config).IsValid);
    }

    [Fact]
    public void RejectsEmptyInclude()
    {
        var json = (JObject)JToken.Parse(MinimalJson);
        json["include"] = new JArray();

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Contains(errors, e => e.Code == PackageConfigErrorCodes.EmptyInclude);
    }

    [Fact]
    public void RejectsInvalidPackageId()
    {
        var json = (JObject)JToken.Parse(MinimalJson);
        json["packageId"] = "Not_Kebab_Case";

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Contains(errors, e => e.Code == PackageConfigErrorCodes.InvalidPackageId);
    }

    [Fact]
    public void RejectsReservedDefaultGroupName()
    {
        var json = (JObject)JToken.Parse(GroupsAndCompressionJson);
        ((JObject)json["groups"]![0]!)["name"] = "default";

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Contains(errors, e => e.Code == PackageConfigErrorCodes.ReservedGroupName);
    }

    [Fact]
    public void ValidatorRejectsDuplicateGroupNames()
    {
        var json = (JObject)JToken.Parse(GroupsAndCompressionJson);
        ((JObject)json["groups"]![1]!)["name"] = "core";

        bool ok = PackageConfig.TryParse(json, out PackageConfig? config, out IReadOnlyList<GamePatchKitError> parseErrors);
        Assert.True(ok, string.Join("; ", parseErrors));

        ValidationResult result = PackageConfigValidator.Validate(config!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PackageConfigErrorCodes.DuplicateGroupName);
    }

    [Fact]
    public void RoundTripsThroughCanonicalWriter()
    {
        var json = (JObject)JToken.Parse(GroupsAndCompressionJson);
        PackageConfig.TryParse(json, out PackageConfig? config, out _);

        byte[] firstPass = CanonicalJsonWriter.Write(config!.ToJson());

        PackageConfig.TryParse((JObject)JToken.Parse(GroupsAndCompressionJson), out PackageConfig? reparsed, out _);
        byte[] secondPass = CanonicalJsonWriter.Write(reparsed!.ToJson());

        Assert.Equal(firstPass, secondPass);
    }
}
