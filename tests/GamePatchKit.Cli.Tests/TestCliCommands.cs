using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

public class TestCliCommands
{
    // Any 32 bytes are a valid Ed25519 private key; this one is fixed so the derived keyId is stable.
    private const string TestPrivateKeyBase64Url = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA";

    [Fact]
    public void Package_ReportsIdentityTotalsAndMetrics()
    {
        using var fixture = NewPackage();

        CliRun run = fixture.RunExpectingSuccess("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        JObject result = run.Result();
        Assert.Equal(fixture.PackageId, (string?)result["packageId"]);
        Assert.StartsWith("v1-", (string?)result["identity"]!["dataVersion"]);
        Assert.Equal(0, (long?)result["identity"]!["compactVersion"]);
        Assert.Equal(64, ((string?)result["identity"]!["manifestHash"])!.Length);
        Assert.Equal(3, (int?)result["totals"]!["fileCount"]);
        Assert.Equal(3, (int?)result["changes"]!["added"]);
        Assert.Equal(0, (int?)result["changes"]!["deleted"]);

        // Per-group counts and the first-install estimate are the PRD observability numbers; a result without
        // them is not a package report.
        JArray groups = (JArray)result["totals"]!["groups"]!;
        Assert.Equal(new[] { "core", "maps" }, groups.Select(group => (string?)group["name"]));
        Assert.True((long?)result["estimatedFirstInstall"]!["downloadBytes"] > 0);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    [Fact]
    public void Package_JsonEnvelopeCarriesTheCommandOutcome()
    {
        using var fixture = NewPackage();

        CliRun run = fixture.RunExpectingSuccess("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        JObject envelope = run.Json();
        Assert.Equal("package", (string?)envelope["command"]);
        Assert.True((bool?)envelope["ok"]);
        Assert.Equal(0, (int?)envelope["exitCode"]);
        Assert.False((bool?)envelope["dryRun"]);

        // Canonical JSON: sorted keys, no insignificant whitespace, so a CI job can diff two runs directly.
        Assert.StartsWith("{\"command\":\"package\",\"dryRun\":false,\"exitCode\":0,\"ok\":true,\"result\":{", run.StandardOutput);
    }

    [Fact]
    public void Package_DryRun_CreatesNoFileAndStillReportsTheRealIdentity()
    {
        using var fixture = NewPackage();
        IReadOnlyDictionary<string, string> before = fixture.OutputTree();

        CliRun dryRun = fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--dry-run", "--json");

        Assert.Equal(before, fixture.OutputTree());
        Assert.True((bool?)dryRun.Json()["dryRun"]);

        CliRun real = fixture.RunExpectingSuccess("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");
        Assert.Equal(dryRun.Result()["identity"], real.Result()["identity"]);
        Assert.NotEqual(before, fixture.OutputTree());
    }

    [Fact]
    public void Package_Incremental_ReusesArtifactsAndReportsTheChangedFile()
    {
        using var fixture = NewPackage();
        string baselineHash = ManifestHash(Package(fixture));
        fixture.WriteSource("core/config.json", "changed configuration");

        CliRun incremental = fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--previous", baselineHash, "--json");

        JObject result = incremental.Result();
        Assert.Equal(1, (int?)result["changes"]!["changed"]);
        Assert.Equal(0, (int?)result["changes"]!["added"]);
        Assert.True((int?)result["artifacts"]!["reusedBundleArtifactCount"] > 0);
        Assert.NotEqual(baselineHash, (string?)result["identity"]!["manifestHash"]);
    }

    [Fact]
    public void Verify_PublishedRelease_Succeeds()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        CliRun run = fixture.RunExpectingSuccess(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");

        Assert.Equal(manifestHash, (string?)run.Result()["identity"]!["manifestHash"]);
        Assert.Equal("absent", (string?)run.Result()["signature"]!["state"]);
    }

    [Fact]
    public void Verify_CorruptedArtifactPayload_IsAnIntegrityError()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        CorruptFirstStoredObject(fixture);

        CliRun run = fixture.Run(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");

        Assert.Equal(ExitCode.IntegrityError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, run.FirstErrorCode());
        Assert.False((bool?)run.Json()["ok"]);
    }

    [Fact]
    public void Verify_ForgedSignature_IsReportedAsUnverifiedAndHasNoGateToPass()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        string keyId = "ed25519-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(new byte[32])).ToLowerInvariant();
        string signature = Convert.ToBase64String(Enumerable.Repeat((byte)0xff, 64).ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string signaturePath = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash));
        Directory.CreateDirectory(Path.GetDirectoryName(signaturePath)!);
        File.WriteAllText(
            signaturePath,
            $"{{\"algorithm\":\"Ed25519\",\"keyId\":\"{keyId}\",\"schemaVersion\":1,\"signature\":\"{signature}\"}}");

        CliRun run = fixture.RunExpectingSuccess(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");

        // The forgery is a structurally valid document, so verify reports it as found and unchecked. What it
        // must never do is offer an option that treats that as proof the release was signed.
        Assert.Equal("present", (string?)run.Result()["signature"]!["state"]);
        Assert.Equal(
            ExitCode.InputError,
            fixture.Run(
                "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
                "--manifest-hash", manifestHash, "--require-signature", "--json").ExitCode);
    }

    [Fact]
    public void ConfigWithAnUnrepresentableInteger_IsAnInputErrorRatherThanACrash()
    {
        using var fixture = NewPackage();
        fixture.WriteConfig($"""
            schemaVersion: -9223372036854775808
            packageId: {fixture.PackageId}
            inputRoot: {fixture.SourceRoot.Replace('\\', '/')}
            include:
              - "**/*"
            compression:
              kind: none
            groups: []
            """);

        CliRun run = fixture.Run("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        // long.MinValue used to escape as an unhandled OverflowException: exit 134, a stack trace and no JSON
        // envelope at all. It has to be an ordinary rejected configuration.
        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.False((bool?)run.Json()["ok"]);
    }

    [Fact]
    public void Verify_UnknownManifestHash_IsAnInputError()
    {
        using var fixture = NewPackage();
        Package(fixture);

        CliRun run = fixture.Run(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", new string('0', 64), "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.ManifestNotFound, run.FirstErrorCode());
    }

    [Fact]
    public void Diff_ReportsLogicalAndPhysicalChangesSeparately()
    {
        using var fixture = NewPackage();
        string fromHash = ManifestHash(Package(fixture));
        fixture.WriteSource("core/config.json", "changed configuration");
        string toHash = ManifestHash(fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--previous", fromHash, "--json"));

        CliRun run = fixture.RunExpectingSuccess(
            "diff", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--from", fromHash, "--to", toHash, "--json");

        JObject result = run.Result();
        Assert.Equal(1, (int?)result["files"]!["contentChanged"]);
        Assert.Equal(0, (int?)result["files"]!["added"]);
        Assert.Equal(1, (int?)result["artifacts"]!["addedObjectCount"]);
        Assert.Equal(1, (int?)result["artifacts"]!["removedObjectCount"]);

        // One changed file means one file to fetch, and only its own bytes.
        Assert.Equal(1, (int?)result["estimatedUpdate"]!["missingFileCount"]);
        Assert.Equal("changed configuration".Length, (long?)result["estimatedUpdate"]!["downloadBytes"]);
    }

    [Fact]
    public void Compact_IdenticalLayout_IsASuccessfulNoOpThatCreatesNothing()
    {
        using var fixture = NewPackage();
        string sourceHash = ManifestHash(Package(fixture));
        IReadOnlyDictionary<string, string> before = fixture.OutputTree();

        CliRun run = fixture.RunExpectingSuccess(
            "compact", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--source", sourceHash, "--group", "maps", "--json");

        JObject result = run.Result();
        Assert.False((bool?)result["changed"]);
        Assert.Equal(sourceHash, (string?)result["identity"]!["manifestHash"]);
        Assert.Equal(0, (long?)result["identity"]!["compactVersion"]);
        Assert.Equal(before, fixture.OutputTree());
    }

    [Fact]
    public void Compact_AfterOverrides_AdvancesCompactVersionAndKeepsDataVersion()
    {
        using var fixture = NewPackage();
        string baselineHash = ManifestHash(Package(fixture));
        fixture.WriteSource("maps/level1.bin", "changed map bytes");
        CliRun incremental = fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--previous", baselineHash, "--json");
        string sourceHash = ManifestHash(incremental);

        CliRun run = fixture.RunExpectingSuccess(
            "compact", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--source", sourceHash, "--group", "maps", "--retained", baselineHash, "--json");

        JObject result = run.Result();
        Assert.True((bool?)result["changed"]);
        Assert.Equal((string?)incremental.Result()["identity"]!["dataVersion"], (string?)result["identity"]!["dataVersion"]);
        Assert.Equal(1, (long?)result["identity"]!["compactVersion"]);
        Assert.NotEqual(sourceHash, (string?)result["identity"]!["manifestHash"]);
    }

    [Fact]
    public void Compact_DryRun_DecidesTheCompactWithoutPublishing()
    {
        using var fixture = NewPackage();
        string baselineHash = ManifestHash(Package(fixture));
        fixture.WriteSource("maps/level1.bin", "changed map bytes");
        string sourceHash = ManifestHash(fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--previous", baselineHash, "--json"));
        IReadOnlyDictionary<string, string> before = fixture.OutputTree();

        CliRun run = fixture.RunExpectingSuccess(
            "compact", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--source", sourceHash, "--group", "maps", "--retained", baselineHash, "--dry-run", "--json");

        Assert.True((bool?)run.Result()["changed"]);
        Assert.Equal(before, fixture.OutputTree());
    }

    [Fact]
    public void Compact_WithoutAGroup_IsAnInputError()
    {
        using var fixture = NewPackage();
        string sourceHash = ManifestHash(Package(fixture));

        CliRun run = fixture.Run(
            "compact", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot,
            "--source", sourceHash, "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(CliErrorCodes.InvalidArguments, run.FirstErrorCode());
    }

    [Fact]
    public void PlanDownload_RequiredOnly_PlansOnlyTheRequiredGroups()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        CliRun run = fixture.RunExpectingSuccess(
            "plan-download", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--required-only", "--json");

        JObject result = run.Result();
        Assert.Equal(new[] { "core" }, ((JArray)result["groups"]!).Select(group => (string?)group));
        Assert.Equal(1, (int?)result["plan"]!["missingFileCount"]);
        Assert.Equal(0, (int?)result["plan"]!["bundleCount"]);
    }

    [Fact]
    public void PlanDownload_ExplicitOptionalGroup_PlansTheBundleOnce()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        CliRun run = fixture.RunExpectingSuccess(
            "plan-download", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--group", "maps", "--json");

        JObject result = run.Result();
        Assert.Equal(2, (int?)result["plan"]!["missingFileCount"]);
        Assert.Equal(1, (int?)result["plan"]!["bundleCount"]);
    }

    [Fact]
    public void PlanDownload_LocalStateThatAlreadyMatches_PlansNothing()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        // The source tree is a complete installation of this release: same paths, same bytes.
        CliRun run = fixture.RunExpectingSuccess(
            "plan-download", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--required-only", "--install-root", fixture.SourceRoot, "--json");

        JObject result = run.Result();
        Assert.Equal(0, (int?)result["plan"]!["missingFileCount"]);
        Assert.Equal(0, (long?)result["plan"]!["downloadBytes"]);
        Assert.Equal(3, (int?)result["localState"]!["installedFileCount"]);
    }

    [Fact]
    public void PlanDownload_NeedsExactlyOneGroupSelection()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        string[] common =
        {
            "plan-download", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json",
        };

        Assert.Equal(ExitCode.InputError, fixture.Run(common).ExitCode);
        Assert.Equal(ExitCode.InputError, fixture.Run(common.Append("--required-only").Append("--group").Append("maps").ToArray()).ExitCode);
    }

    [Fact]
    public void Sign_DerivesTheKeyIdAndNeverEmitsTheKey()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        string keyFile = WriteKeyFile(fixture);

        CliRun run = fixture.RunExpectingSuccess(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", keyFile, "--json");

        JObject result = run.Result();
        Assert.Matches("^ed25519-[0-9a-f]{64}$", (string?)result["keyId"]);
        Assert.True((bool?)result["created"]);
        Assert.True(File.Exists(fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash))));
        AssertNoKeyMaterial(run);
    }

    [Fact]
    public void Sign_SameKeyTwice_ReusesTheExistingSignature()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        string keyFile = WriteKeyFile(fixture);
        string[] args =
        {
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", keyFile, "--json",
        };
        fixture.RunExpectingSuccess(args);

        CliRun second = fixture.RunExpectingSuccess(args);

        Assert.False((bool?)second.Result()["created"]);
    }

    [Fact]
    public void Sign_DifferentKey_IsAnIntegrityErrorAndKeepsThePublishedSignature()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        fixture.RunExpectingSuccess(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", WriteKeyFile(fixture), "--json");
        string signaturePath = fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash));
        byte[] published = File.ReadAllBytes(signaturePath);

        CliRun run = fixture.Run(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", WriteKeyFile(fixture, "other.key", OtherPrivateKey()), "--json");

        Assert.Equal(ExitCode.IntegrityError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.ImmutablePathConflict, run.FirstErrorCode());
        Assert.Equal(published, File.ReadAllBytes(signaturePath));
    }

    [Fact]
    public void Sign_DryRun_ProducesTheKeyIdWithoutWritingTheSignature()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        CliRun run = fixture.RunExpectingSuccess(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", WriteKeyFile(fixture), "--dry-run", "--json");

        Assert.False(File.Exists(fixture.OutputPath(PackageLayout.SignaturePath(fixture.PackageId, manifestHash))));
        Assert.Matches("^ed25519-[0-9a-f]{64}$", (string?)run.Result()["keyId"]);
        AssertNoKeyMaterial(run);
    }

    [Fact]
    public void Sign_KeyFromAnEnvironmentVariable_IsAcceptedWithoutNamingItsValue()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        const string variableName = "GPK_TEST_SIGNING_KEY";
        Environment.SetEnvironmentVariable(variableName, TestPrivateKeyBase64Url);

        try
        {
            CliRun run = fixture.RunExpectingSuccess(
                "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
                "--manifest-hash", manifestHash, "--key-env", variableName, "--json");

            Assert.True((bool?)run.Result()["created"]);
            AssertNoKeyMaterial(run);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Sign_MalformedKey_IsAnInputErrorThatDoesNotEchoTheKey()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));
        const string secret = "not-a-valid-key-but-still-a-secret==";
        string keyFile = WriteKeyFile(fixture, "bad.key", secret);

        CliRun run = fixture.Run(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--key-file", keyFile, "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.InvalidSigningKey, run.FirstErrorCode());
        Assert.DoesNotContain(secret, run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_NeedsExactlyOneKeySource()
    {
        using var fixture = NewPackage();
        string manifestHash = ManifestHash(Package(fixture));

        CliRun run = fixture.Run(
            "sign", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(CliErrorCodes.InvalidArguments, run.FirstErrorCode());
    }

    [Fact]
    public void InvalidYamlConfig_IsRejectedBeforeAnythingIsPublished()
    {
        using var fixture = NewPackage();
        fixture.WriteConfig("schemaVersion: 1\n---\nschemaVersion: 1\n");
        IReadOnlyDictionary<string, string> before = fixture.OutputTree();

        CliRun run = fixture.Run("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(CliErrorCodes.YamlMultipleDocuments, run.FirstErrorCode());
        Assert.Equal(before, fixture.OutputTree());
    }

    [Theory]
    [InlineData("not-a-command")]
    [InlineData("package")]
    public void UnusableArguments_AreInputErrors(string command)
    {
        using var fixture = NewPackage();

        CliRun run = fixture.Run(command, "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Equal(CliErrorCodes.InvalidArguments, run.FirstErrorCode());
    }

    [Fact]
    public void UnknownOption_IsRejectedRatherThanIgnored()
    {
        using var fixture = NewPackage();

        CliRun run = fixture.Run(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--dry-runn", "--json");

        Assert.Equal(ExitCode.InputError, run.ExitCode);
        Assert.Contains("--dry-runn", (string?)run.Json()["errors"]![0]!["message"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutJson_FailuresGoToStandardErrorAndSuccessesToStandardOutput()
    {
        using var fixture = NewPackage();

        CliRun success = fixture.RunExpectingSuccess("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot);
        CliRun failure = fixture.Run("verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", new string('0', 64));

        Assert.Contains("dataVersion:", success.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(success.StandardError);
        Assert.Empty(failure.StandardOutput);
        Assert.Contains("error: [", failure.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_IsAvailableAndSucceeds()
    {
        using var fixture = NewPackage();

        CliRun run = fixture.RunExpectingSuccess("--help");

        Assert.Contains("plan-download", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Exit codes:", run.StandardOutput, StringComparison.Ordinal);
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

    private static CliRun Package(CliFixture fixture)
    {
        return fixture.RunExpectingSuccess("package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");
    }

    private static string ManifestHash(CliRun run)
    {
        return (string)run.Result()["identity"]!["manifestHash"]!;
    }

    private static string WriteKeyFile(CliFixture fixture, string fileName = "signing.key", string? content = null)
    {
        string path = Path.Combine(Path.GetDirectoryName(fixture.ConfigPath)!, fileName);
        File.WriteAllText(path, content ?? TestPrivateKeyBase64Url);
        return path;
    }

    private static string OtherPrivateKey()
    {
        byte[] key = Enumerable.Range(1, 32).Select(value => (byte)(value + 100)).ToArray();
        return Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void AssertNoKeyMaterial(CliRun run)
    {
        Assert.DoesNotContain(TestPrivateKeyBase64Url, run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPrivateKeyBase64Url, run.StandardError, StringComparison.Ordinal);
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
