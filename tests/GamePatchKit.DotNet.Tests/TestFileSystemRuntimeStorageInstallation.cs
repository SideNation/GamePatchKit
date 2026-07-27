using System.Text;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet.Tests;

// Staged files must stay invisible until PromoteAsync renames the whole group directory into installs/, an
// abandoned staging area must clean up after itself, and the opaque installationKey PromoteAsync returns has
// to be enough on its own - with no packageId parameter - to find that installation again.
public class TestFileSystemRuntimeStorageInstallation
{
    private const string PackageId = "game-data";

    [Fact]
    public async Task PromoteAsync_MakesFilesReadableThroughTheReturnedKey()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        byte[] core = Encoding.UTF8.GetBytes("core-bytes");
        byte[] maps = Encoding.UTF8.GetBytes("maps-bytes");

        string installationKey;
        await using (IRuntimeStagingArea staging = await storage.CreateStagingAreaAsync(PackageId, "core", CancellationToken.None))
        {
            await using (Stream file = await staging.CreateFileAsync("data/core.bin", CancellationToken.None))
            {
                await file.WriteAsync(core, CancellationToken.None);
            }

            await using (Stream file = await staging.CreateFileAsync("data/nested/maps.bin", CancellationToken.None))
            {
                await file.WriteAsync(maps, CancellationToken.None);
            }

            installationKey = await staging.PromoteAsync(CancellationToken.None);
        }

        Assert.True(await storage.InstallationExistsAsync(installationKey, CancellationToken.None));

        IReadOnlyList<string> paths = await storage.GetInstallationFilePathsAsync(installationKey, CancellationToken.None);
        Assert.Equal(
            new[] { "data/core.bin", "data/nested/maps.bin" },
            paths.OrderBy(path => path, StringComparer.Ordinal));

        await using Stream? coreFile = await storage.OpenInstallationFileAsync(installationKey, "data/core.bin", CancellationToken.None);
        using var coreReader = new MemoryStream();
        await coreFile!.CopyToAsync(coreReader);
        Assert.Equal(core, coreReader.ToArray());
    }

    [Fact]
    public async Task InstallationExistsAsync_ForAKeyThatWasNeverPromoted_ReturnsFalse()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        bool exists = await storage.InstallationExistsAsync($"{PackageId}/{Guid.NewGuid():N}", CancellationToken.None);

        Assert.False(exists);
    }

    // installationKey round-trips through PackageState.json, so a corrupted-but-schema-valid state file can
    // hand this adapter an installationKey it never produced. Each case exercises a different part of the
    // stricter validation: an extra '/' turning the local segment into a rooted path Path.Combine would
    // treat as absolute (escaping installs/ entirely on Unix), a local segment that is not a GUID, and a
    // package segment that is not valid kebab-case.
    [Theory]
    [InlineData("game-data//tmp")]
    [InlineData("game-data/not-a-guid")]
    [InlineData("Invalid_Pkg/00000000000000000000000000000000")]
    public async Task InstallationExistsAsync_MalformedInstallationKey_IsRejectedRatherThanResolvedOutsideTheRoot(
        string malformedKey)
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.InstallationExistsAsync(malformedKey, CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_WithoutPromoting_RemovesTheStagingDirectory()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);

        await using (IRuntimeStagingArea staging = await storage.CreateStagingAreaAsync(PackageId, "core", CancellationToken.None))
        {
            await using Stream file = await staging.CreateFileAsync("data/core.bin", CancellationToken.None);
            await file.WriteAsync(Encoding.UTF8.GetBytes("abandoned"), CancellationToken.None);
        }

        string stagingRoot = Path.Combine(root.Path, "packages", PackageId, "staging");
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }

    [Fact]
    public async Task CreateScratchStreamAsync_IsReadWriteSeekableAndDeletesOnDispose()
    {
        using var root = new TempRuntimeRoot();
        var storage = new FileSystemRuntimeStorage(root.Path);
        string stagingRoot = Path.Combine(root.Path, "packages", PackageId, "staging");
        byte[] bytes = Encoding.UTF8.GetBytes("scratch-bytes");

        await using (Stream scratch = await storage.CreateScratchStreamAsync(PackageId, CancellationToken.None))
        {
            Assert.True(scratch.CanRead);
            Assert.True(scratch.CanWrite);
            Assert.True(scratch.CanSeek);

            await scratch.WriteAsync(bytes, CancellationToken.None);
            scratch.Position = 0;
            var buffer = new byte[bytes.Length];
            int read = await scratch.ReadAsync(buffer, CancellationToken.None);

            Assert.Equal(bytes.Length, read);
            Assert.Equal(bytes, buffer);
            Assert.Single(Directory.EnumerateFileSystemEntries(stagingRoot));
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }
}
