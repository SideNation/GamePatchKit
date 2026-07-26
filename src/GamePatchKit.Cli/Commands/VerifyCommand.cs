using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

internal static class VerifyCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string outputRoot = args.GetRequired("output-root");
        string packageId = args.GetRequired("package-id");
        string manifestHash = args.GetRequired("manifest-hash");
        IReadOnlyList<string> trustedKeys = args.GetAll("trusted-key");
        bool requireSignature = args.GetFlag("require-signature");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        // No trusted keys means no verifier: the reported signature.state says exactly what was and was not
        // checked rather than a --require-signature option pretending to gate on signing while only checking
        // that a file exists.
        IManifestSignatureVerifier? signatureVerifier = trustedKeys.Count == 0
            ? null
            : TrustedKeySignatureVerifier.FromBase64UrlPublicKeys(trustedKeys);

        var timings = new StageTimings();

        byte[] manifestBytes = await timings.MeasureAsync(
            "read-manifest",
            () => ReleaseManifestReader.ReadPublishedBytesAsync(outputRoot, packageId, manifestHash, cancellationToken))
            .ConfigureAwait(false);

        ReleaseVerifyReport report = await timings.MeasureAsync(
            "verify",
            () => new ReleaseVerifier().VerifyAsync(
                new ReleaseVerifyRequest(outputRoot, packageId, manifestHash, manifestBytes, signatureVerifier, requireSignature),
                cancellationToken)).ConfigureAwait(false);

        return new CommandOutcome(BuildResult(report, timings), BuildTextLines(report, timings));
    }

    private static JObject BuildResult(ReleaseVerifyReport report, StageTimings timings)
    {
        var signature = new JObject
        {
            // absent | present | verified. 'present' means a valid signature document was found but no
            // '--trusted-key' was given, so the 64 bytes were not checked against any key and this is not
            // evidence the release was signed by anyone. 'verified' means a '--trusted-key' matched the
            // signature's keyId and the bytes checked out cryptographically.
            ["state"] = report.SignatureState.ToString().ToLowerInvariant(),
        };

        if (report.KeyId != null)
        {
            signature["keyId"] = report.KeyId;
        }

        return new JObject
        {
            ["packageId"] = report.PackageId,
            ["identity"] = ReleaseReporting.Identity(report.DataVersion, report.CompactVersion, report.ManifestHash),
            ["totals"] = ReleaseReporting.Totals(report.Totals),
            ["signature"] = signature,
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(ReleaseVerifyReport report, StageTimings timings)
    {
        var lines = new List<string> { $"verify {report.PackageId}: ok" };

        lines.AddRange(ReleaseReporting.IdentityLines(report.DataVersion, report.CompactVersion, report.ManifestHash));
        lines.AddRange(ReleaseReporting.TotalsLines(report.Totals));
        lines.Add($"  signature: {report.SignatureState.ToString().ToLowerInvariant()}"
            + (report.KeyId == null ? string.Empty : $" ({report.KeyId})"));
        lines.Add("  durations:");
        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));

        return lines;
    }
}
