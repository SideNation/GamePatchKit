using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

public sealed class BundleCompactRequest
{
    public PackageConfig Config { get; }

    public string OutputRoot { get; }

    public PreviousRelease SourceRelease { get; }

    public IReadOnlyList<string> TargetGroups { get; }

    public IReadOnlyList<ArtifactPayloadObject> RetainedObjects { get; }

    public bool WriteCompressedManifest { get; }

    public BundleCompactRequest(
        PackageConfig config,
        string outputRoot,
        PreviousRelease sourceRelease,
        IReadOnlyList<string> targetGroups,
        IReadOnlyList<ArtifactPayloadObject> retainedObjects,
        bool writeCompressedManifest = false)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        OutputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? throw new ArgumentException("Output root must not be empty.", nameof(outputRoot))
            : outputRoot;
        SourceRelease = sourceRelease ?? throw new ArgumentNullException(nameof(sourceRelease));
        TargetGroups = targetGroups == null
            ? throw new ArgumentNullException(nameof(targetGroups))
            : targetGroups.ToArray();
        RetainedObjects = retainedObjects == null
            ? throw new ArgumentNullException(nameof(retainedObjects))
            : retainedObjects.ToArray();
        WriteCompressedManifest = writeCompressedManifest;
    }
}