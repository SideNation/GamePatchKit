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
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        var timings = new StageTimings();

        byte[] manifestBytes = await timings.MeasureAsync(
            "read-manifest",
            () => ReleaseManifestReader.ReadPublishedBytesAsync(outputRoot, packageId, manifestHash, cancellationToken))
            .ConfigureAwait(false);

        // No signature verifier and therefore no --require-signature: until step 11 supplies primitive
        // verification and a trusted-key set, the CLI cannot tell a real signature from a forged one, and an
        // option that claims to gate on signing while only checking that a file exists is worse than no
        // option at all. The reported signature.state says exactly what was and was not checked.
        ReleaseVerifyReport report = await timings.MeasureAsync(
            "verify",
            () => new ReleaseVerifier().VerifyAsync(
                new ReleaseVerifyRequest(outputRoot, packageId, manifestHash, manifestBytes),
                cancellationToken)).ConfigureAwait(false);

        return new CommandOutcome(BuildResult(report, timings), BuildTextLines(report, timings));
    }

    private static JObject BuildResult(ReleaseVerifyReport report, StageTimings timings)
    {
        var signature = new JObject
        {
            // absent | present. 'present' means a valid signature document was found and nothing more - the
            // 64 bytes were not checked against any key, so it is not evidence the release was signed by
            // anyone. 'verified' becomes reachable when step 11 supplies primitive verification.
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
