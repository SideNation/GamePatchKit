using System;

namespace GamePatchKit.Core.Manifests
{
    // One physically stored object of an artifact: a whole single-file or bundle payload, or one part of a
    // multipart file artifact. ObjectHash is the digest of that object's own bytes - artifactHash for whole
    // payloads, partHash for parts - which is what a transfer is verified against before the parts are joined.
    //
    // Path is content-addressed only for whole payloads. A part's path is built from its parent artifactHash
    // and index, so two generations of one payload split at different boundaries occupy the same part paths
    // with different bytes: identify a stored object by Path and ObjectHash together, never by Path alone.
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
