using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

// The PRD requires that the CLI and any other host share the Packager's verify and sign rules rather than
// reimplementing them. These run the same fixture through both entry points and compare what comes out -
// including the failures, since a rule that only one of the two enforces is exactly the divergence that
// matters.
public class TestCliMatchesPackagerApi
{
    private const string TestPrivateKeyBase64Url = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";

    [Fact]
    public async Task Verify_ReportsTheSameIdentityAndTotalsThroughBothEntryPoints()
    {
        using var fixture = NewPackage();
        string manifestHash = Package(fixture);

        JObject cli = fixture.RunExpectingSuccess(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json").Result();
        ReleaseVerifyReport api = await VerifyThroughApiAsync(fixture, manifestHash);

        Assert.Equal(api.DataVersion, (string?)cli["identity"]!["dataVersion"]);
        Assert.Equal(api.CompactVersion, (long?)cli["identity"]!["compactVersion"]);
        Assert.Equal(api.ManifestHash, (string?)cli["identity"]!["manifestHash"]);
        Assert.Equal(api.Totals.FileCount, (int?)cli["totals"]!["fileCount"]);
        Assert.Equal(api.Totals.FileBytes, (long?)cli["totals"]!["fileBytes"]);
        Assert.Equal(api.Totals.StoredObjectCount, (int?)cli["totals"]!["storedObjectCount"]);
        Assert.Equal(api.Totals.StoredObjectBytes, (long?)cli["totals"]!["storedObjectBytes"]);
        Assert.Equal(
            api.Totals.Groups.Select(group => (group.Name, group.Required, group.FileCount, group.FileBytes)),
            ((JArray)cli["totals"]!["groups"]!).Select(group => (
                (string)group["name"]!,
                (bool)group["required"]!,
                (int)group["fileCount"]!,
                (long)group["fileBytes"]!)));
    }

    [Fact]
    public async Task Verify_RejectsACorruptedPayloadWithTheSameErrorThroughBothEntryPoints()
    {
        using var fixture = NewPackage();
        string manifestHash = Package(fixture);
        CorruptFirstStoredObject(fixture);

        CliRun cli = fixture.Run(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");
        PackageException api = await Assert.ThrowsAsync<PackageException>(() => VerifyThroughApiAsync(fixture, manifestHash));

        Assert.Equal(api.Errors[0].Code, cli.FirstErrorCode());
        Assert.Equal(ExitCode.IntegrityError, cli.ExitCode);
    }

    [Fact]
    public async Task Sign_ProducesTheSameSignatureBytesThroughBothEntryPoints()
    {
        using var fixture = NewPackage();
        string manifestHash = Package(fixture);

        // The API signs first, so the CLI meets an already-published manifest.sig. Agreement then means the
        // CLI reused it rather than conflicting with it: a byte difference would be an immutable-path error.
        SignReleaseResult api = await ReleaseSigner.SignAsync(new SignReleaseRequest(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash,
            Ed25519ManifestSigner.FromBase64UrlPrivateKey(TestPrivateKeyBase64Url)));

        JObject cli = fixture.RunExpectingSuccess(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", WriteKeyFile(fixture), "--json").Result();

        Assert.Equal(api.KeyId, (string?)cli["keyId"]);
        Assert.False((bool?)cli["created"]);
        Assert.Equal(
            api.GetCanonicalBytes(),
            File.ReadAllBytes(fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash))));
    }

    [Fact]
    public async Task Verify_SeesTheSignatureTheOtherEntryPointPublished()
    {
        using var fixture = NewPackage();
        string manifestHash = Package(fixture);
        fixture.RunExpectingSuccess(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", WriteKeyFile(fixture), "--json");

        ReleaseVerifyReport api = await VerifyThroughApiAsync(fixture, manifestHash);
        JObject cli = fixture.RunExpectingSuccess(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json").Result();

        Assert.Equal(SignatureState.Present, api.SignatureState);
        Assert.Equal("present", (string?)cli["signature"]!["state"]);
        Assert.Equal(api.KeyId, (string?)cli["signature"]!["keyId"]);
    }

    private static async Task<ReleaseVerifyReport> VerifyThroughApiAsync(CliFixture fixture, string manifestHash)
    {
        byte[] manifestBytes = await ReleaseManifestReader.ReadPublishedBytesAsync(
            fixture.OutputRoot,
            fixture.PackageId,
            manifestHash);

        return await new ReleaseVerifier().VerifyAsync(
            new ReleaseVerifyRequest(fixture.OutputRoot, fixture.PackageId, manifestHash, manifestBytes));
    }

    private static CliFixture NewPackage()
    {
        var fixture = new CliFixture();
        fixture.WriteConfig();
        fixture.WriteSource("core/config.json", "configuration");
        fixture.WriteSource("maps/level1.bin", "map bytes");
        fixture.WriteSource("maps/level2.bin", "more map bytes");
        return fixture;
    }

    private static string Package(CliFixture fixture)
    {
        CliRun run = fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        return (string)run.Result()["identity"]!["manifestHash"]!;
    }

    private static string WriteKeyFile(CliFixture fixture)
    {
        string path = Path.Combine(Path.GetDirectoryName(fixture.ConfigPath)!, "signing.key");
        File.WriteAllText(path, TestPrivateKeyBase64Url);
        return path;
    }

    private static void CorruptFirstStoredObject(CliFixture fixture)
    {
        string artifactRoot = fixture.OutputPath($"{fixture.PackageId}/artifacts/files");
        string objectPath = Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .First();

        File.WriteAllBytes(objectPath, Enumerable.Repeat((byte)0x5a, (int)new FileInfo(objectPath).Length).ToArray());
    }
}
