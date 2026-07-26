using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;

namespace GamePatchKit.Core.Manifests
{
    // Core's identity API: the two digests a release is known by.
    //
    // dataVersion hashes only ManifestIdentity's projection (packageId, group consumption policy, and each
    // file's path/group/size/fileHash), so re-packaging the same data with different artifacts, bundle
    // boundaries or compression leaves it untouched. manifestHash hashes the whole canonical manifest with no
    // manifestHash field of its own, so any physical or schema change moves it. Both are taken over
    // CanonicalJsonWriter output, which is what makes them reproducible across implementations.
    public static class ReleaseIdentity
    {
        private const string Stage = "release-manifest";

        public static byte[] ComputeIdentityBytes(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return CanonicalJsonWriter.Write(ManifestIdentity.FromManifest(manifest).ToCanonicalValue());
        }

        // Independent of the manifest's own recorded dataVersion, so a draft carrying a placeholder value
        // computes the same result as the finished manifest.
        public static string ComputeDataVersion(ReleaseManifest manifest)
        {
            return DataVersionFormat.Prefix + Sha256Hash.ComputeHex(ComputeIdentityBytes(manifest));
        }

        public static byte[] ComputeCanonicalBytes(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return CanonicalJsonWriter.Write(manifest.ToJson());
        }

        public static string ComputeManifestHash(byte[] canonicalManifestBytes)
        {
            return Sha256Hash.ComputeHex(canonicalManifestBytes);
        }

        // Settles a draft's identity: dataVersion from the draft's logical state, compactVersion from the
        // caller's packaging rule (CompactVersionRule), manifestHash from the resulting canonical bytes.
        public static FinalizedManifest Finalize(ReleaseManifest draft, long compactVersion)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            if (compactVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(compactVersion), "compactVersion must not be negative.");
            }

            // ReleaseManifest stores the collections it is handed without copying, so finalizing straight off
            // the draft would leave the caller's own lists aliased by the result: editing one afterwards would
            // change finalized.Manifest while its canonical bytes and manifestHash - the things that get
            // published and signed - stayed behind. Both identity values are computed from these snapshots, so
            // the model and the bytes describe the same release for good.
            var groups = new List<ManifestGroupEntry>(draft.Groups).AsReadOnly();
            var artifacts = new List<ManifestArtifact>(draft.Artifacts).AsReadOnly();
            var files = new List<ManifestFileEntry>(draft.Files).AsReadOnly();

            var snapshot = new ReleaseManifest(draft.SchemaVersion, draft.PackageId, draft.DataVersion, compactVersion, groups, artifacts, files);

            var manifest = new ReleaseManifest(
                snapshot.SchemaVersion,
                snapshot.PackageId,
                ComputeDataVersion(snapshot),
                compactVersion,
                groups,
                artifacts,
                files);

            byte[] canonicalBytes = ComputeCanonicalBytes(manifest);

            return new FinalizedManifest(manifest, canonicalBytes, ComputeManifestHash(canonicalBytes));
        }

        // Checks received manifest bytes against a target reference's manifestHash. Takes bytes rather than a
        // parsed manifest on purpose: the hash is a statement about the exact bytes that were transferred, and
        // re-serializing a parsed model would verify Core's own writer instead of the payload.
        public static ValidationResult VerifyManifestHash(byte[] manifestBytes, string expectedManifestHash)
        {
            if (manifestBytes == null)
            {
                throw new ArgumentNullException(nameof(manifestBytes));
            }

            if (expectedManifestHash == null)
            {
                throw new ArgumentNullException(nameof(expectedManifestHash));
            }

            if (!Hex64.IsValid(expectedManifestHash))
            {
                return Failure(ManifestErrorCodes.InvalidManifestHashFormat, "Expected manifestHash must be lowercase hex64.");
            }

            string actualManifestHash = ComputeManifestHash(manifestBytes);

            if (actualManifestHash != expectedManifestHash)
            {
                return Failure(
                    ManifestErrorCodes.ManifestHashMismatch,
                    $"Manifest bytes hash to '{actualManifestHash}', expected '{expectedManifestHash}'.");
            }

            return ValidationResult.Success();
        }

        private static ValidationResult Failure(string code, string message)
        {
            return ValidationResult.Failure(new List<GamePatchKitError> { new GamePatchKitError(Stage, code, message) });
        }
    }
}
