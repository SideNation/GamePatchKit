using System.Globalization;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

// Shared shapes for the values more than one command reports: the release identity every result is keyed by,
// the PRD's per-release and per-group counts, and a download estimate. Written once so package, diff, verify
// and plan-download describe the same thing with the same field names.
internal static class ReleaseReporting
{
    public static JObject Identity(string dataVersion, long compactVersion, string manifestHash)
    {
        return new JObject
        {
            ["dataVersion"] = dataVersion,
            ["compactVersion"] = compactVersion,
            ["manifestHash"] = manifestHash,
        };
    }

    public static IEnumerable<string> IdentityLines(string dataVersion, long compactVersion, string manifestHash)
    {
        yield return $"  dataVersion:    {dataVersion}";
        yield return $"  compactVersion: {compactVersion.ToString(CultureInfo.InvariantCulture)}";
        yield return $"  manifestHash:   {manifestHash}";
    }

    public static JObject Totals(ReleaseTotals totals)
    {
        return new JObject
        {
            ["fileCount"] = totals.FileCount,
            ["fileBytes"] = totals.FileBytes,
            ["fileArtifactCount"] = totals.FileArtifactCount,
            ["bundleArtifactCount"] = totals.BundleArtifactCount,
            ["storedObjectCount"] = totals.StoredObjectCount,
            ["storedObjectBytes"] = totals.StoredObjectBytes,
            ["groups"] = new JArray(totals.Groups
                .Select(group => new JObject
                {
                    ["name"] = group.Name,
                    ["required"] = group.Required,
                    ["fileCount"] = group.FileCount,
                    ["fileBytes"] = group.FileBytes,
                })
                .Cast<object>()
                .ToArray()),
        };
    }

    public static IEnumerable<string> TotalsLines(ReleaseTotals totals)
    {
        yield return $"  files:  {totals.FileCount} ({totals.FileBytes} bytes)";
        yield return $"  stored: {totals.StoredObjectCount} objects ({totals.StoredObjectBytes} bytes) "
            + $"in {totals.FileArtifactCount} file artifacts and {totals.BundleArtifactCount} bundles";

        foreach (GroupTotals group in totals.Groups)
        {
            string requirement = group.Required ? "required" : "optional";
            yield return $"    group {group.Name} ({requirement}): {group.FileCount} files, {group.FileBytes} bytes";
        }
    }

    public static JObject DownloadEstimate(DownloadPlan plan)
    {
        return new JObject
        {
            ["downloadBytes"] = plan.EstimatedDownloadBytes,
            ["temporaryBytes"] = plan.EstimatedTemporaryBytes,
            ["fileObjectCount"] = plan.FileObjectCount,
            ["bundleCount"] = plan.BundleCount,
            ["missingFileCount"] = plan.MissingFiles.Count,
        };
    }

    public static IEnumerable<string> DownloadEstimateLines(DownloadPlan plan)
    {
        yield return $"  download:  {plan.EstimatedDownloadBytes} bytes "
            + $"in {plan.FileObjectCount} file objects and {plan.BundleCount} bundles";
        yield return $"  temporary: {plan.EstimatedTemporaryBytes} bytes for {plan.MissingFiles.Count} missing files";
    }

    // What a client already holding `installed` still has to fetch to reach every selected group of `target`.
    // package uses it with nothing installed (a first install of the required groups) and diff uses it with
    // the previous release's files (an update), which is the same calculation asked two different questions.
    public static DownloadPlan Estimate(
        ReleaseManifest target,
        IEnumerable<string> groups,
        IEnumerable<LocalFileState> installedFiles)
    {
        return DownloadPlanner.Plan(target, groups, installedFiles, Array.Empty<CachedArtifactObject>());
    }

    public static IReadOnlyList<LocalFileState> AsInstalledFiles(ReleaseManifest manifest)
    {
        return manifest.Files.Select(file => new LocalFileState(file.Path, file.FileHash)).ToArray();
    }

    public static IReadOnlyList<string> AllGroupNames(ReleaseManifest manifest)
    {
        return manifest.Groups.Select(group => group.Name).ToArray();
    }
}
