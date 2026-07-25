using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.Manifests;

public class TestReleaseManifest
{
    private const string HashA = "30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6";
    private const string HashB = "cd43d82673a50ede733a204a3db6997dd349fb9963ee34950682433e97b1b512";
    private const string DataVersion = "v1-30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6";

    private static string SingleFileManifestJson(string artifactHash = HashA, string fileHash = HashA)
    {
        return $@"{{
            ""schemaVersion"": 1,
            ""packageId"": ""sample-game-client-data"",
            ""dataVersion"": ""{DataVersion}"",
            ""compactVersion"": 0,
            ""groups"": [ {{ ""name"": ""core"", ""required"": true }} ],
            ""artifacts"": [
                {{
                    ""kind"": ""file"",
                    ""compression"": {{ ""kind"": ""none"" }},
                    ""payload"": {{
                        ""kind"": ""single"",
                        ""path"": ""sample-game-client-data/artifacts/files/{artifactHash}/content"",
                        ""size"": 14,
                        ""artifactHash"": ""{artifactHash}""
                    }}
                }}
            ],
            ""files"": [
                {{
                    ""path"": ""data/config.json"",
                    ""group"": ""core"",
                    ""size"": 14,
                    ""fileHash"": ""{fileHash}"",
                    ""source"": {{ ""kind"": ""file"", ""artifactHash"": ""{artifactHash}"" }}
                }}
            ]
        }}";
    }

    [Fact]
    public void ParsesAndValidatesSingleFileManifest()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());

        bool parsed = ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> parseErrors);
        Assert.True(parsed, string.Join("; ", parseErrors));

        ValidationResult result = ManifestValidator.Validate(manifest!);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void RejectsDuplicateGroupNames()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        ((JArray)json["groups"]!).Add(JObject.Parse(@"{ ""name"": ""core"", ""required"": false }"));

        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);
        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.DuplicateGroupName);
    }

    [Fact]
    public void RejectsUnknownFileGroup()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        ((JObject)((JArray)json["files"]!)[0]!)["group"] = "does-not-exist";

        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);
        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.UnknownFileGroup);
    }

    [Fact]
    public void RejectsArtifactPathNotMatchingContentAddressedExpectation()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        ((JObject)((JArray)json["artifacts"]!)[0]!["payload"]!)["path"] = "sample-game-client-data/artifacts/files/wrong/content";

        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);
        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.ArtifactPathMismatch);
    }

    [Fact]
    public void RejectsUnreferencedFileArtifact()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        ((JArray)json["files"]!).Clear();

        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);
        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.UnreferencedFileArtifact);
    }

    [Fact]
    public void AllowsFileArtifactSharedByMultipleFiles()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        var secondFile = JObject.Parse(@"{
            ""path"": ""data/config2.json"",
            ""group"": ""core"",
            ""size"": 14,
            ""fileHash"": """ + HashA + @""",
            ""source"": { ""kind"": ""file"", ""artifactHash"": """ + HashA + @""" }
        }");
        ((JArray)json["files"]!).Add(secondFile);

        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);
        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void RejectsBundleEntryReferencedByMoreThanOneFile()
    {
        string json = $@"{{
            ""schemaVersion"": 1,
            ""packageId"": ""sample-game-client-data"",
            ""dataVersion"": ""{DataVersion}"",
            ""compactVersion"": 0,
            ""groups"": [ {{ ""name"": ""maps"", ""required"": true }} ],
            ""artifacts"": [
                {{
                    ""kind"": ""bundle"",
                    ""group"": ""maps"",
                    ""path"": ""sample-game-client-data/artifacts/bundles/maps/{HashB}.tar"",
                    ""size"": 28,
                    ""artifactHash"": ""{HashB}"",
                    ""compression"": {{ ""kind"": ""none"" }},
                    ""entries"": [ {{ ""path"": ""maps/level1.bin"" }} ]
                }}
            ],
            ""files"": [
                {{ ""path"": ""maps/level1.bin"", ""group"": ""maps"", ""size"": 14, ""fileHash"": ""{HashA}"", ""source"": {{ ""kind"": ""bundleEntry"", ""artifactHash"": ""{HashB}"", ""entryPath"": ""maps/level1.bin"" }} }},
                {{ ""path"": ""maps/level1-alias.bin"", ""group"": ""maps"", ""size"": 14, ""fileHash"": ""{HashA}"", ""source"": {{ ""kind"": ""bundleEntry"", ""artifactHash"": ""{HashB}"", ""entryPath"": ""maps/level1.bin"" }} }}
            ]
        }}";

        ReleaseManifest.TryParse((JObject)JToken.Parse(json), out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> parseErrors);
        Assert.NotNull(manifest);

        ValidationResult result = ManifestValidator.Validate(manifest!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.DuplicateBundleEntryReference);
    }

    [Fact]
    public void RoundTripsThroughCanonicalWriter()
    {
        var json = (JObject)JToken.Parse(SingleFileManifestJson());
        ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out _);

        byte[] firstPass = GamePatchKit.Core.Json.CanonicalJsonWriter.Write(manifest!.ToJson());

        ReleaseManifest.TryParse((JObject)JToken.Parse(SingleFileManifestJson()), out ReleaseManifest? reparsed, out _);
        byte[] secondPass = GamePatchKit.Core.Json.CanonicalJsonWriter.Write(reparsed!.ToJson());

        Assert.Equal(firstPass, secondPass);
    }
}
