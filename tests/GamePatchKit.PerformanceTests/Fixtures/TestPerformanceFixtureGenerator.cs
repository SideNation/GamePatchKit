namespace GamePatchKit.PerformanceTests.Fixtures;

[Trait("Category", "Performance")]
public class TestPerformanceFixtureGenerator
{
    [Fact]
    public void Ensure_ProducesExactFileCountAndTotalBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "gpk-perf-fixture-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            const long totalBytes = 20L * 1024 * 1024;
            PerformanceFixtureGenerator.FixtureLayout layout = PerformanceFixtureGenerator.Ensure(root, totalBytes, "smoke");

            string[] files = Directory.GetFiles(layout.InputRoot, "*", SearchOption.AllDirectories);
            Assert.Equal(PerformanceFixtureGenerator.FileCount, files.Length);

            long total = files.Sum(file => new FileInfo(file).Length);
            Assert.Equal(totalBytes, total);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Ensure_SecondCallWithSameParameters_SkipsRegeneration()
    {
        string root = Path.Combine(Path.GetTempPath(), "gpk-perf-fixture-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            const long totalBytes = 20L * 1024 * 1024;
            PerformanceFixtureGenerator.FixtureLayout layout = PerformanceFixtureGenerator.Ensure(root, totalBytes, "smoke");
            string oneFile = Directory.GetFiles(layout.InputRoot, "*", SearchOption.AllDirectories).First();
            DateTime firstWriteTime = File.GetLastWriteTimeUtc(oneFile);

            PerformanceFixtureGenerator.Ensure(root, totalBytes, "smoke");

            Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(oneFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // The plan (paths and group assignment) must not depend on totalBytes: that is what lets the 256 MiB and
    // 1 GiB fixtures share the same files and only differ in size.
    [Fact]
    public void PlanFiles_IsIndependentOfTotalBytes()
    {
        IReadOnlyList<PerformanceFixtureGenerator.PlannedFile> first = PerformanceFixtureGenerator.PlanFiles();
        IReadOnlyList<PerformanceFixtureGenerator.PlannedFile> second = PerformanceFixtureGenerator.PlanFiles();

        Assert.Equal(first.Select(file => file.RelativePath), second.Select(file => file.RelativePath));
        Assert.Equal(first.Select(file => file.Group), second.Select(file => file.Group));
    }

    [Fact]
    public void MutateForIncremental_ChangesRoughlyOnePercentOfFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "gpk-perf-fixture-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            PerformanceFixtureGenerator.FixtureLayout layout = PerformanceFixtureGenerator.Ensure(root, 20L * 1024 * 1024, "smoke");

            var beforeHashes = Directory.GetFiles(layout.InputRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(file => file, File.ReadAllBytes);

            PerformanceFixtureGenerator.MutateForIncremental(layout.InputRoot);

            int changedCount = beforeHashes.Count(entry => !entry.Value.AsSpan().SequenceEqual(File.ReadAllBytes(entry.Key)));

            Assert.Equal(PerformanceFixtureGenerator.FileCount / 100, changedCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
