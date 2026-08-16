namespace GamePatchKit.Cli;

internal sealed record BuildArguments(string SourcePath, string OutputPath);

internal sealed record VerifyArguments(string OutputPath);

internal sealed record UploadArguments(string OutputPath, string? EnvFilePath);

internal sealed record SyncArguments(string OutputPath, string? EnvFilePath);

internal static class ArgumentsParser
{
    private const string SourceOption = "--source";
    private const string OutputOption = "--output";
    private const string EnvFileOption = "--env-file";

    public static BuildArguments ParseBuild(string[] arguments)
    {
        (string? sourcePath, string outputPath, _) = ParseOptions(
            "build",
            arguments,
            acceptsSource: true,
            acceptsEnvFile: false);
        return new BuildArguments(sourcePath!, outputPath);
    }

    public static VerifyArguments ParseVerify(string[] arguments)
    {
        (_, string outputPath, _) = ParseOptions("verify", arguments, acceptsSource: false, acceptsEnvFile: false);
        return new VerifyArguments(outputPath);
    }

    public static UploadArguments ParseUpload(string[] arguments)
    {
        (_, string outputPath, string? envFilePath) = ParseOptions(
            "upload",
            arguments,
            acceptsSource: false,
            acceptsEnvFile: true);
        return new UploadArguments(outputPath, envFilePath);
    }

    public static SyncArguments ParseSync(string[] arguments)
    {
        (_, string outputPath, string? envFilePath) = ParseOptions(
            "sync",
            arguments,
            acceptsSource: false,
            acceptsEnvFile: true);
        return new SyncArguments(outputPath, envFilePath);
    }

    private static (string? SourcePath, string OutputPath, string? EnvFilePath) ParseOptions(
        string command,
        string[] arguments,
        bool acceptsSource,
        bool acceptsEnvFile)
    {
        string? sourcePath = acceptsSource ? Directory.GetCurrentDirectory() : null;
        string? outputPath = null;
        string? envFilePath = null;
        bool isSourceSpecified = false;
        bool isOutputSpecified = false;
        bool isEnvFileSpecified = false;

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

            throw new BuildException($"알 수 없는 {command} 옵션입니다: {argument}");
        }

        if (outputPath is null)
        {
            throw new BuildException("--output 옵션을 지정해야 합니다.");
        }

        return (sourcePath, outputPath, envFilePath);
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