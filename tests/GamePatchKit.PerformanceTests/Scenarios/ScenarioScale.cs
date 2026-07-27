namespace GamePatchKit.PerformanceTests.Scenarios;

// GPK_PERF_FULL_SCALE gates whether a scenario also runs against the PRD's real 256 MiB/1 GiB fixtures. Off by
// default (local/macOS smoke runs); a Linux CI job sets it to run the official blocking-gate measurement.
internal static class ScenarioScale
{
    public const long SmokeTotalBytes = 20L * 1024 * 1024;
    public const long TwoFiftySixMiBBytes = 256L * 1024 * 1024;
    public const long OneGiBBytes = 1024L * 1024 * 1024;

    public static bool FullScale => Environment.GetEnvironmentVariable("GPK_PERF_FULL_SCALE") == "1";

    // Being on Linux does not by itself mean a peak-RSS number is the PRD's official blocking-gate result - an
    // arbitrary Linux box can have different CPU count, RAM, or GC characteristics than the pinned reference
    // environment (Ubuntu 24.04 x64, repo-pinned .NET 10, Release, Workstation GC, 4 vCPU, 8GiB RAM, local SSD,
    // parallelism 4). This must be set explicitly - only by the specific CI job provisioned to match that
    // environment - before a run's pass/fail is treated as the official gate rather than a Linux reference value.
    public static bool OfficialGate => Environment.GetEnvironmentVariable("GPK_PERF_OFFICIAL_GATE") == "1";
}
