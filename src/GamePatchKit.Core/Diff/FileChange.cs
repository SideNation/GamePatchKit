using System;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Diff
{
    // One logical file difference between two releases. Source is null for Added and Target is null for
    // Removed; both sides are present otherwise, so a caller reads the old and new group, size, fileHash and
    // artifact reference straight off the change instead of looking them up again.
    public sealed class FileChange
    {
        public FileChangeKind Kind { get; }

        public ManifestFileEntry? Source { get; }

        public ManifestFileEntry? Target { get; }

        public string Path => Target?.Path ?? Source!.Path;

        public FileChange(FileChangeKind kind, ManifestFileEntry? source, ManifestFileEntry? target)
        {
            if (source == null && target == null)
            {
                throw new ArgumentException("A file change must have a source, a target, or both.", nameof(target));
            }

            Kind = kind;
            Source = source;
            Target = target;
        }
    }
}
