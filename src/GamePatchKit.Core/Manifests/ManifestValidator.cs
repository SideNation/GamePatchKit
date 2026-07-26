using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Manifests
{
    // Layer-2 semantic validation: everything release-manifest.schema.json cannot express itself
    // (cross-field references, uniqueness, canonical ordering, content-addressed path reconstruction).
    // Filesystem-independent: never touches actual artifact bytes (that is Packager/Runtime 'verify').
    public static class ManifestValidator
    {
        private const string Stage = "release-manifest";

        public static ValidationResult Validate(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var errors = new List<GamePatchKitError>();

            ValidateGroups(manifest, errors, out HashSet<string> groupNames);
            ValidateFiles(manifest, groupNames, errors);
            ValidateArtifacts(
                manifest,
                errors,
                out Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
                out Dictionary<(string Group, string Hash), ManifestArtifact.BundleArtifact> bundleArtifactsByGroupAndHash);
            ValidateReferences(manifest, fileArtifactsByHash, bundleArtifactsByGroupAndHash, errors);

            return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
        }

        private static void ValidateGroups(ReleaseManifest manifest, List<GamePatchKitError> errors, out HashSet<string> groupNames)
        {
            groupNames = new HashSet<string>(StringComparer.Ordinal);
            string? previousName = null;

            foreach (ManifestGroupEntry group in manifest.Groups)
            {
                if (!groupNames.Add(group.Name))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateGroupName, $"Group '{group.Name}' is declared more than once.", packageId: manifest.PackageId, group: group.Name));
                }

                if (previousName != null && Utf8OrdinalStringComparer.Instance.Compare(previousName, group.Name) > 0)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnsortedGroups, "groups[] must be sorted by name in ascending ordinal UTF-8 byte order.", packageId: manifest.PackageId));
                }

                previousName = group.Name;
            }
        }

        private static void ValidateFiles(ReleaseManifest manifest, HashSet<string> groupNames, List<GamePatchKitError> errors)
        {
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            string? previousPath = null;

            foreach (ManifestFileEntry file in manifest.Files)
            {
                if (!RelativePathNormalizer.TryNormalize(file.Path, out string normalized, out string pathErrorCode) || normalized != file.Path)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.NonCanonicalPath, "File path is not already in normalized canonical form.", packageId: manifest.PackageId, relativePath: file.Path));
                }

                if (!seenPaths.Add(file.Path))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateFilePath, $"File path '{file.Path}' is declared more than once.", packageId: manifest.PackageId, relativePath: file.Path));
                }

                if (previousPath != null && Utf8OrdinalStringComparer.Instance.Compare(previousPath, file.Path) > 0)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnsortedFiles, "files[] must be sorted by normalized path in ascending ordinal UTF-8 byte order.", packageId: manifest.PackageId));
                }

                previousPath = file.Path;

                if (!groupNames.Contains(file.Group))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownFileGroup, $"File references undeclared group '{file.Group}'.", packageId: manifest.PackageId, relativePath: file.Path, group: file.Group));
                }
            }

            var allPaths = new List<string>(manifest.Files.Count);
            foreach (ManifestFileEntry file in manifest.Files)
            {
                allPaths.Add(file.Path);
            }

            foreach (IReadOnlyList<string> duplicateGroup in RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(allPaths))
            {
                if (duplicateGroup.Distinct(StringComparer.Ordinal).Count() < 2)
                {
                    continue;
                }

                string joined = string.Join(", ", duplicateGroup);
                errors.Add(new GamePatchKitError(
                    Stage,
                    ManifestErrorCodes.CaseInsensitiveDuplicateFilePath,
                    $"File paths collide under OS-independent case-insensitive comparison: {joined}",
                    packageId: manifest.PackageId));
            }
        }

        private static void ValidateArtifacts(
            ReleaseManifest manifest,
            List<GamePatchKitError> errors,
            out Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
            out Dictionary<(string Group, string Hash), ManifestArtifact.BundleArtifact> bundleArtifactsByGroupAndHash)
        {
            fileArtifactsByHash = new Dictionary<string, ManifestArtifact.FileArtifact>(StringComparer.Ordinal);
            bundleArtifactsByGroupAndHash = new Dictionary<(string, string), ManifestArtifact.BundleArtifact>();
            string? previousSortKey = null;

            foreach (ManifestArtifact artifact in manifest.Artifacts)
            {
                string sortKey = artifact.ContentAddressedSortKey(manifest.PackageId);

                if (previousSortKey != null && Utf8OrdinalStringComparer.Instance.Compare(previousSortKey, sortKey) > 0)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnsortedArtifacts, "artifacts[] must be sorted by content-addressed path in ascending ordinal UTF-8 byte order.", packageId: manifest.PackageId));
                }

                previousSortKey = sortKey;

                if (artifact is ManifestArtifact.FileArtifact fileArtifact)
                {
                    ValidateFileArtifact(manifest, fileArtifact, errors);

                    if (!fileArtifactsByHash.TryAdd(fileArtifact.PrimaryArtifactHash, fileArtifact))
                    {
                        errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateArtifactIdentity, $"File artifact hash '{fileArtifact.PrimaryArtifactHash}' is declared more than once.", packageId: manifest.PackageId));
                    }
                }
                else if (artifact is ManifestArtifact.BundleArtifact bundleArtifact)
                {
                    ValidateBundleArtifact(manifest, bundleArtifact, errors);

                    var key = (bundleArtifact.Group, bundleArtifact.ArtifactHash);
                    if (!bundleArtifactsByGroupAndHash.TryAdd(key, bundleArtifact))
                    {
                        errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateArtifactIdentity, $"Bundle artifact hash '{bundleArtifact.ArtifactHash}' in group '{bundleArtifact.Group}' is declared more than once.", packageId: manifest.PackageId, group: bundleArtifact.Group));
                    }
                }
            }
        }

        private static void ValidateFileArtifact(ReleaseManifest manifest, ManifestArtifact.FileArtifact artifact, List<GamePatchKitError> errors)
        {
            if (artifact.Payload is FilePayload.Single single)
            {
                string expectedPath = ContentAddressedPath.FileSinglePayloadPath(manifest.PackageId, single.ArtifactHash, artifact.Compression);
                if (single.Path != expectedPath)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.ArtifactPathMismatch, $"Expected single-payload path '{expectedPath}', found '{single.Path}'.", packageId: manifest.PackageId));
                }

                return;
            }

            var parts = (FilePayload.Parts)artifact.Payload;
            long sizeSum = 0;
            bool sizeSumOverflowed = false;
            var seenPartPaths = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < parts.PartList.Count; i++)
            {
                FilePart part = parts.PartList[i];

                // Every part.Size is already bounded to [0, MaxSafeInteger] by FilePart.TryParse, so this
                // guard (rather than checked/unchecked long addition) cannot itself under/overflow; without
                // it, thousands of near-MaxSafeInteger parts could silently wrap sizeSum past long.MaxValue.
                if (!sizeSumOverflowed && part.Size > JsonNumbers.MaxSafeInteger - sizeSum)
                {
                    sizeSumOverflowed = true;
                }
                else if (!sizeSumOverflowed)
                {
                    sizeSum += part.Size;
                }

                if (part.Index != i)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.NonContiguousPartIndex, $"Part index {part.Index} at array position {i} is not contiguous from 0.", packageId: manifest.PackageId));
                }

                if (!seenPartPaths.Add(part.Path))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicatePartPath, $"Part path '{part.Path}' is declared more than once.", packageId: manifest.PackageId));
                }

                string expectedPartPath = ContentAddressedPath.FilePartPath(manifest.PackageId, parts.ArtifactHash, part.Index);
                if (part.Path != expectedPartPath)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.ArtifactPathMismatch, $"Expected part path '{expectedPartPath}', found '{part.Path}'.", packageId: manifest.PackageId));
                }
            }

            if (sizeSumOverflowed || sizeSum != parts.Size)
            {
                errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.PartSizeSumMismatch, $"Sum of part sizes does not equal payload size ({parts.Size}).", packageId: manifest.PackageId));
            }
        }

        private static void ValidateBundleArtifact(ReleaseManifest manifest, ManifestArtifact.BundleArtifact artifact, List<GamePatchKitError> errors)
        {
            string expectedPath = ContentAddressedPath.BundleArtifactPath(manifest.PackageId, artifact.Group, artifact.ArtifactHash, artifact.Compression);
            if (artifact.Path != expectedPath)
            {
                errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.ArtifactPathMismatch, $"Expected bundle path '{expectedPath}', found '{artifact.Path}'.", packageId: manifest.PackageId, group: artifact.Group));
            }

            string? previousEntryPath = null;
            var seenEntryPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (BundleEntry entry in artifact.Entries)
            {
                if (!RelativePathNormalizer.TryNormalize(entry.Path, out string normalized, out string pathErrorCode) || normalized != entry.Path)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.NonCanonicalPath, "Bundle entry path is not already in normalized canonical form.", packageId: manifest.PackageId, relativePath: entry.Path, group: artifact.Group));
                }

                if (!seenEntryPaths.Add(entry.Path))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateBundleEntryPath, $"Bundle entry path '{entry.Path}' is declared more than once in this bundle.", packageId: manifest.PackageId, relativePath: entry.Path, group: artifact.Group));
                }

                if (previousEntryPath != null && Utf8OrdinalStringComparer.Instance.Compare(previousEntryPath, entry.Path) > 0)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnsortedBundleEntries, "Bundle entries[] must be sorted by path in ascending ordinal UTF-8 byte order.", packageId: manifest.PackageId, group: artifact.Group));
                }

                previousEntryPath = entry.Path;
            }
        }

        private static void ValidateReferences(
            ReleaseManifest manifest,
            Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
            Dictionary<(string Group, string Hash), ManifestArtifact.BundleArtifact> bundleArtifactsByGroupAndHash,
            List<GamePatchKitError> errors)
        {
            var fileArtifactReferenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var bundleEntryReferenceCounts = new Dictionary<(string Group, string Hash, string EntryPath), int>();
            var fileArtifactContentByHash = new Dictionary<string, (long Size, string FileHash)>(StringComparer.Ordinal);

            foreach (ManifestFileEntry file in manifest.Files)
            {
                if (file.Source is FileSource.FileReference fileReference)
                {
                    if (fileArtifactsByHash.ContainsKey(fileReference.ArtifactHash))
                    {
                        fileArtifactReferenceCounts.TryGetValue(fileReference.ArtifactHash, out int count);
                        fileArtifactReferenceCounts[fileReference.ArtifactHash] = count + 1;

                        // "One file artifact may be shared by several files with the same bytes" implies
                        // every file sharing it must actually agree on those bytes (size + fileHash).
                        if (fileArtifactContentByHash.TryGetValue(fileReference.ArtifactHash, out (long Size, string FileHash) firstContent))
                        {
                            if (firstContent.Size != file.Size || firstContent.FileHash != file.FileHash)
                            {
                                errors.Add(new GamePatchKitError(
                                    Stage,
                                    ManifestErrorCodes.InconsistentFileArtifactContent,
                                    $"File artifact hash '{fileReference.ArtifactHash}' is referenced with differing size/fileHash across files.",
                                    packageId: manifest.PackageId,
                                    relativePath: file.Path));
                            }
                        }
                        else
                        {
                            fileArtifactContentByHash[fileReference.ArtifactHash] = (file.Size, file.FileHash);
                        }
                    }
                    else
                    {
                        errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownFileArtifactReference, $"File references unknown file artifact hash '{fileReference.ArtifactHash}'.", packageId: manifest.PackageId, relativePath: file.Path));
                    }

                    continue;
                }

                var bundleEntryReference = (FileSource.BundleEntryReference)file.Source;
                ManifestArtifact.BundleArtifact? referencedBundle = FindBundleByHash(bundleArtifactsByGroupAndHash, bundleEntryReference.ArtifactHash);

                if (referencedBundle == null)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownBundleArtifactReference, $"File references unknown bundle artifact hash '{bundleEntryReference.ArtifactHash}'.", packageId: manifest.PackageId, relativePath: file.Path));
                    continue;
                }

                if (referencedBundle.Group != file.Group)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.BundleEntryGroupMismatch, $"File group '{file.Group}' does not match referenced bundle's group '{referencedBundle.Group}'.", packageId: manifest.PackageId, relativePath: file.Path, group: file.Group));
                }

                bool entryExists = false;
                foreach (BundleEntry entry in referencedBundle.Entries)
                {
                    if (entry.Path == bundleEntryReference.EntryPath)
                    {
                        entryExists = true;
                        break;
                    }
                }

                if (!entryExists)
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnknownBundleEntryReference, $"Bundle has no entry at path '{bundleEntryReference.EntryPath}'.", packageId: manifest.PackageId, relativePath: file.Path));
                    continue;
                }

                // Bundle entries are stored at the file's own full relative path, not a bundle-local alias.
                if (file.Path != bundleEntryReference.EntryPath)
                {
                    errors.Add(new GamePatchKitError(
                        Stage,
                        ManifestErrorCodes.BundleEntryPathMismatch,
                        $"File path '{file.Path}' does not match its bundle entry path '{bundleEntryReference.EntryPath}'.",
                        packageId: manifest.PackageId,
                        relativePath: file.Path));
                }

                var entryKey = (referencedBundle.Group, referencedBundle.ArtifactHash, bundleEntryReference.EntryPath);
                bundleEntryReferenceCounts.TryGetValue(entryKey, out int entryCount);
                bundleEntryReferenceCounts[entryKey] = entryCount + 1;
            }

            foreach (string hash in fileArtifactsByHash.Keys)
            {
                if (!fileArtifactReferenceCounts.ContainsKey(hash))
                {
                    errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnreferencedFileArtifact, $"File artifact hash '{hash}' is never referenced by files[].", packageId: manifest.PackageId));
                }
            }

            foreach (ManifestArtifact.BundleArtifact bundle in bundleArtifactsByGroupAndHash.Values)
            {
                foreach (BundleEntry entry in bundle.Entries)
                {
                    var entryKey = (bundle.Group, bundle.ArtifactHash, entry.Path);
                    bundleEntryReferenceCounts.TryGetValue(entryKey, out int count);

                    if (count == 0)
                    {
                        errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.UnreferencedBundleEntry, $"Bundle entry '{entry.Path}' is never referenced by files[].", packageId: manifest.PackageId, group: bundle.Group));
                    }
                    else if (count > 1)
                    {
                        errors.Add(new GamePatchKitError(Stage, ManifestErrorCodes.DuplicateBundleEntryReference, $"Bundle entry '{entry.Path}' is referenced by {count} files instead of exactly one.", packageId: manifest.PackageId, group: bundle.Group));
                    }
                }
            }
        }

        private static ManifestArtifact.BundleArtifact? FindBundleByHash(
            Dictionary<(string Group, string Hash), ManifestArtifact.BundleArtifact> bundleArtifactsByGroupAndHash,
            string artifactHash)
        {
            foreach (KeyValuePair<(string Group, string Hash), ManifestArtifact.BundleArtifact> pair in bundleArtifactsByGroupAndHash)
            {
                if (pair.Key.Hash == artifactHash)
                {
                    return pair.Value;
                }
            }

            return null;
        }
    }
}
