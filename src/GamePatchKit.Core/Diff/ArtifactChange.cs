using System;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Diff
{
    // One physical difference between two releases: a stored object present in only one of them. Removed
    // objects are reported for reporting and planning only - a past release's bytes stay in shared storage and
    // are never deleted by a diff.
    public sealed class ArtifactChange
    {
        public ArtifactChangeKind Kind { get; }

        public ArtifactPayloadObject Payload { get; }

        public ArtifactChange(ArtifactChangeKind kind, ArtifactPayloadObject payload)
        {
            Kind = kind;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }
}
