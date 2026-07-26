using System;
using System.Collections.Generic;
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

        private static List<ArtifactChange> ComputeArtifactChanges(ReleaseManifest source, ReleaseManifest target)
        {
            HashSet<(string Path, string ObjectHash)> sourceObjects = CollectPayloadIdentities(source);
            HashSet<(string Path, string ObjectHash)> targetObjects = CollectPayloadIdentities(target);

            var changes = new List<ArtifactChange>();

            foreach (ArtifactPayloadObject payload in EnumeratePayloadObjects(target))
            {
                if (!sourceObjects.Contains((payload.Path, payload.ObjectHash)))
                {
                    changes.Add(new ArtifactChange(ArtifactChangeKind.Added, payload));
                }
            }

            foreach (ArtifactPayloadObject payload in EnumeratePayloadObjects(source))
            {
                if (!targetObjects.Contains((payload.Path, payload.ObjectHash)))
                {
                    changes.Add(new ArtifactChange(ArtifactChangeKind.Removed, payload));
                }
            }

            // Path alone is not a total key: a payload re-split under a different maxArtifactBytes puts
            // different bytes at the same part path, which is reported as that path being both removed and
            // added. (Path, ObjectHash) is total, because an object present in both releases is in neither list.
            changes.Sort(CompareByPathThenObjectHash);

            return changes;
        }

        private static int CompareByPathThenObjectHash(ArtifactChange left, ArtifactChange right)
        {
            int byPath = Utf8OrdinalStringComparer.Instance.Compare(left.Payload.Path, right.Payload.Path);

            return byPath != 0 ? byPath : string.CompareOrdinal(left.Payload.ObjectHash, right.Payload.ObjectHash);
        }

        // Identity is path plus the object's own digest. Comparing paths alone would call a release whose parts
        // were re-split at the same boundaries "physically unchanged" even though every stored byte moved.
        private static HashSet<(string Path, string ObjectHash)> CollectPayloadIdentities(ReleaseManifest manifest)
        {
            var identities = new HashSet<(string Path, string ObjectHash)>();

            foreach (ArtifactPayloadObject payload in EnumeratePayloadObjects(manifest))
            {
                identities.Add((payload.Path, payload.ObjectHash));
            }

            return identities;
        }

        private static IEnumerable<ArtifactPayloadObject> EnumeratePayloadObjects(ReleaseManifest manifest)
        {
            foreach (ManifestArtifact artifact in manifest.Artifacts)
            {
                foreach (ArtifactPayloadObject payload in artifact.GetPayloadObjects())
                {
                    yield return payload;
                }
            }
        }
    }
}
