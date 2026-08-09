namespace GamePatchKit.Cli;

internal sealed record BuildArguments(string SourcePath, string OutputPath);

internal sealed record VerifyArguments(string OutputPath);

internal static class ArgumentsParser
{
    private const string SourceOption = "--source";
    private const string OutputOption = "--output";

    public static BuildArguments ParseBuild(string[] arguments)
    {
        (string? sourcePath, string outputPath) = ParseOptions("build", arguments, acceptsSource: true);
        return new BuildArguments(sourcePath!, outputPath);
    }

    public static VerifyArguments ParseVerify(string[] arguments)
    {
        (_, string outputPath) = ParseOptions("verify", arguments, acceptsSource: false);
        return new VerifyArguments(outputPath);
    }

    private static (string? SourcePath, string OutputPath) ParseOptions(
        string command,
        string[] arguments,
        bool acceptsSource)
    {
        string? sourcePath = acceptsSource ? Directory.GetCurrentDirectory() : null;
        string? outputPath = null;
        bool isSourceSpecified = false;
        bool isOutputSpecified = false;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];

            if (argument == SourceOption)
            {
                if (!acceptsSource)
                {
                    throw new BuildException("verify 명령은 --source 옵션을 받지 않습니다.");
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

            throw new BuildException($"알 수 없는 {command} 옵션입니다: {argument}");
        }

        if (outputPath is null)
        {
            throw new BuildException("--output 옵션을 지정해야 합니다.");
        }

        return (sourcePath, outputPath);
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