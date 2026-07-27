namespace GamePatchKit.PerformanceTests.Measurement;

public class TestProcessRssMeasurement
{
    // A real GNU `time -v` sample, trimmed to the lines that matter for parsing.
    private const string LinuxSample = """
        	Command being timed: "dotnet GamePatchKit.Cli.dll package"
        	User time (seconds): 0.42
        	System time (seconds): 0.05
        	Percent of CPU this job got: 97%
        	Elapsed (wall clock) time (h:mm:ss or m:ss): 0:00.48
        	Maximum resident set size (kbytes): 131072
        	Exit status: 0
        """;

    // A real BSD `time -l` sample (macOS), trimmed the same way.
    private const string MacOsSample = """
                0.42 real         0.38 user         0.04 sys
          98765432  maximum resident set size
                 0  average shared memory size
                 0  average unshared data size
        """;

    [Fact]
    public void ParsePeakRssBytes_LinuxTimeDashV_ParsesKilobytesAsBytes()
    {
        long? result = ProcessRssMeasurement.ParsePeakRssBytes(LinuxSample);

        Assert.Equal(131072L * 1024, result);
    }

    [Fact]
    public void ParsePeakRssBytes_MacOsTimeDashL_ParsesBytesDirectly()
    {
        long? result = ProcessRssMeasurement.ParsePeakRssBytes(MacOsSample);

        Assert.Equal(98765432L, result);
    }

    [Fact]
    public void ParsePeakRssBytes_NoRecognizedLine_ReturnsNull()
    {
        long? result = ProcessRssMeasurement.ParsePeakRssBytes("nothing useful here\nreally nothing\n");

        Assert.Null(result);
    }
}
