using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Diff
{
    // Logical and physical differences between two releases of one package, kept separate on purpose: a
    // release re-packaged with different compression or bundle boundaries has physical changes and no logical
    // ones, while a group move is logical with no physical change at all.
    //
    // Both lists are sorted by path in ordinal UTF-8 byte order, so the same manifest pair always produces the
    // same diff regardless of how the manifests were built.
    public sealed class ReleaseDiff
    {
        public IReadOnlyList<FileChange> FileChanges { get; }

        public IReadOnlyList<ArtifactChange> ArtifactChanges { get; }

        private ReleaseDiff(IReadOnlyList<FileChange> fileChanges, IReadOnlyList<ArtifactChange> artifactChanges)
        {
            FileChanges = fileChanges;
            ArtifactChanges = artifactChanges;
        }

        public static ReleaseDiff Compute(ReleaseManifest source, ReleaseManifest target)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            // Content-addressed paths are package-scoped and a package's files[] only describe its own data, so
            // comparing two different packages would produce differences that mean nothing.
            if (!string.Equals(source.PackageId, target.PackageId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Cannot diff releases of different packages ('{source.PackageId}' and '{target.PackageId}').",
                    nameof(target));
            }

            // Two releases that disagree about the bytes at one storage path cannot both exist, so there is no
            // honest diff to report between them - one of the two manifests is wrong. This is a statement about
            // these two releases only; whether a release is safe to publish depends on everything the package
            // still stores, which is ReleaseStorageCompatibility's inventory overload.
            ValidationResult compatibility = ReleaseStorageCompatibility.Validate(source, target);

            if (!compatibility.IsValid)
            {
                throw new ArgumentException(
                    "Releases cannot be diffed because they disagree about the bytes at a shared storage path: " + string.Join("; ", compatibility.Errors),
                    nameof(target));
            }

            return new ReleaseDiff(ComputeFileChanges(source, target), ComputeArtifactChanges(source, target));
        }

        private static List<FileChange> ComputeFileChanges(ReleaseManifest source, ReleaseManifest target)
        {
            var sourceByPath = new Dictionary<string, ManifestFileEntry>(StringComparer.Ordinal);

            foreach (ManifestFileEntry file in source.Files)
            {
                sourceByPath[file.Path] = file;
            }

            var changes = new List<FileChange>();
            var matchedPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (ManifestFileEntry targetFile in target.Files)
            {
                if (!sourceByPath.TryGetValue(targetFile.Path, out ManifestFileEntry? sourceFile))
                {
                    changes.Add(new FileChange(FileChangeKind.Added, null, targetFile));
                    continue;
                }

                matchedPaths.Add(targetFile.Path);

                if (!string.Equals(sourceFile.FileHash, targetFile.FileHash, StringComparison.Ordinal))
                {
                    changes.Add(new FileChange(FileChangeKind.ContentChanged, sourceFile, targetFile));
                }
                else if (!string.Equals(sourceFile.Group, targetFile.Group, StringComparison.Ordinal))
                {
                    // Reported even when the artifact reference was reused unchanged: the bytes did not move,
                    // but which group has to be installed for this file did.
                    changes.Add(new FileChange(FileChangeKind.GroupMoved, sourceFile, targetFile));
                }
            }

            foreach (ManifestFileEntry sourceFile in source.Files)
            {
                if (!matchedPaths.Contains(sourceFile.Path))
                {
                    changes.Add(new FileChange(FileChangeKind.Removed, sourceFile, null));
                }
            }

            // Every path appears at most once across the four kinds, so ordering by path is a total order.
            changes.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Path, right.Path));

            return changes;
        }

        // Path is a sufficient key here only because Compute has already rejected releases that disagree about
        // the bytes at a shared path: what is left is objects that exist in one release and not the other.
        private static List<ArtifactChange> ComputeArtifactChanges(ReleaseManifest source, ReleaseManifest target)
        {
            HashSet<string> sourcePaths = CollectPayloadPaths(source);
            HashSet<string> targetPaths = CollectPayloadPaths(target);

            var changes = new List<ArtifactChange>();

            foreach (ArtifactPayloadObject payload in target.EnumeratePayloadObjects())
            {
                if (!sourcePaths.Contains(payload.Path))
                {
                    changes.Add(new ArtifactChange(ArtifactChangeKind.Added, payload));
                }
            }

            foreach (ArtifactPayloadObject payload in source.EnumeratePayloadObjects())
            {
                if (!targetPaths.Contains(payload.Path))
                {
                    changes.Add(new ArtifactChange(ArtifactChangeKind.Removed, payload));
                }
            }

            changes.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Payload.Path, right.Payload.Path));

            return changes;
        }

        private static HashSet<string> CollectPayloadPaths(ReleaseManifest manifest)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            foreach (ArtifactPayloadObject payload in manifest.EnumeratePayloadObjects())
            {
                paths.Add(payload.Path);
            }

            return paths;
        }
    }
}
