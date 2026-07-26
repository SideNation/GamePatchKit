using System;

namespace GamePatchKit.Core.Manifests
{
    // One physically stored object of an artifact: a whole single-file or bundle payload, or one part of a
    // multipart file artifact. ObjectHash is the digest of that object's own bytes - artifactHash for whole
    // payloads, partHash for parts - which is what a transfer is verified against before the parts are joined.
    //
    // Path is the object's permanent storage key, and it is content-addressed only for whole payloads: a
    // part's path is built from its parent artifactHash and index. Two generations of one payload split at
    // different boundaries would therefore claim the same part paths for different bytes, which immutable
    // storage cannot hold - ReleaseStorageCompatibility rejects that rather than trying to represent it.
    public sealed class ArtifactPayloadObject
    {
        public string Path { get; }

        public long Size { get; }

        public string ObjectHash { get; }

        public ArtifactPayloadObject(string path, long size, string objectHash)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Size = size;
            ObjectHash = objectHash ?? throw new ArgumentNullException(nameof(objectHash));
        }
    }
}
