using System.Globalization;
using System.Runtime.InteropServices;
using GamePatchKit.PerformanceTests.Measurement;
using GamePatchKit.PerformanceTests.Support;

namespace GamePatchKit.PerformanceTests.Reports;

// Appends one row per (scenario, fixture size) run to docs/perf/results.md - the running record described in
// docs/perf/README.md. A row's "gate" column only ever reads pass/fail on Linux at full scale; every other row
// is explicitly labeled reference-only so it is never mistaken for the official blocking-gate result.
internal static class PerformanceReportWriter
{
    private static readonly object _fileLock = new object();

    public static void Append(string scenario, string sizeLabel, ProcessRssMeasurement.RssResult result, string? gateVerdict = null)
    {
        string path = Path.Combine(RepositoryRoot.Find(), "docs", "perf", "results.md");
        string os = RuntimeInformation.OSDescription;
        string runs = string.Join(", ", result.PeakRssBytesPerRun.Select(FormatMiB));
        string verdict = gateVerdict ?? (result.IsAuthoritative ? "not asserted" : "reference only (non-Linux)");

        string row = $"| {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | {scenario} | {sizeLabel} | {os} | {runs} | {FormatMiB(result.MaxPeakRssBytes)} | {verdict} |";

        lock (_fileLock)
        {
            File.AppendAllText(path, row + Environment.NewLine);
        }
    }

    public static string FormatMiB(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";
    }
}
