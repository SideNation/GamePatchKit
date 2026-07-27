using System;
using System.IO;
using System.Threading.Tasks;

namespace GamePatchKit.DotNet;

// Holding this open FileStream is the lock: FileShare.None means no other handle (this or another process)
// can open the same path until it is disposed, which is what makes this an exclusive OS handle rather than a
// check on whether a lock file happens to exist.
internal sealed class FileLockHandle : IAsyncDisposable
{
    private readonly FileStream _stream;

    public FileLockHandle(FileStream stream)
    {
        _stream = stream;
    }

    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}
