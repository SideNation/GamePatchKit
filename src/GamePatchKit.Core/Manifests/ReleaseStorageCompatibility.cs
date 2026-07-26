using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;

namespace GamePatchKit.Core.Manifests
{
    // Checks that a release can be published into a package's immutable content-addressed storage.
    //
    // A stored object's path is its permanent key: published bytes are never rewritten (the PRD forbids
    // re-uploading to an existing path and treats differing bytes at a hash path as an error), so anything
    // that names an occupied path has to mean the bytes already there. Whole payloads get this for free
    // because their path carries their own digest. A part does not: its path is <artifactHash>/part-#####,
    // addressed by its parent. Re-splitting one payload under a different maxArtifactBytes keeps the
    // artifactHash and the part indexes, so the new generation claims the old one's paths with different
    // bytes - publishing it would either fail or overwrite bytes an older release still needs for rollback
    // and for readers already on it.
    //
    // Packaging is supposed to make this unreachable by reusing an existing artifactHash's payload as it
    // stands. This is what catches a manifest that did not.
    public static class ReleaseStorageCompatibility
    {
        private const string Stage = "release-manifest";

        // The real question a publisher has to answer, because storage keeps every retained release, not just
        // the newest one. Checking only the previous release is not enough: a package that stored payload H as
        // 20/10 parts, then stopped referencing those paths, then re-split H as 16/14 has a candidate that
        // shares no path with its immediate predecessor and still overwrites the first release's parts.
        public static ValidationResult Validate(IEnumerable<ArtifactPayloadObject> retainedObjects, ReleaseManifest candidate)
        {
            if (retainedObjects == null)
            {
                throw new ArgumentNullException(nameof(retainedObjects));
            }

            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            var errors = new List<GamePatchKitError>();
            var objectHashByPath = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (ArtifactPayloadObject retained in retainedObjects)
            {
                if (!objectHashByPath.TryGetValue(retained.Path, out string? recordedObjectHash))
                {
                    objectHashByPath[retained.Path] = retained.ObjectHash;
                }
                else if (recordedObjectHash != retained.ObjectHash)
                {
                    // Storage already contradicts itself here, so the candidate is not the problem. Reported
                    // rather than resolved: keeping the first entry means a candidate matching the second is
                    // still compared against something instead of quietly passing.
                    errors.Add(new GamePatchKitError(
                        Stage,
                        ManifestErrorCodes.StoragePathConflict,
                        $"Retained storage already holds two different objects for path '{retained.Path}': '{recordedObjectHash}' and '{retained.ObjectHash}'.",
                        packageId: candidate.PackageId));
                }
            }

            foreach (ArtifactPayloadObject payload in candidate.EnumeratePayloadObjects())
            {
                if (objectHashByPath.TryGetValue(payload.Path, out string? retainedObjectHash) && retainedObjectHash != payload.ObjectHash)
                {
                    errors.Add(new GamePatchKitError(
                        Stage,
                        ManifestErrorCodes.StoragePathConflict,
                        $"Storage path '{payload.Path}' already holds '{retainedObjectHash}', but this release declares '{payload.ObjectHash}'; a published path's bytes are immutable.",
                        packageId: candidate.PackageId));
                }
            }

            return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
        }

        // Whether these two releases can coexist, and nothing more. Answers a diff's question - it says nothing
        // about the rest of the package's retained history, so it is not a publish check.
        public static ValidationResult Validate(ReleaseManifest left, ReleaseManifest right)
        {
            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            return Validate(left.EnumeratePayloadObjects(), right);
        }
    }
}
