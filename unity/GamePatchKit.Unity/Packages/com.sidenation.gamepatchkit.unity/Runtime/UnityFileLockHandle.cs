using System;
using System.IO;
using System.Threading.Tasks;

namespace GamePatchKit.Unity
{
    internal sealed class UnityFileLockHandle : IAsyncDisposable
    {
        private readonly FileStream _stream;

        public UnityFileLockHandle(FileStream stream)
        {
            _stream = stream;
        }

        public ValueTask DisposeAsync()
        {
            return _stream.DisposeAsync();
        }
    }
}

