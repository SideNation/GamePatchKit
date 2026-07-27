using GamePatchKit.Core.Errors;

namespace GamePatchKit.Cli;

// A failure the CLI itself detected: bad arguments, an unreadable file, a gamepatchkit.yml that breaks the
// input contract. Packager failures arrive as PackageException and carry their own errors; both end up in the
// same reported shape.
public sealed class CliException : Exception
{
    public IReadOnlyList<GamePatchKitError> Errors { get; }

    public CliException(string code, string message, string? relativePath = null)
        : this(new[] { new GamePatchKitError("cli", code, message, packageId: null, relativePath) })
    {
    }

    public CliException(IReadOnlyList<GamePatchKitError> errors)
        : base(errors.Count == 0 ? "The command failed." : errors[0].ToString())
    {
        Errors = errors;
    }
}
