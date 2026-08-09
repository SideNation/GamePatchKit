namespace GamePatchKit.Cli;

public static class Program
{
    public static int Main(string[] arguments)
    {
        return Run(arguments, Console.Error);
    }

    internal static int Run(string[] arguments, TextWriter error)
    {
        try
        {
            if (arguments.Length == 0)
            {
                throw new BuildException("명령을 지정해야 합니다. build 또는 verify를 사용하세요.");
            }

            string command = arguments[0];
            string[] commandArguments = arguments[1..];

            switch (command)
            {
                case "build":
                    new BuildCommand().Execute(ArgumentsParser.ParseBuild(commandArguments));
                    break;
                case "verify":
                    new VerifyCommand().Execute(ArgumentsParser.ParseVerify(commandArguments));
                    break;
                default:
                    throw new BuildException($"알 수 없는 명령입니다: {command}");
            }

            return 0;
        }
        catch (BuildException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }
}