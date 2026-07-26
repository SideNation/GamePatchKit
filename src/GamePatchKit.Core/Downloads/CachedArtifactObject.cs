using System;

namespace GamePatchKit.Core.Downloads
{
    // One object already in the content-addressed cache: where it sits and the digest its bytes were verified
    // against when it was stored.
    //
    // The hash is not redundant with the path. A part's path is built from its parent artifactHash and index
    // (ContentAddressedPath.FilePartPath), not from its own bytes, so re-splitting one payload under a
    // different maxArtifactBytes produces the same part paths holding different bytes. Matching on path alone
    // would treat a stale part as present and plan a download that can never join to the right payload.
    public sealed class CachedArtifactObject
    {
        public string Path { get; }

        public string ObjectHash { get; }

        public CachedArtifactObject(string path, string objectHash)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            ObjectHash = objectHash ?? throw new ArgumentNullException(nameof(objectHash));
        }
    }
}
