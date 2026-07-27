namespace GamePatchKit.Conformance;

public class TestInMemoryRuntimeStorage
{
    [Fact]
    public async Task AcquirePackageWriterLockAsync_DisposingTheHandleTwice_ReleasesTheSemaphoreOnlyOnce()
    {
        var storage = new InMemoryRuntimeStorage();
        IAsyncDisposable first = await storage.AcquirePackageWriterLockAsync("pkg", CancellationToken.None);
        await first.DisposeAsync();
        await first.DisposeAsync();

        IAsyncDisposable second = await storage.AcquirePackageWriterLockAsync("pkg", CancellationToken.None);
        Task<IAsyncDisposable> thirdAttempt = storage.AcquirePackageWriterLockAsync("pkg", CancellationToken.None);
        await Task.Delay(50);
        Assert.False(
            thirdAttempt.IsCompleted,
            "a double-dispose leaked an extra permit, letting a third acquirer in while the second still holds the lock");

        await second.DisposeAsync();
        IAsyncDisposable third = await thirdAttempt;
        await third.DisposeAsync();
    }
}
