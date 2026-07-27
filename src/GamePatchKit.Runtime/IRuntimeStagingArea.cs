using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Runtime
{
    public interface IRuntimeStagingArea : IAsyncDisposable
    {
        Task<Stream> CreateFileAsync(
            string relativePath,
            CancellationToken cancellationToken);

        Task<string> PromoteAsync(CancellationToken cancellationToken);
    }
}
