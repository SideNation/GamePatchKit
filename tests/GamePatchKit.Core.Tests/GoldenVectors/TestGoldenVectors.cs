using System;
using System.Text;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.GoldenVectors;

public class TestGoldenVectors
{
    private const string PlaceholderDataVersion = "v1-0000000000000000000000000000000000000000000000000000000000000000";

    public static IEnumerable<object[]> Vectors()
    {
        yield return new object[] { "single-file", (Func<string, ReleaseManifest>)GoldenVectorManifests.SingleFile };
        yield return new object[] { "multipart-file", (Func<string, ReleaseManifest>)GoldenVectorManifests.MultipartFile };
        yield return new object[] { "bundle-entry", (Func<string, ReleaseManifest>)GoldenVectorManifests.BundleEntry };
        yield return new object[] { "mixed", (Func<string, ReleaseManifest>)GoldenVectorManifests.Mixed };
    }

    public static IEnumerable<object[]> VectorNames()
    {
        foreach (object[] vector in Vectors())
        {
            yield return new object[] { vector[0] };
        }
    }

    // Runs the vectors through the identity API the rest of the system uses, so the fixtures pin
    // ReleaseIdentity's real output rather than a computation written alongside them.
    [Theory]
    [MemberData(nameof(Vectors))]
    public void ProducesExpectedCanonicalBytesAndHashes(string vectorName, Func<string, ReleaseManifest> build)
    {
        ReleaseManifest draft = build(PlaceholderDataVersion);

        byte[] identityBytes = ReleaseIdentity.ComputeIdentityBytes(draft);
        FinalizedManifest finalized = ReleaseIdentity.Finalize(draft, CompactVersionRule.Initial);

        ValidationResult validation = ManifestValidator.Validate(finalized.Manifest);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));

        Assert.Equal(GoldenVectorFixtures.ReadBytes(vectorName, "identity.canonical.json"), identityBytes);
        Assert.Equal(GoldenVectorFixtures.ReadText(vectorName, "data-version.txt"), finalized.DataVersion);
        Assert.Equal(GoldenVectorFixtures.ReadBytes(vectorName, "manifest.canonical.json"), finalized.GetCanonicalBytes());
        Assert.Equal(GoldenVectorFixtures.ReadText(vectorName, "manifest-hash.txt"), finalized.ManifestHash);
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void VerifiesFixtureBytesAgainstTheFixtureManifestHash(string vectorName)
    {
        byte[] manifestBytes = GoldenVectorFixtures.ReadBytes(vectorName, "manifest.canonical.json");
        string manifestHash = GoldenVectorFixtures.ReadText(vectorName, "manifest-hash.txt");

        Assert.True(ReleaseIdentity.VerifyManifestHash(manifestBytes, manifestHash).IsValid);
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void ParsingFixtureAndRewritingReproducesIdenticalBytes(string vectorName)
    {
        byte[] fixtureBytes = GoldenVectorFixtures.ReadBytes(vectorName, "manifest.canonical.json");
        var json = (JObject)JToken.Parse(Encoding.UTF8.GetString(fixtureBytes));

        bool parsed = ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> errors);
        Assert.True(parsed, string.Join("; ", errors));

        byte[] rewritten = CanonicalJsonWriter.Write(manifest!.ToJson());

        Assert.Equal(fixtureBytes, rewritten);
    }

    [Fact]
    public void ReorderingObjectKeysAndWhitespaceStillCanonicalizesIdentically()
    {
        byte[] fixtureBytes = GoldenVectorFixtures.ReadBytes("single-file", "manifest.canonical.json");

        // Same content as the fixture, but with different object key order and added whitespace -
        // canonicalization must normalize both away while preserving the fixed array order contract.
        const string reordered = @"{
            ""schemaVersion"" : 1 ,
            ""groups"": [ { ""required"": true, ""name"": ""core"" } ],
            ""packageId"": ""golden-single-file"",
            ""compactVersion"": 0,
            ""dataVersion"": ""v1-a382501f1e5bed87b530c56cf9c1b1a63e363fd328ea7882bf66553c248421aa"",
            ""files"": [
                {
                    ""source"": { ""kind"": ""file"", ""artifactHash"": ""30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6"" },
                    ""size"": 14,
                    ""path"": ""data/config.json"",
                    ""fileHash"": ""30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6"",
                    ""group"": ""core""
                }
            ],
            ""artifacts"": [
                {
                    ""payload"": {
                        ""size"": 14,
                        ""kind"": ""single"",
                        ""artifactHash"": ""30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6"",
                        ""path"": ""golden-single-file/artifacts/files/30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6/content""
                    },
                    ""kind"": ""file"",
                    ""compression"": { ""kind"": ""none"" }
                }
            ]
        }";

        var json = (JObject)JToken.Parse(reordered);
        bool parsed = ReleaseManifest.TryParse(json, out ReleaseManifest? manifest, out IReadOnlyList<GamePatchKitError> errors);
        Assert.True(parsed, string.Join("; ", errors));

        byte[] rewritten = CanonicalJsonWriter.Write(manifest!.ToJson());

        Assert.Equal(fixtureBytes, rewritten);
    }

    [Fact]
    public void RejectsFilesArrayOutOfCanonicalOrder()
    {
        ReleaseManifest manifest = GoldenVectorManifests.Mixed(PlaceholderDataVersion);
        var reversedFiles = manifest.Files.Reverse().ToList();
        var outOfOrder = new ReleaseManifest(1, manifest.PackageId, manifest.DataVersion, manifest.CompactVersion, manifest.Groups, manifest.Artifacts, reversedFiles);

        ValidationResult result = ManifestValidator.Validate(outOfOrder);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.UnsortedFiles);
    }

    [Fact]
    public void RejectsNonNfcFilePath()
    {
        // "e\u0301" (e + combining acute accent, NFD) instead of the precomposed "é" (NFC) -
        // built from an explicit escape so the source is unambiguous about which form is used.
        string nfdPath = "data/café.json";
        ReleaseManifest baseline = GoldenVectorManifests.SingleFile(PlaceholderDataVersion);
        var nfdFile = new ManifestFileEntry(nfdPath, "core", 14, GoldenVectorManifests.HashA, new FileSource.FileReference(GoldenVectorManifests.HashA));
        var withNfdPath = new ReleaseManifest(1, baseline.PackageId, baseline.DataVersion, baseline.CompactVersion, baseline.Groups, baseline.Artifacts, new List<ManifestFileEntry> { nfdFile });

        ValidationResult result = ManifestValidator.Validate(withNfdPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == ManifestErrorCodes.NonCanonicalPath);
    }
}
