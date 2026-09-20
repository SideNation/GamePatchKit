namespace GamePatchKit.Cli;

internal sealed record BuildArguments(string SourcePath, string OutputPath, string? ConfigurationPath = null);

internal sealed record VerifyArguments(string OutputPath);

internal sealed record UploadArguments(string OutputPath, string? EnvFilePath);

internal sealed record SyncArguments(string OutputPath, string? EnvFilePath);

internal sealed record DeployFunctionArguments(string ProjectId, string AccessToken)
{
    public override string ToString()
    {
        return "DeployFunctionArguments { AccessToken = [redacted] }";
    }
}

internal static class ArgumentsParser
{
    private const string SourceOption = "--source";
    private const string OutputOption = "--output";
    private const string ConfigurationOption = "--config";
    private const string EnvFileOption = "--env-file";
    private const string ProjectIdOption = "--project-id";
    private const string AccessTokenOption = "--access-token";
    private const int ProjectRefLength = 20;

    public static BuildArguments ParseBuild(string[] arguments)
    {
        (string? sourcePath, string outputPath, _, string? configurationPath) = ParseOptions(
            "build",
            arguments,
            acceptsSource: true,
            acceptsEnvFile: false,
            acceptsConfiguration: true);
        return new BuildArguments(sourcePath!, outputPath, configurationPath);
    }

    public static VerifyArguments ParseVerify(string[] arguments)
    {
        (_, string outputPath, _, _) = ParseOptions("verify", arguments, acceptsSource: false, acceptsEnvFile: false);
        return new VerifyArguments(outputPath);
    }

    public static UploadArguments ParseUpload(string[] arguments)
    {
        (_, string outputPath, string? envFilePath, _) = ParseOptions(
            "upload",
            arguments,
            acceptsSource: false,
            acceptsEnvFile: true);
        return new UploadArguments(outputPath, envFilePath);
    }

    public static SyncArguments ParseSync(string[] arguments)
    {
        (_, string outputPath, string? envFilePath, _) = ParseOptions(
            "sync",
            arguments,
            acceptsSource: false,
            acceptsEnvFile: true);
        return new SyncArguments(outputPath, envFilePath);
    }

    public static DeployFunctionArguments ParseDeployFunction(string[] arguments)
    {
        string? projectId = null;
        string? accessToken = null;

        for (int index = 0; index < arguments.Length; index += 2)
        {
            string option = arguments[index];

            if (option is not (ProjectIdOption or AccessTokenOption))
            {
                throw new BuildException("알 수 없는 deploy-function 옵션입니다.");
            }

            if ((option == ProjectIdOption && projectId is not null) || (option == AccessTokenOption && accessToken is not null))
            {
                throw new BuildException($"{option} 옵션을 두 번 지정할 수 없습니다.");
            }

            if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new BuildException($"{option} 옵션의 값을 지정해야 합니다.");
            }

            if (option == ProjectIdOption)
            {
                projectId = arguments[index + 1];
            }
            else
            {
                accessToken = arguments[index + 1];
            }
        }

        if (projectId is null || projectId.Length != ProjectRefLength || projectId.Any(character => character is < 'a' or > 'z'))
        {
            throw new BuildException("--project-id에 소문자 영문 20자로 된 project ref를 지정해야 합니다.");
        }

        if (accessToken is null || accessToken.Any(character => character <= ' ' || character > '~'))
        {
            throw new BuildException("--access-token에 공백 없는 Access Token을 지정해야 합니다.");
        }

        return new DeployFunctionArguments(projectId, accessToken);
    }

    private static (string? SourcePath, string OutputPath, string? EnvFilePath, string? ConfigurationPath) ParseOptions(
        string command,
        string[] arguments,
        bool acceptsSource,
        bool acceptsEnvFile,
        bool acceptsConfiguration = false)
    {
        string? sourcePath = acceptsSource ? Directory.GetCurrentDirectory() : null;
        string? outputPath = null;
        string? envFilePath = null;
        string? configurationPath = null;
        bool isSourceSpecified = false;
        bool isOutputSpecified = false;
        bool isEnvFileSpecified = false;
        bool isConfigurationSpecified = false;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];

            if (argument == SourceOption)
            {
                if (!acceptsSource)
                {
                    throw new BuildException($"{command} 명령은 --source 옵션을 받지 않습니다.");
                }

                if (isSourceSpecified)
                {
                    throw new BuildException("--source 옵션을 두 번 지정할 수 없습니다.");
                }

                sourcePath = ReadValue(arguments, ref index, SourceOption);
                isSourceSpecified = true;
                continue;
            }

            if (argument == OutputOption)
            {
                if (isOutputSpecified)
                {
                    throw new BuildException("--output 옵션을 두 번 지정할 수 없습니다.");
                }

                outputPath = ReadValue(arguments, ref index, OutputOption);
                isOutputSpecified = true;
                continue;
            }

            if (argument == EnvFileOption)
            {
                if (!acceptsEnvFile)
                {
                    throw new BuildException($"{command} 명령은 --env-file 옵션을 받지 않습니다.");
                }

                if (isEnvFileSpecified)
                {
                    throw new BuildException("--env-file 옵션을 두 번 지정할 수 없습니다.");
                }

                envFilePath = ReadValue(arguments, ref index, EnvFileOption);
                isEnvFileSpecified = true;
                continue;
            }

            if (argument == ConfigurationOption)
            {
                if (!acceptsConfiguration)
                {
                    throw new BuildException($"{command} 명령은 --config 옵션을 받지 않습니다.");
                }

                if (isConfigurationSpecified)
                {
                    throw new BuildException("--config 옵션을 두 번 지정할 수 없습니다.");
                }

                configurationPath = ReadValue(arguments, ref index, ConfigurationOption);
                isConfigurationSpecified = true;
                continue;
            }

            throw new BuildException($"알 수 없는 {command} 옵션입니다: {argument}");
        }

        if (outputPath is null)
        {
            throw new BuildException("--output 옵션을 지정해야 합니다.");
        }

        return (sourcePath, outputPath, envFilePath, configurationPath);
    }

    private static string ReadValue(string[] arguments, ref int index, string option)
    {
        int valueIndex = index + 1;

        if (valueIndex >= arguments.Length || arguments[valueIndex].StartsWith("--", StringComparison.Ordinal))
        {
            throw new BuildException($"{option} 옵션의 경로를 지정해야 합니다.");
        }

        string value = arguments[valueIndex];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BuildException($"{option} 옵션의 경로를 지정해야 합니다.");
        }

        index = valueIndex;
        return value;
    }
}