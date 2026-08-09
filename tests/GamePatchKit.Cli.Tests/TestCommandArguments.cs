using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestCommandArguments
{
    [Fact]
    public void ParseBuild_SourceIsMissing_UsesCurrentDirectory()
    {
        var arguments = new[] { "--output", "patches" };

        BuildArguments result = ArgumentsParser.ParseBuild(arguments);

        Assert.Equal(Directory.GetCurrentDirectory(), result.SourcePath);
        Assert.Equal("patches", result.OutputPath);
    }

    [Fact]
    public void ParseBuild_OutputIsMissing_ThrowsBuildException()
    {
        Assert.Throws<BuildException>(() => ArgumentsParser.ParseBuild(Array.Empty<string>()));
    }

    [Fact]
    public void ParseVerify_OutputIsMissing_ThrowsBuildException()
    {
        Assert.Throws<BuildException>(() => ArgumentsParser.ParseVerify(Array.Empty<string>()));
    }

    [Fact]
    public void ParseBuild_UnknownOption_ThrowsBuildException()
    {
        var arguments = new[] { "--unknown", "value", "--output", "patches" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseBuild(arguments));
    }

    [Fact]
    public void ParseVerify_SourceIsProvided_ThrowsBuildException()
    {
        var arguments = new[] { "--source", "data", "--output", "patches" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseVerify(arguments));
    }

    [Fact]
    public void Run_CommandIsUnknown_ReturnsNonZeroExitCode()
    {
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Program.Run(new[] { "unknown" }, standardOutput, error);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("알 수 없는 명령", error.ToString(), StringComparison.Ordinal);
    }
}