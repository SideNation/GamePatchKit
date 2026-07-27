using System.Text;

namespace GamePatchKit.DotNet.Tests;

// PackageState replacement must never be observable as a partial write, and the writer lock has to be a real
// cross-instance exclusion rather than a check on whether a lock file happens to exist.
public class TestFileSystemRuntimeStorageState
{
    [Fact]
    public async Task ReadPackageStateAsync_BeforeAnyReplace_ReturnsNull()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        byte[]? bytes = await storage.ReadPackageStateAsync("game-data", CancellationToken.None);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task ReplacePackageStateAsync_ThenRead_RoundTripsExactBytes()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        byte[] first = Encoding.UTF8.GetBytes("{\"stateRevision\":1}");
        byte[] second = Encoding.UTF8.GetBytes("{\"stateRevision\":2}");

        await storage.ReplacePackageStateAsync("game-data", first, CancellationToken.None);
        Assert.Equal(first, await storage.ReadPackageStateAsync("game-data", CancellationToken.None));

        // A second replace must overwrite, not append or merge - state is a single current snapshot.
        await storage.ReplacePackageStateAsync("game-data", second, CancellationToken.None);
        Assert.Equal(second, await storage.ReadPackageStateAsync("game-data", CancellationToken.None));
    }

    [Fact]
    public async Task ReplacePackageStateAsync_InterruptedBeforeRename_LeavesPreviousStateIntact()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        byte[] committed = Encoding.UTF8.GetBytes("{\"stateRevision\":1}");
        await storage.ReplacePackageStateAsync("game-data", committed, CancellationToken.None);

        // An already-cancelled token fails the write before the temporary file is ever renamed into place -
        // the closest a unit test can get to a mid-write crash without actually killing the process.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        byte[] attempted = Encoding.UTF8.GetBytes("{\"stateRevision\":2}");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.ReplacePackageStateAsync("game-data", attempted, cancelled.Token));

        Assert.Equal(committed, await storage.ReadPackageStateAsync("game-data", CancellationToken.None));

        string stateDirectory = Path.Combine(root.Path, "packages", "game-data", "state");
        string[] leftoverTemporaryFiles = Directory
            .GetFiles(stateDirectory)
            .Where(path => Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(leftoverTemporaryFiles);
    }

    [Fact]
    public async Task AcquirePackageWriterLockAsync_SerializesTwoInstancesOnTheSamePackage()
    {
        using var root = new TempRuntimeRoot();
        var first = new FileSystemRuntimeStorage(root.Path);
        var second = new FileSystemRuntimeStorage(root.Path);

        IAsyncDisposable held = await first.AcquirePackageWriterLockAsync("game-data", CancellationToken.None);
        Task<IAsyncDisposable> waiting = second.AcquirePackageWriterLockAsync("game-data", CancellationToken.None);

        // Existence-of-a-lock-file would let this second attempt through immediately; an exclusive OS handle
        // does not.
        await Task.Delay(200);
        Assert.False(waiting.IsCompleted);

        await held.DisposeAsync();
        IAsyncDisposable acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        await acquired.DisposeAsync();
    }

    [Fact]
    public async Task AcquirePackageWriterLockAsync_TimesOutInsteadOfHangingForeverWithoutACancellationToken()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path, TimeSpan.FromMilliseconds(200));
        IAsyncDisposable held = await storage.AcquirePackageWriterLockAsync("game-data", CancellationToken.None);

        try
        {
            // No cancellation token at all - an IOException here is not necessarily contention (it could be a
            // full disk or a permissions problem), so only the internal timeout can end this without one.
            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => storage.AcquirePackageWriterLockAsync("game-data", CancellationToken.None));
            Assert.NotNull(exception.InnerException);
        }
        finally
        {
            await held.DisposeAsync();
        }
    }
}
