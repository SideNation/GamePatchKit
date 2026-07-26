using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

// Loading the private key is the CLI's job and the CLI's alone: the Packager sign API takes a signer, never a
// path or an environment variable name. Nothing here puts key material into the result, the text output or an
// error message - the key only ever appears as its derived keyId.
internal static class SignCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string outputRoot = args.GetRequired("output-root");
        string packageId = args.GetRequired("package-id");
        string manifestHash = args.GetRequired("manifest-hash");
        string? keyFile = args.GetOptional("key-file");
        string? keyEnvironmentVariable = args.GetOptional("key-env");
        bool dryRun = args.GetFlag("dry-run");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        var timings = new StageTimings();
        IManifestSigner signer = await timings.MeasureAsync(
            "load-key",
            () => Task.FromResult<IManifestSigner>(LoadSigner(keyFile, keyEnvironmentVariable))).ConfigureAwait(false);

        SignReleaseResult result = await timings.MeasureAsync(
            "sign",
            () => ReleaseSigner.SignAsync(
                new SignReleaseRequest(outputRoot, packageId, manifestHash, signer, dryRun),
                cancellationToken)).ConfigureAwait(false);

        return new CommandOutcome(
            BuildResult(packageId, result, timings),
            BuildTextLines(packageId, result, dryRun, timings));
    }

    private static Ed25519ManifestSigner LoadSigner(string? keyFile, string? keyEnvironmentVariable)
    {
        if ((keyFile == null) == (keyEnvironmentVariable == null))
        {
            throw new CliException(
                CliErrorCodes.InvalidArguments,
                "sign needs exactly one of '--key-file <path>' and '--key-env <name>'.");
        }

        if (keyFile != null)
        {
            if (!File.Exists(keyFile))
            {
                // The path is named, the contents never are.
                throw new CliException(CliErrorCodes.FileNotFound, "The signing key file does not exist.", Path.GetFileName(keyFile));
            }

            return Ed25519ManifestSigner.FromBase64UrlPrivateKey(File.ReadAllText(keyFile));
        }

        string? value = Environment.GetEnvironmentVariable(keyEnvironmentVariable!);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException(
                CliErrorCodes.InvalidArguments,
                $"Environment variable '{keyEnvironmentVariable}' is not set or is empty.");
        }

        return Ed25519ManifestSigner.FromBase64UrlPrivateKey(value);
    }

    private static JObject BuildResult(string packageId, SignReleaseResult result, StageTimings timings)
    {
        return new JObject
        {
            ["packageId"] = packageId,
            ["manifestHash"] = result.ManifestHash,
            ["keyId"] = result.KeyId,
            ["algorithm"] = result.Signature.Algorithm,

            // false means an identical manifest.sig was already published and was verified and reused.
            ["created"] = result.Created,
            ["signaturePath"] = PackageLayout.SignaturePath(packageId, result.ManifestHash),
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(
        string packageId,
        SignReleaseResult result,
        bool dryRun,
        StageTimings timings)
    {
        var lines = new List<string>
        {
            $"sign {packageId}{(dryRun ? " (dry run, nothing published)" : string.Empty)}",
            $"  manifestHash: {result.ManifestHash}",
            $"  keyId:        {result.KeyId}",
            $"  algorithm:    {result.Signature.Algorithm}",
            $"  created:      {(result.Created ? "true" : "false (existing signature verified and reused)")}",
            $"  path:         {PackageLayout.SignaturePath(packageId, result.ManifestHash)}",
            "  durations:",
        };

        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));
        return lines;
    }
}
