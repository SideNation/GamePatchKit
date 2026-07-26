using System;

namespace GamePatchKit.Core.Downloads
{
    // One installed file the caller has already verified: its normalized relative path and the fileHash its
    // bytes actually produced. This pair is the whole local input to planning - nothing about which artifact
    // or bundle the file originally came from is considered - which is what lets a compacted release reuse an
    // installation whose artifacts have all moved.
    public sealed class LocalFileState
    {
        public string Path { get; }

        public string FileHash { get; }

        public LocalFileState(string path, string fileHash)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            FileHash = fileHash ?? throw new ArgumentNullException(nameof(fileHash));
        }
    }
}
