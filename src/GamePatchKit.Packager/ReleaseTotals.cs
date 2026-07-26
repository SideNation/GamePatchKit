using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

// The PRD's per-release observability numbers, read straight off a validated manifest: overall and per-group
// file counts and bytes plus how many stored objects back them. package, diff and verify all report these, so
// they are counted once here instead of three times at the reporting edge.
public sealed class ReleaseTotals
{
    public int FileCount { get; }

    // Sum of files[].size - the installed size of the release, not what a client downloads.
    public long FileBytes { get; }

    // In the manifest's canonical group order.
    public IReadOnlyList<GroupTotals> Groups { get; }

    public int FileArtifactCount { get; }

    public int BundleArtifactCount { get; }

    // One per whole payload and one per part, matching what actually occupies a storage path.
    public int StoredObjectCount { get; }

    public long StoredObjectBytes { get; }

    private ReleaseTotals(
        int fileCount,
        long fileBytes,
        IReadOnlyList<GroupTotals> groups,
        int fileArtifactCount,
        int bundleArtifactCount,
        int storedObjectCount,
        long storedObjectBytes)
    {
        FileCount = fileCount;
        FileBytes = fileBytes;
        Groups = groups;
        FileArtifactCount = fileArtifactCount;
        BundleArtifactCount = bundleArtifactCount;
        StoredObjectCount = storedObjectCount;
        StoredObjectBytes = storedObjectBytes;
    }

    public static ReleaseTotals Compute(ReleaseManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var fileCountByGroup = new Dictionary<string, int>(StringComparer.Ordinal);
        var fileBytesByGroup = new Dictionary<string, long>(StringComparer.Ordinal);
        int fileCount = 0;
        long fileBytes = 0;

        foreach (ManifestFileEntry file in manifest.Files)
        {
            fileCount++;
            fileBytes += file.Size;
            fileCountByGroup[file.Group] = fileCountByGroup.GetValueOrDefault(file.Group) + 1;
            fileBytesByGroup[file.Group] = fileBytesByGroup.GetValueOrDefault(file.Group) + file.Size;
        }

        var groups = new List<GroupTotals>();

        foreach (ManifestGroupEntry group in manifest.Groups)
        {
            groups.Add(new GroupTotals(
                group.Name,
                group.Required,
                fileCountByGroup.GetValueOrDefault(group.Name),
                fileBytesByGroup.GetValueOrDefault(group.Name)));
        }

        int fileArtifactCount = 0;
        int bundleArtifactCount = 0;
        int storedObjectCount = 0;
        long storedObjectBytes = 0;

        foreach (ManifestArtifact artifact in manifest.Artifacts)
        {
            if (artifact is ManifestArtifact.BundleArtifact)
            {
                bundleArtifactCount++;
            }
            else
            {
                fileArtifactCount++;
            }

            foreach (ArtifactPayloadObject payload in artifact.GetPayloadObjects())
            {
                storedObjectCount++;
                storedObjectBytes += payload.Size;
            }
        }

        return new ReleaseTotals(
            fileCount,
            fileBytes,
            groups,
            fileArtifactCount,
            bundleArtifactCount,
            storedObjectCount,
            storedObjectBytes);
    }
}

public sealed class GroupTotals
{
    public string Name { get; }

    public bool Required { get; }

    public int FileCount { get; }

    public long FileBytes { get; }

    internal GroupTotals(string name, bool required, int fileCount, long fileBytes)
    {
        Name = name;
        Required = required;
        FileCount = fileCount;
        FileBytes = fileBytes;
    }
}
