using System.Collections.ObjectModel;

namespace GamePatchKit.Packager;

public sealed class PackageBuildReport
{
    public DateTimeOffset CreatedAtUtc { get; }

    public string MachineName { get; }

    public string? SourceRevision { get; }

    public string DataVersion { get; }

    public long CompactVersion { get; }

    public string ManifestHash { get; }

    public IReadOnlyList<string> AddedFiles { get; }

    public IReadOnlyList<string> ChangedFiles { get; }

    public IReadOnlyList<string> DeletedFiles { get; }

    public IReadOnlyList<string> MovedGroupFiles { get; }

    public int CreatedFileArtifactCount { get; }

    public long CreatedFileArtifactBytes { get; }

    public int ReusedFileArtifactCount { get; }

    public int CreatedBundleArtifactCount { get; }

    public long CreatedBundleArtifactBytes { get; }

    public int ReusedBundleArtifactCount { get; }

    public IReadOnlyDictionary<string, string> CompressionPolicies { get; }

    internal PackageBuildReport(
        DateTimeOffset createdAtUtc,
        string machineName,
        string? sourceRevision,
        string dataVersion,
        long compactVersion,
        string manifestHash,
        IReadOnlyList<string> addedFiles,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<string> deletedFiles,
        IReadOnlyList<string> movedGroupFiles,
        int createdFileArtifactCount,
        long createdFileArtifactBytes,
        int reusedFileArtifactCount,
        int createdBundleArtifactCount,
        long createdBundleArtifactBytes,
        int reusedBundleArtifactCount,
        IReadOnlyDictionary<string, string> compressionPolicies)
    {
        CreatedAtUtc = createdAtUtc;
        MachineName = machineName;
        SourceRevision = sourceRevision;
        DataVersion = dataVersion;
        CompactVersion = compactVersion;
        ManifestHash = manifestHash;
        AddedFiles = addedFiles.ToArray();
        ChangedFiles = changedFiles.ToArray();
        DeletedFiles = deletedFiles.ToArray();
        MovedGroupFiles = movedGroupFiles.ToArray();
        CreatedFileArtifactCount = createdFileArtifactCount;
        CreatedFileArtifactBytes = createdFileArtifactBytes;
        ReusedFileArtifactCount = reusedFileArtifactCount;
        CreatedBundleArtifactCount = createdBundleArtifactCount;
        CreatedBundleArtifactBytes = createdBundleArtifactBytes;
        ReusedBundleArtifactCount = reusedBundleArtifactCount;
        CompressionPolicies = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(compressionPolicies, StringComparer.Ordinal));
    }
}