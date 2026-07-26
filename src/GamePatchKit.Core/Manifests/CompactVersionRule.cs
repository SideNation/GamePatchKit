using System;

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
        public static CompactDecision Resolve(ReleaseManifest source, ReleaseManifest candidate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
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
