using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestProgram
{
    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public async Task RunAsync_VersionCommand_PrintsAssemblyVersionAndSucceeds(string command)
    {
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(new[] { command }, standardOutput, error);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("0.1.0", standardOutput.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }
}
