namespace GamePatchKit.Core.Diff
{
    // Stored bytes are immutable, so there is no "modified" kind: an object either exists at a path or does
    // not. A release pair that would need one is rejected by ReleaseStorageCompatibility before it reaches a
    // diff.
    public enum ArtifactChangeKind
    {
        Added,
        Removed,
    }
}
