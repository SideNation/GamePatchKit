namespace GamePatchKit.PerformanceTests.Fixtures;

// Deterministic 10,000-file source tree generator for step 12 (docs/plan/12-performance-validation.md). A
// single fixed seed drives both the per-file group/size-class plan and the file content, so the 256 MiB and
// 1 GiB variants share the same file paths, the same group split and the same relative size shape - only the
// absolute payload size differs, per the PRD's scaling-fixture requirement.
public static class PerformanceFixtureGenerator
{
    public const int FileCount = 10_000;
    public const int Seed = 20260101;
    public const string PackageId = "perf-fixture";

    // Bump whenever PlanFiles/ComputeSizes/WriteConfig's logic changes materially (weights, group split,
    // naming, config shape...). Seed/FileCount/totalBytes alone do not change when only the generator's code
    // changes, so without this a stale on-disk fixture from an older checkout would be silently reused forever.
    private const int GeneratorVersion = 1;

    private const string ContentGroupName = "content";
    private const string ConfigGroupName = "config";
    private const double ContentGroupShare = 0.85;

    // Files are drawn from a heavy-tailed weight distribution (tiny/medium/large) rather than a uniform one,
    // so the byte total is dominated by a small share of files the way a real asset tree's is. The absolute
    // weight values do not matter - only their ratios do, since ComputeSizes always renormalizes to the exact
    // requested totalBytes.
    private const double TinyWeight = 1.0;
    private const double MediumWeight = 8.0;
    private const double LargeWeight = 64.0;

    public sealed record PlannedFile(string RelativePath, string Group, double Weight);

    public sealed record FixtureLayout(string RootDirectory, string InputRoot, string ConfigPath, string PackageId);

    // Defends Ensure() against being asked for the same (fixturesRoot, sizeLabel) fixture from more than one
    // thread at once - this assembly disables xUnit's default cross-class parallelization (AssemblyInfo.cs),
    // but the lock stays as cheap insurance against that assumption changing or this being called elsewhere.
    private static readonly object _generationGate = new object();

    public static FixtureLayout Ensure(string fixturesRoot, long totalBytes, string sizeLabel)
    {
        string root = Path.Combine(fixturesRoot, $"{Seed}-{sizeLabel}");
        string inputRoot = Path.Combine(root, "input");
        string configPath = Path.Combine(root, "gamepatchkit.yml");
        string markerPath = Path.Combine(root, ".generated-marker");
        // gamepatchkit.yml bakes in inputRoot as an absolute path (WriteConfig below), so a marker that only
        // covers the generator's own parameters would still "match" after the checkout itself moved, got
        // renamed, or was restored from a cache at a different location - reusing a config that points at a
        // path that may no longer exist, or worse, at another checkout's files. Including inputRoot ties cache
        // validity to the exact path that ends up written into the config.
        string marker = $"{GeneratorVersion}|{Seed}|{FileCount}|{totalBytes}|{inputRoot}";

        lock (_generationGate)
        {
            if (Directory.Exists(root) && File.Exists(markerPath) && File.ReadAllText(markerPath) == marker)
            {
                return new FixtureLayout(root, inputRoot, configPath, PackageId);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(inputRoot);

            IReadOnlyList<PlannedFile> plan = PlanFiles();
            IReadOnlyList<long> sizes = ComputeSizes(plan, totalBytes);
            WriteFiles(inputRoot, plan, sizes);
            WriteConfig(configPath, inputRoot);

            File.WriteAllText(markerPath, marker);
            return new FixtureLayout(root, inputRoot, configPath, PackageId);
        }
    }

    // The per-file plan (relative path, group, size-class weight) depends only on Seed and FileCount, never on
    // totalBytes, so the same plan underlies every fixture size and the incremental-mutation helper can
    // recompute it independently without re-reading a generated fixture from disk.
    public static IReadOnlyList<PlannedFile> PlanFiles()
    {
        var random = new Random(Seed);
        var files = new List<PlannedFile>(FileCount);
        int contentIndex = 0;
        int configIndex = 0;

        for (int i = 0; i < FileCount; i++)
        {
            bool isContent = random.NextDouble() < ContentGroupShare;
            string group = isContent ? ContentGroupName : ConfigGroupName;
            string relativePath = isContent
                ? $"content/asset-{contentIndex++:D5}.bin"
                : $"config/setting-{configIndex++:D5}.cfg";

            double roll = random.NextDouble();
            double weight = roll < 0.70 ? TinyWeight : roll < 0.95 ? MediumWeight : LargeWeight;

            files.Add(new PlannedFile(relativePath, group, weight));
        }

        return files;
    }

    private static IReadOnlyList<long> ComputeSizes(IReadOnlyList<PlannedFile> plan, long totalBytes)
    {
        double totalWeight = plan.Sum(file => file.Weight);
        var sizes = new long[plan.Count];
        long assigned = 0;

        for (int i = 0; i < plan.Count; i++)
        {
            long size = Math.Max(1, (long)Math.Round(plan[i].Weight / totalWeight * totalBytes));
            sizes[i] = size;
            assigned += size;
        }

        // Rounding leaves a small remainder; absorbing it into the last file is what makes the sum land on
        // totalBytes exactly rather than merely approximately.
        sizes[^1] = Math.Max(1, sizes[^1] + (totalBytes - assigned));
        return sizes;
    }

    private static void WriteFiles(string inputRoot, IReadOnlyList<PlannedFile> plan, IReadOnlyList<long> sizes)
    {
        var contentRandom = new Random(Seed + 1);
        var buffer = new byte[64 * 1024];

        for (int i = 0; i < plan.Count; i++)
        {
            string fullPath = Path.Combine(inputRoot, plan[i].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            long remaining = sizes[i];

            while (remaining > 0)
            {
                int chunk = (int)Math.Min(buffer.Length, remaining);
                contentRandom.NextBytes(buffer.AsSpan(0, chunk));
                stream.Write(buffer, 0, chunk);
                remaining -= chunk;
            }
        }
    }

    private static void WriteConfig(string configPath, string inputRoot)
    {
        string yaml = string.Join(
            Environment.NewLine,
            "schemaVersion: 1",
            $"packageId: {PackageId}",
            $"inputRoot: {inputRoot.Replace('\\', '/')}",
            "include:",
            "  - \"**/*\"",
            "compression:",
            "  kind: zstd",
            "  codecId: zstd",
            "groups:",
            $"  - name: {ContentGroupName}",
            $"    include:",
            $"      - \"{ContentGroupName}/**/*\"",
            "    artifactMode: bundle",
            "    required: true",
            $"  - name: {ConfigGroupName}",
            $"    include:",
            $"      - \"{ConfigGroupName}/**/*\"",
            "    artifactMode: file",
            "    required: true",
            string.Empty);

        File.WriteAllText(configPath, yaml);
    }

    // Deterministically mutates roughly 1% of the input tree in place for the incremental-package scenario:
    // every 100th planned file (by plan order, not filesystem enumeration order, so the selection does not
    // depend on directory-listing order) gets its content regenerated from a distinct random stream.
    public static void MutateForIncremental(string inputRoot)
    {
        IReadOnlyList<PlannedFile> plan = PlanFiles();
        var mutationRandom = new Random(Seed + 2);

        for (int i = 0; i < plan.Count; i += 100)
        {
            string fullPath = Path.Combine(inputRoot, plan[i].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            long originalSize = new FileInfo(fullPath).Length;
            var bytes = new byte[originalSize];
            mutationRandom.NextBytes(bytes);
            File.WriteAllBytes(fullPath, bytes);
        }
    }
}
