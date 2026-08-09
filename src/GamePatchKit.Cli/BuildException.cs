namespace GamePatchKit.Cli;

internal sealed class BuildException : Exception
{
    public BuildException(string message)
        : base(message)
    {
    }
}