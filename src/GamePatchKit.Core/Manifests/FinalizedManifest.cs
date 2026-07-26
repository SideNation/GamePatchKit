using System;

namespace GamePatchKit.Core.Manifests
{
    // A manifest whose identity fields are settled: dataVersion computed from its own logical state,
    // compactVersion supplied by the caller's packaging rule, and manifestHash taken over the canonical bytes
    // carried here. Those exact bytes - not a later re-serialization - are what gets published, hashed and
    // signed, so they travel with the hash rather than being recomputed by every consumer.
    public sealed class FinalizedManifest
    {
        private readonly byte[] _canonicalBytes;

        public ReleaseManifest Manifest { get; }

        public string ManifestHash { get; }

        public string DataVersion => Manifest.DataVersion;

        public long CompactVersion => Manifest.CompactVersion;

        public FinalizedManifest(ReleaseManifest manifest, byte[] canonicalBytes, string manifestHash)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _canonicalBytes = canonicalBytes ?? throw new ArgumentNullException(nameof(canonicalBytes));
            ManifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
        }

        // Copies on the way out: ManifestHash is only true of these bytes, so a caller must not be able to
        // edit the array this instance keeps.
        public byte[] GetCanonicalBytes()
        {
            return (byte[])_canonicalBytes.Clone();
        }
    }
}
