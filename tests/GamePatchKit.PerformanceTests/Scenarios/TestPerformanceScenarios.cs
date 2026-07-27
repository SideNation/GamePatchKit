using System.Diagnostics;
using GamePatchKit.PerformanceTests.Measurement;
using GamePatchKit.PerformanceTests.Reports;
using GamePatchKit.PerformanceTests.Support;

namespace GamePatchKit.PerformanceTests.Scenarios;

// The PRD's 7 performance scenarios (docs/plan/12-performance-validation.md), each run against the real `gpk`
// CLI or the Runtime install harness as a genuine subprocess. Every fact always runs at a small "smoke" scale
// to prove the pipeline itself works; only when GPK_PERF_FULL_SCALE=1 does it additionally run the real 1 GiB
// and 256 MiB fixtures and assert the PRD's 512MiB/64MiB gates (Linux only - see ScenarioScale).
[Trait("Category", "Performance")]
public class TestPerformanceScenarios : IClassFixture<ScenarioFixtureSet>, IDisposable
{
    private const long PeakRssGateBytes = 512L * 1024 * 1024;
    private const long ScalingDeltaGateBytes = 64L * 1024 * 1024;

    private readonly ScenarioFixtureSet _fixtures;
    private readonly List<string> _scratchDirectories = new List<string>();

    public TestPerformanceScenarios(ScenarioFixtureSet fixtures)
    {
        _fixtures = fixtures;
    }

    public void Dispose()
    {
        foreach (string directory in _scratchDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public Task InitialPackage()
    {
        return RunScenarioAsync("initial-package", state => Task.FromResult<Func<ProcessStartInfo>>(() =>
        {
            string outputRoot = NewScratchDirectory();
            return CliStartInfo("package", "--config", state.Fixture.ConfigPath, "--output-root", outputRoot, "--json");
        }));
    }

    [Fact]
    public Task Incremental()
    {
        return RunScenarioAsync("incremental-package", state => Task.FromResult<Func<ProcessStartInfo>>(() =>
        {
            string outputRoot = NewScratchDirectory();
            DirectoryCopy.CopyRecursively(state.AfterInitialOutputRoot, outputRoot);
            return CliStartInfo(
                "package", "--config", state.MutatedConfigPath, "--output-root", outputRoot,
                "--previous", state.InitialManifestHash, "--json");
        }));
    }

    [Fact]
    public Task Compact()
    {
        return RunScenarioAsync("compact", async state =>
        {
            // Preflight, not RSS-measured: if this golden state ever stops needing compaction, a no-op
            // `gpk compact` would still exit 0, and every measured repeat below would silently benchmark that
            // cheap no-op instead of the real compact work the 512MiB/64MiB gate is meant to cover.
            await AssertCompactWouldChangeSomethingAsync(state).ConfigureAwait(false);

            return () =>
            {
                string outputRoot = NewScratchDirectory();
                DirectoryCopy.CopyRecursively(state.AfterIncrementalOutputRoot, outputRoot);
                return CliStartInfo(
                    "compact", "--config", state.Fixture.ConfigPath, "--output-root", outputRoot,
                    "--source", state.IncrementalManifestHash, "--group", "content", "--json");
            };
        });
    }

    [Fact]
    public Task Verify()
    {
        return RunScenarioAsync("verify", state => Task.FromResult<Func<ProcessStartInfo>>(() => CliStartInfo(
            "verify", "--output-root", state.AfterInitialOutputRoot, "--package-id", state.Fixture.PackageId,
            "--manifest-hash", state.InitialManifestHash, "--json")));
    }

    [Fact]
    public Task SignedVerify()
    {
        return RunScenarioAsync("signed-verify", state => Task.FromResult<Func<ProcessStartInfo>>(() => CliStartInfo(
            "verify", "--output-root", state.SignedOutputRoot, "--package-id", state.Fixture.PackageId,
            "--manifest-hash", state.InitialManifestHash, "--trusted-key", state.TrustedPublicKeyBase64Url,
            "--require-signature", "--json")));
    }

    [Fact]
    public Task PlanDownload()
    {
        return RunScenarioAsync("plan-download", state => Task.FromResult<Func<ProcessStartInfo>>(() => CliStartInfo(
            "plan-download", "--output-root", state.AfterInitialOutputRoot, "--package-id", state.Fixture.PackageId,
            "--manifest-hash", state.InitialManifestHash, "--required-only", "--json")));
    }

    [Fact]
    public Task RuntimeInstall()
    {
        return RunScenarioAsync("runtime-install", state => Task.FromResult<Func<ProcessStartInfo>>(() =>
        {
            string runtimeRoot = NewScratchDirectory();
            return ChildProcess.BuildStartInfo(ChildProcess.HarnessDllPath, new[]
            {
                "--publish-root", state.AfterInitialOutputRoot,
                "--runtime-root", runtimeRoot,
                "--package-id", state.Fixture.PackageId,
                "--data-version", state.InitialDataVersion,
                "--manifest-hash", state.InitialManifestHash,
            });
        }));
    }

    // Runs `gpk compact` once against a disposable scratch copy of the golden state, purely to confirm this
    // fixture still gives compact something to consolidate - never RSS-measured, never counted as one of the
    // scenario's 3 repeats.
    private static async Task AssertCompactWouldChangeSomethingAsync(ScenarioFixtureSet.SizeState state)
    {
        string preflightRoot = Path.Combine(Path.GetTempPath(), "gpk-perf-compact-preflight-" + Guid.NewGuid().ToString("N"));
        DirectoryCopy.CopyRecursively(state.AfterIncrementalOutputRoot, preflightRoot);

        try
        {
            ChildProcess.Result result = await ChildProcess.RunAsync(ChildProcess.CliDllPath, new[]
            {
                "compact", "--config", state.Fixture.ConfigPath, "--output-root", preflightRoot,
                "--source", state.IncrementalManifestHash, "--group", "content", "--json",
            });

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"compact preflight failed (exit {result.ExitCode}): {result.StandardError}");
            }

            var envelope = Newtonsoft.Json.Linq.JObject.Parse(result.StandardOutput);
            Assert.True(
                (bool)envelope["result"]!["changed"]!,
                "This golden state no longer gives compact anything to consolidate, so the compact scenario "
                + "would measure a no-op instead of real compact work.");
        }
        finally
        {
            if (Directory.Exists(preflightRoot))
            {
                Directory.Delete(preflightRoot, recursive: true);
            }
        }
    }

