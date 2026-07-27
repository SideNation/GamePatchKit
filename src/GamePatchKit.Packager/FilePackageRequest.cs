using GamePatchKit.Core.Configuration;

namespace GamePatchKit.Packager;

public sealed class FilePackageRequest
{
    public PackageConfig Config { get; }

    public string OutputRoot { get; }

    public PreviousRelease? PreviousRelease { get; }

    public bool WriteCompressedManifest { get; }

    public string? SourceRevision { get; }

    // Build the release and report it, but publish nothing: staging is still used and still discarded, so the
    // output tree is byte-for-byte what it was. The identity in the result is the real one - artifacts have to
    // be produced to know their hashes - but the published-byte verification that follows a real publish is
    // skipped, because the artifacts it would read were never moved into place.
    public bool DryRun { get; }

    public FilePackageRequest(
        PackageConfig config,
        string outputRoot,
        PreviousRelease? previousRelease = null,
        bool writeCompressedManifest = false,
        string? sourceRevision = null,
        bool dryRun = false)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        OutputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? throw new ArgumentException("Output root must not be empty.", nameof(outputRoot))
            : outputRoot;
        PreviousRelease = previousRelease;
        WriteCompressedManifest = writeCompressedManifest;
        SourceRevision = sourceRevision;
        DryRun = dryRun;
    }
}
