using GamePatchKit.Cli.Configuration;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

internal static class PackageCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string configPath = args.GetOptional("config") ?? PackageConfigLoader.DefaultFileName;
        string outputRoot = args.GetRequired("output-root");
        string? previousManifestHash = args.GetOptional("previous");
        string? sourceRevision = args.GetOptional("source-revision");
        bool writeCompressedManifest = args.GetFlag("compressed-manifest");
        bool dryRun = args.GetFlag("dry-run");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        var timings = new StageTimings();
        PackageConfig config = await timings.MeasureAsync(
            "config",
            () => Task.FromResult(PackageConfigLoader.Load(configPath))).ConfigureAwait(false);

        // The previous release is identified by manifestHash alone: an incremental package builds on a release
        // that is already published in this output tree, so there is nothing else to point at.
        PreviousRelease? previous = previousManifestHash == null
            ? null
            : await timings.MeasureAsync(
                "previous-manifest",
                async () => new PreviousRelease(
                    await ReleaseManifestReader.ReadPublishedBytesAsync(
                        outputRoot,
                        config.PackageId,
                        previousManifestHash,
                        cancellationToken).ConfigureAwait(false),
                    previousManifestHash)).ConfigureAwait(false);

        FilePackageResult result = await timings.MeasureAsync(
            "package",
            () => new FilePackageBuilder().BuildAsync(
                new FilePackageRequest(config, outputRoot, previous, writeCompressedManifest, sourceRevision, dryRun),
                cancellationToken)).ConfigureAwait(false);

        ReleaseTotals totals = ReleaseTotals.Compute(result.Release.Manifest);

        // What a client with nothing installed downloads for the required groups - the number an operator
        // compares between releases to see whether a first install got more expensive.
        DownloadPlan firstInstall = ReleaseReporting.Estimate(
            result.Release.Manifest,
            DownloadPlanner.RequiredGroupNames(result.Release.Manifest),
            Array.Empty<LocalFileState>());

        return new CommandOutcome(
            BuildResult(config, result, totals, firstInstall, timings),
            BuildTextLines(config, result, totals, firstInstall, dryRun, timings));
    }

    private static JObject BuildResult(
        PackageConfig config,
        FilePackageResult result,
        ReleaseTotals totals,
        DownloadPlan firstInstall,
        StageTimings timings)
    {
        PackageBuildReport report = result.Report;

        return new JObject
        {
            ["packageId"] = config.PackageId,
            ["reusedManifest"] = result.ReusedManifest,
            ["identity"] = ReleaseReporting.Identity(
                result.Release.DataVersion,
                result.Release.CompactVersion,
                result.Release.ManifestHash),
            ["totals"] = ReleaseReporting.Totals(totals),
            ["changes"] = new JObject
            {
                ["added"] = report.AddedFiles.Count,
                ["changed"] = report.ChangedFiles.Count,
                ["deleted"] = report.DeletedFiles.Count,
                ["groupMoved"] = report.MovedGroupFiles.Count,
            },
            ["artifacts"] = new JObject
            {
                ["createdFileArtifactCount"] = report.CreatedFileArtifactCount,
                ["createdFileArtifactBytes"] = report.CreatedFileArtifactBytes,
                ["reusedFileArtifactCount"] = report.ReusedFileArtifactCount,
                ["createdBundleArtifactCount"] = report.CreatedBundleArtifactCount,
                ["createdBundleArtifactBytes"] = report.CreatedBundleArtifactBytes,
                ["reusedBundleArtifactCount"] = report.ReusedBundleArtifactCount,
            },
            ["estimatedFirstInstall"] = ReleaseReporting.DownloadEstimate(firstInstall),
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(
        PackageConfig config,
        FilePackageResult result,
        ReleaseTotals totals,
        DownloadPlan firstInstall,
        bool dryRun,
        StageTimings timings)
    {
        PackageBuildReport report = result.Report;
        var lines = new List<string>
        {
            $"package {config.PackageId}{(dryRun ? " (dry run, nothing published)" : string.Empty)}",
        };

        lines.AddRange(ReleaseReporting.IdentityLines(
            result.Release.DataVersion,
            result.Release.CompactVersion,
            result.Release.ManifestHash));
        lines.Add($"  reusedManifest: {(result.ReusedManifest ? "true" : "false")}");
        lines.AddRange(ReleaseReporting.TotalsLines(totals));
        lines.Add($"  changes: {report.AddedFiles.Count} added, {report.ChangedFiles.Count} changed, "
            + $"{report.DeletedFiles.Count} deleted, {report.MovedGroupFiles.Count} group-moved");
        lines.Add($"  file artifacts:   {report.CreatedFileArtifactCount} created "
            + $"({report.CreatedFileArtifactBytes} bytes), {report.ReusedFileArtifactCount} reused");
        lines.Add($"  bundle artifacts: {report.CreatedBundleArtifactCount} created "
            + $"({report.CreatedBundleArtifactBytes} bytes), {report.ReusedBundleArtifactCount} reused");
        lines.Add("  estimated first install (required groups):");
        lines.AddRange(ReleaseReporting.DownloadEstimateLines(firstInstall).Select(line => "  " + line));
        lines.Add("  durations:");
        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));

        return lines;
    }
}
