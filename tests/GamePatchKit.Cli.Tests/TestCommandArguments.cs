using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestCommandArguments
{
    private const string ProjectId = "abcdefghijklmnopqrst";
    private const string AccessToken = "sbp_do_not_print_this_token";

    public static TheoryData<string[]> InvalidDeployArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--project-id", ProjectId },
        new[] { "--access-token", AccessToken },
        new[] { "--project-id", ProjectId, "--access-token" },
        new[] { "--project-id", "--access-token", AccessToken },
        new[] { "--project-id", ProjectId, "--access-token", "" },
        new[] { "--project-id", ProjectId, "--access-token", " " },
        new[] { "--project-id", ProjectId, "--access-token", "sbp_token\r\nheader" },
        new[] { "--project-id", ProjectId, "--access-token", "sbp_token value" },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, "--project-id", ProjectId },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, "--access-token", AccessToken },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, "--output", "patches" },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, "--source", "data" },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, "--env-file", ".env" },
        new[] { "--project-id", ProjectId, "--access-token", AccessToken, AccessToken },
        new[] { "--project-id", ProjectId, $"--access-token={AccessToken}" }
    };

    [Fact]
    public void ParseDeployFunction_ValidArguments_ReturnsProjectAndTokenWithoutEchoingToken()
    {
        // Arrange
        string[] arguments = ["--access-token", AccessToken, "--project-id", ProjectId];

        // Act
        DeployFunctionArguments result = ArgumentsParser.ParseDeployFunction(arguments);

        // Assert
        Assert.Equal(ProjectId, result.ProjectId);
        Assert.Equal(AccessToken, result.AccessToken);
        Assert.DoesNotContain(AccessToken, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("project-name")]
    [InlineData("https://abcdefghijklmnopqrst.supabase.co")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    [InlineData("abcdefghijklmnopqrs1")]
    [InlineData("abcdefghijklmnopqrs")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("../abcdefghijklmnopq")]
    public void ParseDeployFunction_InvalidProjectRef_ThrowsWithoutEchoingInput(string projectId)
    {
        // Arrange
        string[] arguments = ["--project-id", projectId, "--access-token", AccessToken];

        // Act
        BuildException exception = Assert.Throws<BuildException>(() => ArgumentsParser.ParseDeployFunction(arguments));

        // Assert
        Assert.DoesNotContain(AccessToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidDeployArguments))]
    public async Task RunAsync_InvalidDeployArguments_ReturnsOneWithoutEchoingToken(string[] arguments)
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        int exitCode = await Program.RunAsync(["deploy-function", .. arguments], output, error);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain(AccessToken, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("네트워크", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("verify")]
    [InlineData("upload")]
    [InlineData("sync")]
    public async Task RunAsync_DeployOptionOnExistingCommand_IsRejected(string command)
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        int exitCode = await Program.RunAsync([command, "--output", "patches", "--project-id", ProjectId], output, error);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("옵션", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseBuild_SourceIsMissing_UsesCurrentDirectory()
    {
        var arguments = new[] { "--output", "patches" };

        BuildArguments result = ArgumentsParser.ParseBuild(arguments);

        Assert.Equal(Directory.GetCurrentDirectory(), result.SourcePath);
        Assert.Equal("patches", result.OutputPath);
        Assert.Null(result.ConfigurationPath);
    }

    [Fact]
    public void ParseBuild_ConfigurationIsProvided_ReturnsConfigurationPath()
    {
        var arguments = new[]
        {
            "--config",
            "config/shared.yml",
            "--output",
            "patches",
            "--source",
            "data"
        };

        BuildArguments result = ArgumentsParser.ParseBuild(arguments);

        Assert.Equal("data", result.SourcePath);
        Assert.Equal("patches", result.OutputPath);
        Assert.Equal("config/shared.yml", result.ConfigurationPath);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("verify")]
    [InlineData("upload")]
    [InlineData("sync")]
    [InlineData("deploy-function")]
    public void Parse_ConfigIsInvalid_ThrowsBuildException(string command)
    {
        string[] arguments = command == "build"
            ? new[] { "--output", "patches", "--config", "one.yml", "--config", "two.yml" }
            : command == "deploy-function"
                ? new[] { "--config", "config.yml" }
                : new[] { "--output", "patches", "--config", "config.yml" };

        Action parse = command switch
        {
            "build" => () => ArgumentsParser.ParseBuild(arguments),
            "verify" => () => ArgumentsParser.ParseVerify(arguments),
            "upload" => () => ArgumentsParser.ParseUpload(arguments),
            "sync" => () => ArgumentsParser.ParseSync(arguments),
            "deploy-function" => () => ArgumentsParser.ParseDeployFunction(arguments),
            _ => throw new InvalidOperationException()
        };

        Assert.Throws<BuildException>(parse);
    }

    [Fact]
    public void ParseBuild_ConfigurationValueIsMissing_ThrowsBuildException()
    {
        var arguments = new[] { "--output", "patches", "--config" };

        Assert.Throws<BuildException>(() => ArgumentsParser.ParseBuild(arguments));
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