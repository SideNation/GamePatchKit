using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Runtime
{
    public interface IRuntimeCacheWriter : IAsyncDisposable
    {
        Stream Content { get; }

        Task CommitAsync(CancellationToken cancellationToken);
    }
}
