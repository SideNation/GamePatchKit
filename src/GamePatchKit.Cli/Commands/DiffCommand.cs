using GamePatchKit.Core.Diff;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

// Reports the two kinds of difference separately, the way the PRD splits them: what changed about the files a
// player ends up with, and what changed about the objects storage holds. A recompressed release moves every
// object and no file; a group move is the reverse.
internal static class DiffCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string outputRoot = args.GetRequired("output-root");
        string packageId = args.GetRequired("package-id");
        string fromHash = args.GetRequired("from");
        string toHash = args.GetRequired("to");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        var timings = new StageTimings();

        ReleaseManifest from = await timings.MeasureAsync(
            "read-from",
            () => ReleaseManifestReader.ReadPublishedAsync(outputRoot, packageId, fromHash, cancellationToken)).ConfigureAwait(false);
        ReleaseManifest to = await timings.MeasureAsync(
            "read-to",
            () => ReleaseManifestReader.ReadPublishedAsync(outputRoot, packageId, toHash, cancellationToken)).ConfigureAwait(false);

        ReleaseDiff diff = await timings.MeasureAsync(
            "diff",
            () => Task.FromResult(ReleaseDiff.Compute(from, to))).ConfigureAwait(false);

        // The update cost for a client that already holds every file of `from`: exactly what the diff is
        // usually being read to find out.
        DownloadPlan update = ReleaseReporting.Estimate(
            to,
            ReleaseReporting.AllGroupNames(to),
            ReleaseReporting.AsInstalledFiles(from));

        return new CommandOutcome(
            BuildResult(packageId, fromHash, toHash, from, to, diff, update, timings),
            BuildTextLines(packageId, fromHash, toHash, from, to, diff, update, timings));
    }

    private static JObject BuildResult(
        string packageId,
        string fromHash,
        string toHash,
        ReleaseManifest from,
        ReleaseManifest to,
        ReleaseDiff diff,
        DownloadPlan update,
        StageTimings timings)
    {
        return new JObject
        {
            ["packageId"] = packageId,
            ["from"] = ReleaseReporting.Identity(from.DataVersion, from.CompactVersion, fromHash),
            ["to"] = ReleaseReporting.Identity(to.DataVersion, to.CompactVersion, toHash),
            ["files"] = new JObject
            {
                ["added"] = Count(diff, FileChangeKind.Added),
                ["contentChanged"] = Count(diff, FileChangeKind.ContentChanged),
                ["removed"] = Count(diff, FileChangeKind.Removed),
                ["groupMoved"] = Count(diff, FileChangeKind.GroupMoved),
            },
            ["artifacts"] = new JObject
            {
                ["addedObjectCount"] = Count(diff, ArtifactChangeKind.Added),
                ["addedObjectBytes"] = Bytes(diff, ArtifactChangeKind.Added),
                ["removedObjectCount"] = Count(diff, ArtifactChangeKind.Removed),
                ["removedObjectBytes"] = Bytes(diff, ArtifactChangeKind.Removed),
            },
            ["totals"] = new JObject
            {
                ["from"] = ReleaseReporting.Totals(ReleaseTotals.Compute(from)),
                ["to"] = ReleaseReporting.Totals(ReleaseTotals.Compute(to)),
            },
            ["estimatedUpdate"] = ReleaseReporting.DownloadEstimate(update),
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(
        string packageId,
        string fromHash,
        string toHash,
        ReleaseManifest from,
        ReleaseManifest to,
        ReleaseDiff diff,
        DownloadPlan update,
        StageTimings timings)
    {
        var lines = new List<string>
        {
            $"diff {packageId}",
            $"  from: {fromHash} ({from.DataVersion} / compact {from.CompactVersion})",
            $"  to:   {toHash} ({to.DataVersion} / compact {to.CompactVersion})",
            $"  files:     {Count(diff, FileChangeKind.Added)} added, "
                + $"{Count(diff, FileChangeKind.ContentChanged)} changed, "
                + $"{Count(diff, FileChangeKind.Removed)} removed, "
                + $"{Count(diff, FileChangeKind.GroupMoved)} group-moved",
            $"  artifacts: {Count(diff, ArtifactChangeKind.Added)} objects added "
                + $"({Bytes(diff, ArtifactChangeKind.Added)} bytes), "
                + $"{Count(diff, ArtifactChangeKind.Removed)} no longer referenced "
                + $"({Bytes(diff, ArtifactChangeKind.Removed)} bytes)",
            "  estimated update from a complete `from` installation:",
        };

        lines.AddRange(ReleaseReporting.DownloadEstimateLines(update).Select(line => "  " + line));
        lines.Add("  totals (to):");
        lines.AddRange(ReleaseReporting.TotalsLines(ReleaseTotals.Compute(to)).Select(line => "  " + line));
        lines.Add("  durations:");
        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));

        return lines;
    }

    private static int Count(ReleaseDiff diff, FileChangeKind kind)
    {
        return diff.FileChanges.Count(change => change.Kind == kind);
    }

    private static int Count(ReleaseDiff diff, ArtifactChangeKind kind)
    {
        return diff.ArtifactChanges.Count(change => change.Kind == kind);
    }

    private static long Bytes(ReleaseDiff diff, ArtifactChangeKind kind)
    {
        return diff.ArtifactChanges.Where(change => change.Kind == kind).Sum(change => change.Payload.Size);
    }
}
