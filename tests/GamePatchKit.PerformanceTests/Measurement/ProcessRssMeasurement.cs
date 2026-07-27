using System.Diagnostics;
using GamePatchKit.PerformanceTests.Support;

namespace GamePatchKit.PerformanceTests.Measurement;

// Wraps a scenario's ProcessStartInfo in GNU `/usr/bin/time -v` (Linux) or BSD `/usr/bin/time -l` (macOS),
// runs it a fixed number of times as separate processes and reports each run's peak RSS. Only the Linux
// numbers are authoritative against the PRD's blocking gate; macOS numbers exist purely so the harness itself
// can be smoke-tested on this development machine.
internal static class ProcessRssMeasurement
{
    public sealed record RssResult(IReadOnlyList<long> PeakRssBytesPerRun, bool IsAuthoritative)
    {
        public long MaxPeakRssBytes => PeakRssBytesPerRun.Max();
    }

    public static async Task<RssResult> MeasureAsync(
        Func<ProcessStartInfo> scenarioFactory,
        int repeats = 3,
        CancellationToken cancellationToken = default)
    {
        var peakRssPerRun = new List<long>(repeats);

        for (int run = 0; run < repeats; run++)
        {
            peakRssPerRun.Add(await RunOnceAsync(scenarioFactory(), cancellationToken).ConfigureAwait(false));
        }

        return new RssResult(peakRssPerRun, IsAuthoritative: ChildProcess.IsLinux);
    }

    private static async Task<long> RunOnceAsync(ProcessStartInfo scenario, CancellationToken cancellationToken)
    {
        ProcessStartInfo wrapped = WrapWithTime(scenario);

        using var process = new Process { StartInfo = wrapped };
        process.Start();

        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string stderr = await stderrTask.ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Scenario process exited with code {process.ExitCode}. stderr:\n{stderr}");
        }

        return ParsePeakRssBytes(stderr)
            ?? throw new InvalidOperationException($"Could not find a peak RSS line in /usr/bin/time output:\n{stderr}");
    }

    private static ProcessStartInfo WrapWithTime(ProcessStartInfo inner)
    {
        if (!ChildProcess.IsLinux && !ChildProcess.IsMacOs)
        {
            throw new PlatformNotSupportedException("Peak RSS measurement is only implemented for Linux and macOS.");
        }

        var wrapped = new ProcessStartInfo("/usr/bin/time")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // GNU time -v localizes its labels (e.g. "Maximum resident set size") under a non-English locale, which
        // would silently break ParsePeakRssBytes's exact-English match on a CI box with a different LC_ALL/LANG.
        // Forcing the wrapped process's own locale to C keeps the output parseable regardless of the ambient one.
        wrapped.Environment["LC_ALL"] = "C";

        wrapped.ArgumentList.Add(ChildProcess.IsLinux ? "-v" : "-l");
        wrapped.ArgumentList.Add(inner.FileName);

        foreach (string argument in inner.ArgumentList)
        {
            wrapped.ArgumentList.Add(argument);
        }

        return wrapped;
    }

    // Public so a unit test can verify both formats against canned sample output before any real scenario run
    // is trusted to have been parsed correctly.
    //   Linux (GNU time -v):  "\tMaximum resident set size (kbytes): 12345"
    //   macOS (BSD time -l):  "       12345  maximum resident set size"
    public static long? ParsePeakRssBytes(string timeOutput)
    {
        foreach (string rawLine in timeOutput.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("Maximum resident set size (kbytes):", StringComparison.OrdinalIgnoreCase))
            {
                string valueText = line[(line.IndexOf(':') + 1)..].Trim();

                if (long.TryParse(valueText, out long kilobytes))
                {
                    return kilobytes * 1024;
                }
            }

            if (line.EndsWith("maximum resident set size", StringComparison.OrdinalIgnoreCase))
            {
                string valueText = line[..^"maximum resident set size".Length].Trim();

                if (long.TryParse(valueText, out long bytes))
                {
                    return bytes;
                }
            }
        }

        return null;
    }
}
