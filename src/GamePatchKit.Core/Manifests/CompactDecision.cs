using System;

namespace GamePatchKit.Core.Manifests
{
    // Outcome of applying the compactVersion rule to a compact candidate.
    //
    // Changed == false means the candidate canonicalizes to the source release's exact bytes, so Result
    // repeats the source's own dataVersion, compactVersion and manifestHash and the caller must publish
    // nothing - no new artifacts, no new manifest.
    public sealed class CompactDecision
    {
        public bool Changed { get; }

        public FinalizedManifest Result { get; }

        public CompactDecision(bool changed, FinalizedManifest result)
        {
            Changed = changed;
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }
    }
}
