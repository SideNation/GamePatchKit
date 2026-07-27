using System.Text;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet.Tests;

// A cache writer's Content must stay invisible to readers until CommitAsync renames it into place, and commit
// has to be able to replace whatever - including a corrupt object - already sits at that path.
public class TestFileSystemRuntimeStorageCache
{
    private const string PackageId = "game-data";
    private const string RelativePath = "game-data/artifacts/files/aa11/content";

    [Fact]
    public async Task OpenCachedArtifactAsync_BeforeAnyWrite_ReturnsNull()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        Stream? stream = await storage.OpenCachedArtifactAsync(PackageId, RelativePath, CancellationToken.None);

        Assert.Null(stream);
    }

    [Fact]
    public async Task CreateCacheWriterAsync_BeforeCommit_IsNotVisibleToReaders()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        byte[] bytes = Encoding.UTF8.GetBytes("payload");

        await using (IRuntimeCacheWriter writer = await storage.CreateCacheWriterAsync(
            PackageId, RelativePath, CancellationToken.None))
        {
            await writer.Content.WriteAsync(bytes, CancellationToken.None);
            await writer.Content.FlushAsync(CancellationToken.None);

            Stream? duringWrite = await storage.OpenCachedArtifactAsync(PackageId, RelativePath, CancellationToken.None);
            Assert.Null(duringWrite);
        }

        // Disposed without ever calling CommitAsync: still nothing there, and no leaked temporary file.
        Assert.Null(await storage.OpenCachedArtifactAsync(PackageId, RelativePath, CancellationToken.None));
        string cacheDirectory = Path.Combine(root.Path, "packages", PackageId, "cache", "game-data", "artifacts", "files", "aa11");
        Assert.False(Directory.Exists(cacheDirectory) && Directory.EnumerateFileSystemEntries(cacheDirectory).Any());
    }

    [Fact]
    public async Task CreateCacheWriterAsync_CommitAsync_MakesBytesReadable()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        byte[] bytes = Encoding.UTF8.GetBytes("payload");

        await using (IRuntimeCacheWriter writer = await storage.CreateCacheWriterAsync(
            PackageId, RelativePath, CancellationToken.None))
        {
            await writer.Content.WriteAsync(bytes, CancellationToken.None);
            await writer.CommitAsync(CancellationToken.None);
        }

        await using Stream? committed = await storage.OpenCachedArtifactAsync(PackageId, RelativePath, CancellationToken.None);
        Assert.NotNull(committed);
        using var reader = new MemoryStream();
        await committed!.CopyToAsync(reader);
        Assert.Equal(bytes, reader.ToArray());
    }

    [Fact]
    public async Task CreateCacheWriterAsync_CommitAsync_RepairsACorruptExistingObject()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        await using (IRuntimeCacheWriter first = await storage.CreateCacheWriterAsync(
            PackageId, RelativePath, CancellationToken.None))
        {
            await first.Content.WriteAsync(Encoding.UTF8.GetBytes("corrupt"), CancellationToken.None);
            await first.CommitAsync(CancellationToken.None);
        }

        byte[] good = Encoding.UTF8.GetBytes("good-payload");
        await using (IRuntimeCacheWriter second = await storage.CreateCacheWriterAsync(
            PackageId, RelativePath, CancellationToken.None))
        {
            await second.Content.WriteAsync(good, CancellationToken.None);
            await second.CommitAsync(CancellationToken.None);
        }

        await using Stream? repaired = await storage.OpenCachedArtifactAsync(PackageId, RelativePath, CancellationToken.None);
        using var reader = new MemoryStream();
        await repaired!.CopyToAsync(reader);
        Assert.Equal(good, reader.ToArray());
    }

    [Fact]
    public async Task OpenCachedArtifactAsync_RelativePathThatEscapesTheCacheRoot_IsRejected()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        await Assert.ThrowsAsync<IOException>(
            () => storage.OpenCachedArtifactAsync(PackageId, "../../outside", CancellationToken.None));
    }
}
