using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

internal static class PlanDownloadCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string outputRoot = args.GetRequired("output-root");
        string packageId = args.GetRequired("package-id");
        string manifestHash = args.GetRequired("manifest-hash");
        IReadOnlyList<string> requestedGroups = args.GetAll("group");
        bool requiredOnly = args.GetFlag("required-only");
        string? installRoot = args.GetOptional("install-root");
        string? cacheRoot = args.GetOptional("cache-root");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        // The group set is never implicit. A first install or a global update asks for --required-only; an
        // optional-group install names its groups. Defaulting either way would quietly plan the wrong batch.
        if (requiredOnly == (requestedGroups.Count > 0))
        {
            throw new CliException(
                CliErrorCodes.InvalidArguments,
                "plan-download needs exactly one of '--required-only' and one or more '--group <name>'.");
        }

        var timings = new StageTimings();

        ReleaseManifest target = await timings.MeasureAsync(
            "read-manifest",
            () => ReleaseManifestReader.ReadPublishedAsync(outputRoot, packageId, manifestHash, cancellationToken))
            .ConfigureAwait(false);

        IReadOnlyList<string> groups = requiredOnly
            ? DownloadPlanner.RequiredGroupNames(target)
            : requestedGroups;

        IReadOnlyList<LocalFileState> installedFiles = await timings.MeasureAsync(
            "scan-installation",
            () => installRoot == null
                ? Task.FromResult<IReadOnlyList<LocalFileState>>(Array.Empty<LocalFileState>())
                : LocalInstallationScanner.ScanInstalledFilesAsync(installRoot, cancellationToken)).ConfigureAwait(false);

        IReadOnlyList<CachedArtifactObject> cachedObjects = await timings.MeasureAsync(
            "scan-cache",
            () => cacheRoot == null
                ? Task.FromResult<IReadOnlyList<CachedArtifactObject>>(Array.Empty<CachedArtifactObject>())
                : LocalInstallationScanner.ScanCachedObjectsAsync(cacheRoot, cancellationToken)).ConfigureAwait(false);

        DownloadPlan plan = await timings.MeasureAsync(
            "plan",
            () => Task.FromResult(DownloadPlanner.Plan(target, groups, installedFiles, cachedObjects))).ConfigureAwait(false);

        return new CommandOutcome(
            BuildResult(packageId, manifestHash, target, groups, installedFiles, cachedObjects, plan, timings),
            BuildTextLines(packageId, manifestHash, groups, installedFiles, cachedObjects, plan, timings));
    }

    private static JObject BuildResult(
        string packageId,
        string manifestHash,
        ReleaseManifest target,
        IReadOnlyList<string> groups,
        IReadOnlyList<LocalFileState> installedFiles,
        IReadOnlyList<CachedArtifactObject> cachedObjects,
        DownloadPlan plan,
        StageTimings timings)
    {
        return new JObject
        {
            ["packageId"] = packageId,
            ["identity"] = ReleaseReporting.Identity(target.DataVersion, target.CompactVersion, manifestHash),
            ["groups"] = new JArray(groups.Cast<object>().ToArray()),
            ["localState"] = new JObject
            {
                ["installedFileCount"] = installedFiles.Count,
                ["cachedObjectCount"] = cachedObjects.Count,
            },
            ["plan"] = ReleaseReporting.DownloadEstimate(plan),
            ["artifactCount"] = plan.Artifacts.Count,
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(
        string packageId,
        string manifestHash,
        IReadOnlyList<string> groups,
        IReadOnlyList<LocalFileState> installedFiles,
        IReadOnlyList<CachedArtifactObject> cachedObjects,
        DownloadPlan plan,
        StageTimings timings)
    {
        var lines = new List<string>
        {
            $"plan-download {packageId} {manifestHash}",
            $"  groups: [{string.Join(", ", groups)}]",
            $"  local:  {installedFiles.Count} installed files, {cachedObjects.Count} cached objects",
            $"  artifacts to read: {plan.Artifacts.Count}",
        };

        lines.AddRange(ReleaseReporting.DownloadEstimateLines(plan));
        lines.Add("  durations:");
        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));

        return lines;
    }
}
