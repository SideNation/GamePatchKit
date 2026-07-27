using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

public sealed class BundleCompactResult
{
    public bool Changed { get; }

    public FinalizedManifest Release { get; }

    public int CreatedBundleArtifactCount { get; }

    public long CreatedBundleArtifactBytes { get; }

    public int CreatedFileArtifactCount { get; }

    public long CreatedFileArtifactBytes { get; }

    public int ReusedFileArtifactCount { get; }

    internal BundleCompactResult(
        bool changed,
        FinalizedManifest release,
        int createdBundleArtifactCount,
        long createdBundleArtifactBytes,
        int createdFileArtifactCount,
        long createdFileArtifactBytes,
        int reusedFileArtifactCount)
    {
        Changed = changed;
        Release = release ?? throw new ArgumentNullException(nameof(release));
        CreatedBundleArtifactCount = createdBundleArtifactCount;
        CreatedBundleArtifactBytes = createdBundleArtifactBytes;
        CreatedFileArtifactCount = createdFileArtifactCount;
        CreatedFileArtifactBytes = createdFileArtifactBytes;
        ReusedFileArtifactCount = reusedFileArtifactCount;
    }
}