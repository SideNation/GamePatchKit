using System;

namespace GamePatchKit.Core.Downloads
{
    // One object already in the content-addressed cache: where it sits and the digest its bytes were verified
    // against when it was stored.
    //
    // The hash is what makes a cache entry usable evidence. Published storage is immutable, but a client's
    // cache is local state that can be stale or damaged, and a part's path does not carry its own digest
    // (ContentAddressedPath.FilePartPath addresses it by its parent artifactHash and index) - so a path on its
    // own cannot say which bytes are actually sitting there. Planning against the recorded hash means a bad
    // entry is simply re-fetched, instead of being skipped into a plan whose parts can never join.
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
