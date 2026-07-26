namespace GamePatchKit.Core.Diff
{
    public enum FileChangeKind
    {
        Added,
        Removed,

        // Same path, different fileHash. Takes precedence when the group changed too: the file has to be
        // re-materialized either way, and FileChange still carries both entries so the move stays visible.
        ContentChanged,

        // Same path and fileHash, different group. A purely logical move - the bytes are already correct
        // wherever they are installed - but it changes which download batch the file belongs to.
        GroupMoved,
    }
}
