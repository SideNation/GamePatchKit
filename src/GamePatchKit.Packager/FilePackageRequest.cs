using GamePatchKit.Core.Configuration;

namespace GamePatchKit.Packager;

public sealed class FilePackageRequest
{
    public PackageConfig Config { get; }

    public string OutputRoot { get; }

    public PreviousRelease? PreviousRelease { get; }

    public bool WriteCompressedManifest { get; }

    public string? SourceRevision { get; }

    public FilePackageRequest(
        PackageConfig config,
        string outputRoot,
        PreviousRelease? previousRelease = null,
        bool writeCompressedManifest = false,
        string? sourceRevision = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        OutputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? throw new ArgumentException("Output root must not be empty.", nameof(outputRoot))
            : outputRoot;
        PreviousRelease = previousRelease;
        WriteCompressedManifest = writeCompressedManifest;
        SourceRevision = sourceRevision;
    }
}
