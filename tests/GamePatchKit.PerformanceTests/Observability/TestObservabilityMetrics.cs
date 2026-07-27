using GamePatchKit.PerformanceTests.Scenarios;
using GamePatchKit.PerformanceTests.Support;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.PerformanceTests.Observability;

// PRD 12단계의 "관측 지표 출력 검증" 항목: package·diff·verify·plan-download가 성능·관측 기준 절이
// 요구하는 값을 실제로 --json 출력에 담고 있는지 확인한다. 10,000개 파일 규모가 필요 없는 검증이라 작은
// 즉석 fixture로 always-on(성능 category 아님, 일반 dotnet test에 포함)으로 돈다.
public class TestObservabilityMetrics : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpk-observability-" + Guid.NewGuid().ToString("N"));

    public TestObservabilityMetrics()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageDiffVerifyPlanDownload_ExposeEveryPrdRequiredMetric()
    {
        const string packageId = "obs-fixture";
        string inputRoot = Path.Combine(_root, "input");
        Directory.CreateDirectory(Path.Combine(inputRoot, "content"));
        Directory.CreateDirectory(Path.Combine(inputRoot, "config"));

        for (int i = 0; i < 4; i++)
        {
            File.WriteAllBytes(Path.Combine(inputRoot, "content", $"asset-{i}.bin"), new byte[1000 + i]);
        }

        File.WriteAllText(Path.Combine(inputRoot, "config", "settings.cfg"), "k=v");

        string configPath = Path.Combine(_root, "gamepatchkit.yml");
        File.WriteAllText(configPath, BuildConfigYaml(packageId, inputRoot));

        string outputRoot = Path.Combine(_root, "output");
        JObject packageResult = await RunCliJsonAsync("package", "--config", configPath, "--output-root", outputRoot, "--json");
        AssertPackageMetrics(packageResult);
        string initialHash = ManifestHash(packageResult);
        string initialDataVersion = DataVersion(packageResult);

        File.WriteAllBytes(Path.Combine(inputRoot, "content", "asset-0.bin"), new byte[1234]);
        File.WriteAllBytes(Path.Combine(inputRoot, "content", "asset-new.bin"), new byte[500]);

        JObject incrementalResult = await RunCliJsonAsync(
            "package", "--config", configPath, "--output-root", outputRoot, "--previous", initialHash, "--json");
        AssertPackageMetrics(incrementalResult);
        Assert.True((int)incrementalResult["result"]!["changes"]!["added"]! >= 1);
        Assert.True((int)incrementalResult["result"]!["changes"]!["changed"]! >= 1);
        // settings.cfg (the "config" group's only file) is untouched between the initial and incremental
        // package, so it must be reused - proving reusedFileArtifactBytes is a real, nonzero measurement here,
        // not just a present-but-always-zero field.
        Assert.True((long)incrementalResult["result"]!["artifacts"]!["reusedFileArtifactBytes"]! > 0);
        string incrementalHash = ManifestHash(incrementalResult);

        JObject diffResult = await RunCliJsonAsync(
            "diff", "--output-root", outputRoot, "--package-id", packageId, "--from", initialHash, "--to", incrementalHash, "--json");
        AssertDiffMetrics(diffResult);

        JObject verifyResult = await RunCliJsonAsync(
            "verify", "--output-root", outputRoot, "--package-id", packageId, "--manifest-hash", incrementalHash, "--json");
        AssertVerifyMetrics(verifyResult);

        // "compact 전후 신규 설치 byte 차이" (PRD 성능·관측 기준): not a discrete field either, but derivable by
        // comparing a fresh-install plan-download estimate before and after compacting the bundle group.
        JObject compactResult = await RunCliJsonAsync(
            "compact", "--config", configPath, "--output-root", outputRoot,
            "--source", incrementalHash, "--group", "content", "--json");
        AssertCompactMetrics(compactResult);
        string compactedHash = (string)compactResult["result"]!["identity"]!["manifestHash"]!;

        // Without this, a compact that silently no-ops (changed=false, compactedHash == incrementalHash) would
        // still make both download-byte assertions below pass - proving nothing about a genuine before/after
        // comparison. This fixture's incremental package left new bundle artifacts for compact to consolidate,
        // so a real compact must both report a change and produce a distinct manifest hash.
        Assert.True((bool)compactResult["result"]!["changed"]!, "compact should have found bundle artifacts to consolidate for this fixture, not been a no-op.");
        Assert.NotEqual(incrementalHash, compactedHash);

        JObject planBeforeCompact = await RunCliJsonAsync(
            "plan-download", "--output-root", outputRoot, "--package-id", packageId,
            "--manifest-hash", incrementalHash, "--required-only", "--json");
        JObject planAfterCompact = await RunCliJsonAsync(
            "plan-download", "--output-root", outputRoot, "--package-id", packageId,
            "--manifest-hash", compactedHash, "--required-only", "--json");

        long downloadBytesBeforeCompact = (long)planBeforeCompact["result"]!["plan"]!["downloadBytes"]!;
        long downloadBytesAfterCompact = (long)planAfterCompact["result"]!["plan"]!["downloadBytes"]!;
        Assert.True(downloadBytesBeforeCompact > 0, "a fresh-install estimate before compact should be a real, positive byte count.");
        Assert.True(downloadBytesAfterCompact > 0, "a fresh-install estimate after compact should be a real, positive byte count.");

        JObject planWithEmptyCache = await RunCliJsonAsync(
            "plan-download", "--output-root", outputRoot, "--package-id", packageId,
            "--manifest-hash", incrementalHash, "--required-only", "--json");
        AssertPlanDownloadMetrics(planWithEmptyCache);

        // "cache hit byte" (PRD 성능·관측 기준) is not a discrete JSON field anywhere in the current CLI
        // output - there is no such key to assert on. It is derivable instead: plan-download's downloadBytes
        // drops once the release's own artifacts are already present under --cache-root, and the drop is the
        // cache-hit byte count. That derivability is what this assertion demonstrates.
        string cacheRoot = Path.Combine(_root, "cache");
        CopyPublishedArtifactsIntoCache(outputRoot, packageId, cacheRoot);

        JObject planWithPopulatedCache = await RunCliJsonAsync(
            "plan-download", "--output-root", outputRoot, "--package-id", packageId,
            "--manifest-hash", incrementalHash, "--required-only", "--cache-root", cacheRoot, "--json");

        long downloadBytesEmptyCache = (long)planWithEmptyCache["result"]!["plan"]!["downloadBytes"]!;
        long downloadBytesPopulatedCache = (long)planWithPopulatedCache["result"]!["plan"]!["downloadBytes"]!;
        Assert.True(
            downloadBytesPopulatedCache < downloadBytesEmptyCache,
            "populating --cache-root should reduce estimated download bytes - the drop is what 'cache hit bytes' is computed from.");

        // Runtime's own side of the PRD's observability list: InstallOrUpdateAsync's returned PackageState and
        // its IProgress<PatchProgress> stream, not just the CLI's --json output.
        string runtimeRoot = Path.Combine(_root, "runtime");
        ChildProcess.Result harnessResult = await ChildProcess.RunAsync(ChildProcess.HarnessDllPath, new[]
        {
            "--publish-root", outputRoot,
            "--runtime-root", runtimeRoot,
            "--package-id", packageId,
            "--data-version", initialDataVersion,
            "--manifest-hash", initialHash,
        });

        if (harnessResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Runtime harness failed (exit {harnessResult.ExitCode}): {harnessResult.StandardError}");
        }

        AssertRuntimeMetrics(JObject.Parse(harnessResult.StandardOutput));
    }

    private static void AssertRuntimeMetrics(JObject result)
    {
        Assert.NotNull(result["active"]!["dataVersion"]);
        Assert.NotNull(result["active"]!["manifestHash"]);
        Assert.NotEmpty((JArray)result["groups"]!);

        foreach (JToken group in (JArray)result["groups"]!)
        {
            Assert.NotNull(group["name"]);
            Assert.NotNull(group["status"]);
        }

        // A field merely being present is not enough - reportCount, lastTotalFiles and lastTotalBytes all
        // default to 0 even when PackageRuntime never reported real progress (PackageRuntime's own final
        // report, PatchStage.Completed, itself carries all-zero totals), which would silently pass through
        // either a broken IProgress<PatchProgress> wiring or the harness picking the wrong report to summarize.
        // Asserting these are positive proves real per-file/byte progress was actually observed.
        JObject progress = (JObject)result["progress"]!;
        Assert.True((int)progress["reportCount"]! > 0, "PackageRuntime should have reported at least one PatchProgress update.");
        Assert.NotNull(progress["retryCount"]);
        Assert.NotNull(progress["lastStage"]);
        Assert.True((int)progress["lastTotalFiles"]! > 0, "lastTotalFiles should reflect a real download plan, not a zeroed Completed-stage report.");
        Assert.True((long)progress["lastTotalBytes"]! > 0, "lastTotalBytes should reflect a real download plan, not a zeroed Completed-stage report.");
    }

    private static void AssertPackageMetrics(JObject envelope)
    {
        JObject result = (JObject)envelope["result"]!;
        JObject totals = (JObject)result["totals"]!;
        Assert.NotNull(totals["fileCount"]);
        Assert.NotNull(totals["fileBytes"]);
        Assert.NotEmpty((JArray)totals["groups"]!);

        foreach (JToken group in (JArray)totals["groups"]!)
        {
            Assert.NotNull(group["fileCount"]);
            Assert.NotNull(group["fileBytes"]);
        }

        JObject changes = (JObject)result["changes"]!;
        Assert.NotNull(changes["added"]);
        Assert.NotNull(changes["changed"]);
        Assert.NotNull(changes["deleted"]);
        Assert.NotNull(changes["groupMoved"]);

        JObject artifacts = (JObject)result["artifacts"]!;
        Assert.NotNull(artifacts["createdFileArtifactCount"]);
        Assert.NotNull(artifacts["createdFileArtifactBytes"]);
        Assert.NotNull(artifacts["reusedFileArtifactCount"]);
        Assert.NotNull(artifacts["reusedFileArtifactBytes"]);
        Assert.NotNull(artifacts["createdBundleArtifactCount"]);
        Assert.NotNull(artifacts["createdBundleArtifactBytes"]);
        Assert.NotNull(artifacts["reusedBundleArtifactCount"]);
        Assert.NotNull(artifacts["reusedBundleArtifactBytes"]);

        AssertDownloadEstimate((JObject)result["estimatedFirstInstall"]!);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    private static void AssertCompactMetrics(JObject envelope)
    {
        JObject result = (JObject)envelope["result"]!;
        Assert.NotNull(result["identity"]!["manifestHash"]);
        JObject artifacts = (JObject)result["artifacts"]!;
        Assert.NotNull(artifacts["createdBundleArtifactCount"]);
        Assert.NotNull(artifacts["createdBundleArtifactBytes"]);
        Assert.NotNull(artifacts["createdFileArtifactCount"]);
        Assert.NotNull(artifacts["createdFileArtifactBytes"]);
        Assert.NotNull(artifacts["reusedFileArtifactCount"]);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    private static void AssertDiffMetrics(JObject envelope)
    {
        JObject result = (JObject)envelope["result"]!;
        JObject files = (JObject)result["files"]!;
        Assert.NotNull(files["added"]);
        Assert.NotNull(files["contentChanged"]);
        Assert.NotNull(files["removed"]);
        Assert.NotNull(files["groupMoved"]);

        JObject artifacts = (JObject)result["artifacts"]!;
        Assert.NotNull(artifacts["addedObjectCount"]);
        Assert.NotNull(artifacts["addedObjectBytes"]);
        Assert.NotNull(artifacts["removedObjectCount"]);
        Assert.NotNull(artifacts["removedObjectBytes"]);

        AssertDownloadEstimate((JObject)result["estimatedUpdate"]!);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    private static void AssertVerifyMetrics(JObject envelope)
    {
        JObject result = (JObject)envelope["result"]!;
        Assert.NotNull(result["totals"]!["fileCount"]);
        Assert.NotNull(result["signature"]!["state"]);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    private static void AssertPlanDownloadMetrics(JObject envelope)
    {
        JObject result = (JObject)envelope["result"]!;
        AssertDownloadEstimate((JObject)result["plan"]!);
        Assert.NotNull(result["localState"]!["installedFileCount"]);
        Assert.NotNull(result["localState"]!["cachedObjectCount"]);
        Assert.NotEmpty(((JObject)result["durationsMs"]!).Properties());
    }

    private static void AssertDownloadEstimate(JObject estimate)
    {
        Assert.NotNull(estimate["downloadBytes"]);
        Assert.NotNull(estimate["temporaryBytes"]);
        Assert.NotNull(estimate["fileObjectCount"]);
        Assert.NotNull(estimate["bundleCount"]);
    }

    private static void CopyPublishedArtifactsIntoCache(string outputRoot, string packageId, string cacheRoot)
    {
        string sourceArtifacts = Path.Combine(outputRoot, packageId, "artifacts");
        string destinationArtifacts = Path.Combine(cacheRoot, packageId, "artifacts");
        DirectoryCopy.CopyRecursively(sourceArtifacts, destinationArtifacts);
    }

    private static string ManifestHash(JObject envelope) => (string)envelope["result"]!["identity"]!["manifestHash"]!;

    private static string DataVersion(JObject envelope) => (string)envelope["result"]!["identity"]!["dataVersion"]!;

    private static async Task<JObject> RunCliJsonAsync(params string[] args)
    {
        ChildProcess.Result result = await ChildProcess.RunAsync(ChildProcess.CliDllPath, args);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"gpk {args[0]} failed (exit {result.ExitCode}): {result.StandardError}\n{result.StandardOutput}");
        }

        return JObject.Parse(result.StandardOutput);
    }

    private static string BuildConfigYaml(string packageId, string inputRoot)
    {
        return string.Join(
            Environment.NewLine,
            "schemaVersion: 1",
            $"packageId: {packageId}",
            $"inputRoot: {inputRoot.Replace('\\', '/')}",
            "include:",
            "  - \"**/*\"",
            "compression:",
            "  kind: zstd",
            "  codecId: zstd",
            "groups:",
            "  - name: content",
            "    include:",
            "      - \"content/**/*\"",
            "    artifactMode: bundle",
            "    required: true",
            "  - name: config",
            "    include:",
            "      - \"config/**/*\"",
            "    artifactMode: file",
            "    required: true",
            string.Empty);
    }
}
