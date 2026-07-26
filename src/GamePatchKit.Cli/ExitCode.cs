using GamePatchKit.Core.Errors;

namespace GamePatchKit.Cli;

// The CLI's contract with CI. Three distinguishable failure classes, fixed for v1: a job can retry an
// execution failure, must not retry an input error, and has to treat an integrity error as a stop-everything
// signal about published bytes.
public static class ExitCode
{
    public const int Success = 0;

    // The caller asked for something wrong: bad arguments, a gamepatchkit.yml that breaks the input contract,
    // a configuration the schema or the model rejects, a source tree that cannot be packaged as given.
    public const int InputError = 1;

    // Published or supplied bytes do not match what identifies them: a manifestHash that does not match its
    // manifest, a corrupted artifact object, a different payload already sitting on an immutable path.
    public const int IntegrityError = 2;

    // The command could not finish for reasons that are neither: source files changing mid-run, a missing
    // codec, I/O failures, cancellation.
    public const int ExecutionFailure = 3;

    private static readonly HashSet<string> _inputErrorCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        CliErrorCodes.InvalidArguments,
        CliErrorCodes.FileNotFound,
        "packager.invalid-configuration",
        "packager.invalid-input-root",
        "packager.unsupported-entry",
        "packager.package-id-mismatch",
        "packager.manifest-not-found",
        "packager.invalid-signing-key",
    };

    private static readonly HashSet<string> _integrityErrorCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "packager.invalid-previous-manifest",
        "packager.invalid-manifest-document",
        "packager.artifact-corrupted",
        "packager.immutable-path-conflict",
        "packager.manifest-invalid",
        "packager.invalid-signature",
    };

    // Prefixes for the whole families Core reports. A manifest.* or manifest-signature.* code always means
    // published bytes are not what they claim to be; the rest describe input the caller wrote.
    private static readonly string[] _integrityPrefixes = { "manifest.", "manifest-signature." };
    private static readonly string[] _inputPrefixes = { "yaml.", "package-config.", "glob.", "path." };

    // Classified from the first error: the pipeline reports the failure it stopped on, and everything after it
    // in a bundled ValidationResult is another instance of the same class of problem.
    public static int Classify(IReadOnlyList<GamePatchKitError> errors)
    {
        if (errors == null || errors.Count == 0)
        {
            return ExecutionFailure;
        }

        string code = errors[0].Code;

        if (_integrityErrorCodes.Contains(code) || HasPrefix(code, _integrityPrefixes))
        {
            return IntegrityError;
        }

        if (_inputErrorCodes.Contains(code) || HasPrefix(code, _inputPrefixes))
        {
            return InputError;
        }

        return ExecutionFailure;
    }

    private static bool HasPrefix(string code, string[] prefixes)
    {
        return prefixes.Any(prefix => code.StartsWith(prefix, StringComparison.Ordinal));
    }
}
