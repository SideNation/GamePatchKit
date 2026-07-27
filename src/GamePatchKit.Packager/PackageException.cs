using GamePatchKit.Core.Errors;

namespace GamePatchKit.Packager;

public sealed class PackageException : Exception
{
    public IReadOnlyList<GamePatchKitError> Errors { get; }

    public PackageException(GamePatchKitError error)
        : this(new[] { error })
    {
    }

    public PackageException(IReadOnlyList<GamePatchKitError> errors)
        : base(CreateMessage(errors))
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));

        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one error is required.", nameof(errors));
        }
    }

    private static string CreateMessage(IReadOnlyList<GamePatchKitError> errors)
    {
        if (errors == null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        return errors.Count == 0 ? "Packaging failed." : errors[0].ToString();
    }
}
