using GamePatchKit.Cli.Configuration;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Commands;

internal static class CompactCommand
{
    public static async Task<CommandOutcome> RunAsync(CliArguments args, CancellationToken cancellationToken)
    {
        string configPath = args.GetOptional("config") ?? PackageConfigLoader.DefaultFileName;
        string outputRoot = args.GetRequired("output-root");
        string sourceHash = args.GetRequired("source");
        IReadOnlyList<string> groups = args.GetAll("group");
        IReadOnlyList<string> retainedHashes = args.GetAll("retained");
        bool writeCompressedManifest = args.GetFlag("compressed-manifest");
        bool dryRun = args.GetFlag("dry-run");
        args.GetFlag("json");
        args.EnsureNoUnreadOptions();

        if (groups.Count == 0)
        {
            throw new CliException(CliErrorCodes.InvalidArguments, "compact needs at least one '--group <name>'.");
        }

        var timings = new StageTimings();
        PackageConfig config = await timings.MeasureAsync(
            "config",
            () => Task.FromResult(PackageConfigLoader.Load(configPath))).ConfigureAwait(false);

        PreviousRelease source = await timings.MeasureAsync(
            "source-manifest",
            () => ReadReleaseAsync(outputRoot, config.PackageId, sourceHash, cancellationToken)).ConfigureAwait(false);

        // Every release still kept for rollback, named explicitly. An omitted one is a release whose part
        // paths this compact is free to claim, which is why there is no "scan the tree" default: a directory
        // listing cannot tell a retained release's objects from leftovers.
        IReadOnlyList<ArtifactPayloadObject> retainedObjects = await timings.MeasureAsync(
            "retained-manifests",
            async () =>
            {
                var retained = new List<PreviousRelease>();

                foreach (string retainedHash in retainedHashes)
                {
                    retained.Add(await ReadReleaseAsync(outputRoot, config.PackageId, retainedHash, cancellationToken)
                        .ConfigureAwait(false));
                }

                return RetainedReleaseInventory.Collect(retained, config.PackageId);
            }).ConfigureAwait(false);

        BundleCompactResult result = await timings.MeasureAsync(
            "compact",
            () => new BundleCompactor().CompactAsync(
                new BundleCompactRequest(
                    config,
                    outputRoot,
                    source,
                    groups.ToArray(),
                    retainedObjects,
                    writeCompressedManifest,
                    dryRun),
                cancellationToken)).ConfigureAwait(false);

        return new CommandOutcome(
            BuildResult(config, sourceHash, groups, result, timings),
            BuildTextLines(config, sourceHash, groups, result, dryRun, timings));
    }

    private static async Task<PreviousRelease> ReadReleaseAsync(
        string outputRoot,
        string packageId,
        string manifestHash,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await ReleaseManifestReader.ReadPublishedBytesAsync(
            outputRoot,
            packageId,
            manifestHash,
            cancellationToken).ConfigureAwait(false);

        return new PreviousRelease(bytes, manifestHash);
    }

    private static JObject BuildResult(
        PackageConfig config,
        string sourceHash,
        IReadOnlyList<string> groups,
        BundleCompactResult result,
        StageTimings timings)
    {
        return new JObject
        {
            ["packageId"] = config.PackageId,
            ["sourceManifestHash"] = sourceHash,
            ["groups"] = new JArray(groups.Cast<object>().ToArray()),

            // false is a successful no-op: the physical layout did not move, so the identity below is the
            // source release's own, unchanged.
            ["changed"] = result.Changed,
            ["identity"] = ReleaseReporting.Identity(
                result.Release.DataVersion,
                result.Release.CompactVersion,
                result.Release.ManifestHash),
            ["totals"] = ReleaseReporting.Totals(ReleaseTotals.Compute(result.Release.Manifest)),
            ["artifacts"] = new JObject
            {
                ["createdBundleArtifactCount"] = result.CreatedBundleArtifactCount,
                ["createdBundleArtifactBytes"] = result.CreatedBundleArtifactBytes,
                ["createdFileArtifactCount"] = result.CreatedFileArtifactCount,
                ["createdFileArtifactBytes"] = result.CreatedFileArtifactBytes,
                ["reusedFileArtifactCount"] = result.ReusedFileArtifactCount,
            },
            ["durationsMs"] = timings.ToJson(),
        };
    }

    private static IReadOnlyList<string> BuildTextLines(
        PackageConfig config,
        string sourceHash,
        IReadOnlyList<string> groups,
        BundleCompactResult result,
        bool dryRun,
        StageTimings timings)
    {
        var lines = new List<string>
        {
            $"compact {config.PackageId} groups [{string.Join(", ", groups)}]"
                + (dryRun ? " (dry run, nothing published)" : string.Empty),
            $"  source:  {sourceHash}",
            $"  changed: {(result.Changed ? "true" : "false (no-op, existing identity reused)")}",
        };

        lines.AddRange(ReleaseReporting.IdentityLines(
            result.Release.DataVersion,
            result.Release.CompactVersion,
            result.Release.ManifestHash));
        lines.Add($"  bundles: {result.CreatedBundleArtifactCount} created ({result.CreatedBundleArtifactBytes} bytes)");
        lines.Add($"  files:   {result.CreatedFileArtifactCount} created ({result.CreatedFileArtifactBytes} bytes), "
            + $"{result.ReusedFileArtifactCount} reused");
        lines.Add("  durations:");
        lines.AddRange(timings.ToTextLines().Select(line => "  " + line));

        return lines;
    }
}