    // Shared runner: measures the smoke-scale fixture (always) and, only when GPK_PERF_FULL_SCALE=1, the real
    // 1 GiB and 256 MiB fixtures with the PRD's gates asserted against the 1 GiB run.
    private async Task RunScenarioAsync(string scenarioName, Func<ScenarioFixtureSet.SizeState, Task<Func<ProcessStartInfo>>> buildScenario)
    {
        // GPK_PERF_OFFICIAL_GATE asserts that THIS run's pass/fail is the PRD's blocking-gate result, which by
        // definition requires both Linux (the only platform ProcessRssMeasurement treats as authoritative) and
        // full-scale fixtures. Checking this before any other branch is what stops every silent-success path
        // below - the Windows/non-Linux-non-macOS skip, the macOS "reference only" fallback further down, and
        // the smoke-only early return - from letting a mis-provisioned "blocking" CI job report green without
        // ever having executed the actual gate.
        if (ScenarioScale.OfficialGate)
        {
            Assert.True(
                ChildProcess.IsLinux,
                "GPK_PERF_OFFICIAL_GATE=1 must run on Linux - only Linux peak-RSS numbers are authoritative against the PRD's blocking gate.");
            Assert.True(
                ScenarioScale.FullScale,
                "GPK_PERF_OFFICIAL_GATE=1 requires GPK_PERF_FULL_SCALE=1 - refusing to silently skip the actual gate measurement.");
        }

        // ProcessRssMeasurement only implements peak-RSS collection for Linux and macOS (docs/perf/README.md).
        // This project is part of the solution, so a bare `dotnet test GamePatchKit.sln` on Windows would
        // otherwise hit that unsupported-platform throw in every one of these facts; skip cleanly instead.
        if (!ChildProcess.IsLinux && !ChildProcess.IsMacOs)
        {
            return;
        }

        ScenarioFixtureSet.SizeState smoke = await _fixtures.GetOrBuildAsync("smoke", ScenarioScale.SmokeTotalBytes);
        ProcessRssMeasurement.RssResult smokeResult = await ProcessRssMeasurement.MeasureAsync(await buildScenario(smoke));

        // Smoke scale is never the official gate regardless of OS, so its row is always reference-only - unlike
        // the full-scale path below, which lets Append's own IsAuthoritative fallback distinguish Linux from
        // non-Linux runs made without an explicit verdict.
        PerformanceReportWriter.Append(scenarioName, "smoke", smokeResult, "reference only (smoke scale)");

        if (!ScenarioScale.FullScale)
        {
            return;
        }

        ScenarioFixtureSet.SizeState oneGiB = await _fixtures.GetOrBuildAsync("1gib", ScenarioScale.OneGiBBytes);
        ProcessRssMeasurement.RssResult oneGiBResult = await ProcessRssMeasurement.MeasureAsync(await buildScenario(oneGiB));

        ScenarioFixtureSet.SizeState twoFiftySixMiB = await _fixtures.GetOrBuildAsync("256mib", ScenarioScale.TwoFiftySixMiBBytes);
        ProcessRssMeasurement.RssResult twoFiftySixMiBResult = await ProcessRssMeasurement.MeasureAsync(await buildScenario(twoFiftySixMiB));

        bool peakRssWithinGate = oneGiBResult.MaxPeakRssBytes <= PeakRssGateBytes;
        long delta = oneGiBResult.MaxPeakRssBytes - twoFiftySixMiBResult.MaxPeakRssBytes;
        bool deltaWithinGate = delta <= ScalingDeltaGateBytes;

        // Being Linux is not enough to call a number the official gate result - only the specific CI job pinned
        // to the PRD's exact reference environment sets GPK_PERF_OFFICIAL_GATE, so an arbitrary Linux box
        // running with GPK_PERF_FULL_SCALE never gets its pass/fail mistaken for that job's result.
        bool isOfficialGateRun = oneGiBResult.IsAuthoritative && ScenarioScale.OfficialGate;

        // The delta value and, on failure, which specific gate(s) failed are recorded in the row itself - a bare
        // "fail" would not let anyone auditing docs/perf/results.md later tell the 512MiB peak gate apart from
        // the 64MiB scaling-delta gate, or see the actual delta that was measured.
        string deltaText = $"delta {PerformanceReportWriter.FormatMiB(delta)}";
        string verdict;

        if (isOfficialGateRun)
        {
            if (peakRssWithinGate && deltaWithinGate)
            {
                verdict = $"pass ({deltaText})";
            }
            else
            {
                var failedGates = new List<string>();
                if (!peakRssWithinGate)
                {
                    failedGates.Add("512MiB peak gate failed");
                }

                if (!deltaWithinGate)
                {
                    failedGates.Add("64MiB delta gate failed");
                }

                verdict = $"fail ({string.Join("; ", failedGates)}, {deltaText})";
            }
        }
        else
        {
            verdict = oneGiBResult.IsAuthoritative
                ? $"reference only (Linux, GPK_PERF_OFFICIAL_GATE not set, {deltaText})"
                : $"reference only (non-Linux, {deltaText})";
        }

        PerformanceReportWriter.Append(scenarioName, "1gib", oneGiBResult, verdict);
        PerformanceReportWriter.Append(scenarioName, "256mib", twoFiftySixMiBResult, verdict);

        if (isOfficialGateRun)
        {
            Assert.True(peakRssWithinGate, $"{scenarioName}: peak RSS {oneGiBResult.MaxPeakRssBytes} bytes exceeds the 512MiB gate.");
            Assert.True(deltaWithinGate, $"{scenarioName}: 256MiB->1GiB peak RSS delta {delta} bytes exceeds the 64MiB gate.");
        }
    }

    private static ProcessStartInfo CliStartInfo(params string[] args)
    {
        return ChildProcess.BuildStartInfo(ChildProcess.CliDllPath, args);
    }

    private string NewScratchDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "gpk-perf-scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _scratchDirectories.Add(path);
        return path;
    }
}
