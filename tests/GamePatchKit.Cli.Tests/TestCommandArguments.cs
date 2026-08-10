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
    public void ParseUpload_OutputIsMissing_ThrowsBuildException()
    {
        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(Array.Empty<string>()));
    }

    [Fact]
    public void ParseUpload_SourceIsProvided_ThrowsBuildException()
    {
        var arguments = new[] { "--source", "data", "--output", "patches" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseUpload_UnknownOption_ThrowsBuildException()
    {
        var arguments = new[] { "--unknown", "value", "--output", "patches" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseUpload_EnvFileIsMissing_ReturnsNullEnvFilePath()
    {
        var arguments = new[] { "--output", "patches" };

        UploadArguments result = ArgumentsParser.ParseUpload(arguments);

        Assert.Equal("patches", result.OutputPath);
        Assert.Null(result.EnvFilePath);
    }

    [Fact]
    public void ParseUpload_EnvFileIsProvided_ReturnsEnvFilePath()
    {
        var arguments = new[] { "--output", "patches", "--env-file", ".env" };

        UploadArguments result = ArgumentsParser.ParseUpload(arguments);

        Assert.Equal("patches", result.OutputPath);
        Assert.Equal(".env", result.EnvFilePath);
    }

    [Fact]
    public void ParseUpload_EnvFileSpecifiedTwice_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--env-file", ".env", "--env-file", ".env2" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseUpload_OutputSpecifiedTwice_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--output", "patches2" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseUpload_OutputValueIsMissing_ThrowsBuildException()
    {
        var arguments = new[] { "--output" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseUpload_EnvFileValueIsMissing_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--env-file" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseUpload(arguments));
    }

    [Fact]
    public void ParseBuild_EnvFileIsProvided_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--env-file", ".env" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseBuild(arguments));
    }

    [Fact]
    public void ParseVerify_EnvFileIsProvided_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--env-file", ".env" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseVerify(arguments));
    }

    [Fact]
    public async Task Run_CommandIsUnknown_ReturnsNonZeroExitCode()
    {
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(new[] { "unknown" }, standardOutput, error);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("알 수 없는 명령", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_NoCommand_ListsUploadAsAvailableCommand()
    {
        using var standardOutput = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(Array.Empty<string>(), standardOutput, error);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, standardOutput.ToString());
        Assert.Contains("upload", error.ToString(), StringComparison.Ordinal);
    }
}