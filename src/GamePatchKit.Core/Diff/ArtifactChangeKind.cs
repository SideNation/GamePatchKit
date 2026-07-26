namespace GamePatchKit.Core.Diff
{
    // A stored object is identified by its path and its own digest together, so there is no "modified" kind:
    // when a payload is re-split and different bytes land at the same part path, that path shows up once as
    // Removed and once as Added.
    public enum ArtifactChangeKind
    {
        Added,
        Removed,
    }
}
