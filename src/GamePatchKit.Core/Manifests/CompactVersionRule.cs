using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;

namespace GamePatchKit.Core.Manifests
{
    // compactVersion is the physical packaging generation, not a manifest identifier: the first package uses
    // Initial, an incremental package reuses the previous manifest's value unchanged
    // (ReleaseIdentity.Finalize(draft, previous.CompactVersion)), and only a compact that actually changes the
    // canonical bytes advances it.
    public static class CompactVersionRule
    {
        public const long Initial = 0L;

        // Decides whether a compact candidate is a real new generation or a no-op.
        //
        // The candidate is canonicalized under the source's own compactVersion first; if those bytes equal the
        // source manifest's, nothing about the physical layout moved and the source's three identity values are
        // reused. Otherwise the layout changed, so compactVersion advances by one while dataVersion - which
        // excludes physical layout - stays put.
        //
        // Both manifests must already have passed ReleaseManifest.TryParse and ManifestValidator: the source's
        // canonical bytes are recomputed from its model, which is only equal to the published bytes for a valid
        // manifest.
        //
        // retainedObjects is everything the package still stores from releases other than the source - older
        // releases kept for rollback. The source's own objects are always included, so an empty sequence means
        // "the source is the only release still in storage"; passing one when older releases exist lets a
        // candidate overwrite their bytes. There is no overload without it: a compact publishes into shared
        // storage, and what else lives there is not something this rule can infer.
        public static CompactDecision Resolve(ReleaseManifest source, ReleaseManifest candidate, IEnumerable<ArtifactPayloadObject> retainedObjects)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            if (retainedObjects == null)
            {
                throw new ArgumentNullException(nameof(retainedObjects));
            }

            FinalizedManifest probe = ReleaseIdentity.Finalize(candidate, source.CompactVersion);

            // Compact repackages one release; a candidate with a different logical state is a package operation
            // instead, and silently accepting it would publish new data under a compact's version rules.
            if (probe.DataVersion != source.DataVersion)
            {
                throw new ArgumentException(
                    $"Compact candidate has dataVersion '{probe.DataVersion}' but the source release is '{source.DataVersion}'; compact must preserve the logical state.",
                    nameof(candidate));
            }

            // A compact publishes alongside everything already stored rather than replacing any of it, so the
            // candidate must not claim a path that any retained release - not just the source - already filled
            // with different bytes.
            ValidationResult compatibility = ReleaseStorageCompatibility.Validate(
                source.EnumeratePayloadObjects().Concat(retainedObjects),
                candidate);

            if (!compatibility.IsValid)
            {
                throw new ArgumentException(
                    "Compact candidate cannot be published into the package's existing storage: " + string.Join("; ", compatibility.Errors),
                    nameof(candidate));
            }

            byte[] sourceBytes = ReleaseIdentity.ComputeCanonicalBytes(source);

            if (BytesEqual(probe.GetCanonicalBytes(), sourceBytes))
            {
                return new CompactDecision(false, probe);
            }

            return new CompactDecision(true, ReleaseIdentity.Finalize(candidate, source.CompactVersion + 1));
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            return ((ReadOnlySpan<byte>)left).SequenceEqual(right);
        }
    }
}
