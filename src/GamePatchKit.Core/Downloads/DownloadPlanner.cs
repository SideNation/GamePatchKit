using System;
using System.Collections.Generic;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Downloads
{
    // Turns a target manifest plus verified local state into the work needed for one activation batch.
    //
    // The group set is always explicit: a first install or a global release update passes
    // RequiredGroupNames, and an optional-group request passes just the groups asked for. Nothing outside the
    // selected groups is ever planned, which is what keeps an optional-group install from dragging in the rest
    // of the release.
    //
    // The target manifest must already have passed ReleaseManifest.TryParse and ManifestValidator; planning
    // assumes its references resolve.
    public static class DownloadPlanner
    {
        public static IReadOnlyList<string> RequiredGroupNames(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var names = new List<string>();

            foreach (ManifestGroupEntry group in manifest.Groups)
            {
                if (group.Required)
                {
                    names.Add(group.Name);
                }
            }

            return names;
        }

        public static DownloadPlan Plan(
            ReleaseManifest target,
            IEnumerable<string> targetGroups,
            IEnumerable<LocalFileState> installedFiles,
            IEnumerable<CachedArtifactObject> cachedObjects)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (targetGroups == null)
            {
                throw new ArgumentNullException(nameof(targetGroups));
            }

            if (installedFiles == null)
            {
                throw new ArgumentNullException(nameof(installedFiles));
            }

            if (cachedObjects == null)
            {
                throw new ArgumentNullException(nameof(cachedObjects));
            }

            HashSet<string> selectedGroups = SelectGroups(target, targetGroups);

            // Last entry wins for a repeated path in either map: one path is one stored object, so a caller
            // reporting it twice has one file, not two.
            var installedHashByPath = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (LocalFileState state in installedFiles)
            {
                installedHashByPath[state.Path] = state.FileHash;
            }

            var cachedHashByPath = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (CachedArtifactObject cached in cachedObjects)
            {
                cachedHashByPath[cached.Path] = cached.ObjectHash;
            }

            IndexArtifacts(
                target,
                out Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
                out Dictionary<string, ManifestArtifact.BundleArtifact> bundlesByHash);

            var missingFiles = new List<ManifestFileEntry>();
            var neededArtifacts = new HashSet<ManifestArtifact>();

            foreach (ManifestFileEntry file in target.Files)
            {
                if (!selectedGroups.Contains(file.Group))
                {
                    continue;
                }

                // Path plus verified fileHash is the whole test: the same bytes at the same path need no
                // download even when the release that produced them stored them somewhere else entirely.
                if (installedHashByPath.TryGetValue(file.Path, out string? localFileHash) && localFileHash == file.FileHash)
                {
                    continue;
                }

                missingFiles.Add(file);
                neededArtifacts.Add(ResolveArtifact(target, file, fileArtifactsByHash, bundlesByHash));
            }

            var plannedArtifacts = new List<PlannedArtifact>();

            // Walk the manifest rather than the needed set: canonical artifact order carries into the plan, and
            // an artifact several missing files share is emitted once.
            foreach (ManifestArtifact artifact in target.Artifacts)
            {
                if (!neededArtifacts.Contains(artifact))
                {
                    continue;
                }

                var objectsToDownload = new List<ArtifactPayloadObject>();

                foreach (ArtifactPayloadObject payload in artifact.GetPayloadObjects())
                {
                    // Both the location and the bytes have to match. A part path carries its parent's
                    // artifactHash rather than its own digest, so an object cached at the right path can still
                    // be the wrong bytes from a differently-split generation of the same payload.
                    if (!cachedHashByPath.TryGetValue(payload.Path, out string? cachedHash) || cachedHash != payload.ObjectHash)
                    {
                        objectsToDownload.Add(payload);
                    }
                }

                plannedArtifacts.Add(new PlannedArtifact(artifact, objectsToDownload, IsMultipart(artifact)));
            }

            return new DownloadPlan(plannedArtifacts, missingFiles);
        }

        private static HashSet<string> SelectGroups(ReleaseManifest target, IEnumerable<string> targetGroups)
        {
            var declaredGroups = new HashSet<string>(StringComparer.Ordinal);

            foreach (ManifestGroupEntry group in target.Groups)
            {
                declaredGroups.Add(group.Name);
            }

            var selectedGroups = new HashSet<string>(StringComparer.Ordinal);

            foreach (string name in targetGroups)
            {
                if (!declaredGroups.Contains(name))
                {
                    throw new ArgumentException($"Target group '{name}' is not declared by the target manifest.", nameof(targetGroups));
                }

                selectedGroups.Add(name);
            }

            return selectedGroups;
        }

        private static void IndexArtifacts(
            ReleaseManifest target,
            out Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
            out Dictionary<string, ManifestArtifact.BundleArtifact> bundlesByHash)
        {
            fileArtifactsByHash = new Dictionary<string, ManifestArtifact.FileArtifact>(StringComparer.Ordinal);
            bundlesByHash = new Dictionary<string, ManifestArtifact.BundleArtifact>(StringComparer.Ordinal);

            foreach (ManifestArtifact artifact in target.Artifacts)
            {
                if (artifact is ManifestArtifact.FileArtifact fileArtifact)
                {
                    fileArtifactsByHash.TryAdd(fileArtifact.PrimaryArtifactHash, fileArtifact);
                }
                else if (artifact is ManifestArtifact.BundleArtifact bundle)
                {
                    // files[].source.bundleEntry identifies a bundle by hash alone, so this mirrors
                    // ManifestValidator's hash-keyed resolution; a validated manifest cannot have two
                    // referenced bundles sharing a hash.
                    bundlesByHash.TryAdd(bundle.ArtifactHash, bundle);
                }
            }
        }

        private static ManifestArtifact ResolveArtifact(
            ReleaseManifest target,
            ManifestFileEntry file,
            Dictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
            Dictionary<string, ManifestArtifact.BundleArtifact> bundlesByHash)
        {
            if (file.Source is FileSource.FileReference fileReference)
            {
                if (fileArtifactsByHash.TryGetValue(fileReference.ArtifactHash, out ManifestArtifact.FileArtifact? fileArtifact))
                {
                    return fileArtifact;
                }

                throw new ArgumentException(
                    $"File '{file.Path}' references file artifact '{fileReference.ArtifactHash}', which the manifest does not declare; validate the manifest before planning.",
                    nameof(target));
            }

            var bundleReference = (FileSource.BundleEntryReference)file.Source;

            if (bundlesByHash.TryGetValue(bundleReference.ArtifactHash, out ManifestArtifact.BundleArtifact? bundle))
            {
                return bundle;
            }

            throw new ArgumentException(
                $"File '{file.Path}' references bundle artifact '{bundleReference.ArtifactHash}', which the manifest does not declare; validate the manifest before planning.",
                nameof(target));
        }

        private static bool IsMultipart(ManifestArtifact artifact)
        {
            return artifact is ManifestArtifact.FileArtifact fileArtifact && fileArtifact.Payload is FilePayload.Parts;
        }
    }
}
