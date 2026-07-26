using GamePatchKit.Core.Errors;

namespace GamePatchKit.Packager;

// Reads a whole small document into memory, refusing to allocate for one that cannot be that document.
//
// Manifests and signatures are read before anything is known about them - the hash that would reject a wrong
// one is taken over the very bytes being read. File.ReadAllBytesAsync allocates the file's whole length
// first, so a damaged publish tree with a multi-gigabyte blob at a manifest path can take out the process
// before the cheapest check runs. Length is the one thing that can be judged up front, and it is judged
// before the read rather than after.
internal static class BoundedFile
{
    private const int BufferSize = 64 * 1024;

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        long maximumBytes,
        string stage,
        string errorCode,
        string message,
        string? packageId,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Checked twice on purpose. The reported length rejects an oversized regular file without reading a
        // byte; the loop below still counts, because a FIFO or character device reports no useful length and
        // would otherwise stream without end.
        if (source.CanSeek && source.Length > maximumBytes)
        {
            throw Failure(stage, errorCode, message, packageId);
        }

        using var destination = new MemoryStream();
        var buffer = new byte[BufferSize];
        int read;

        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (destination.Length + read > maximumBytes)
            {
                throw Failure(stage, errorCode, message, packageId);
            }

            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    private static PackageException Failure(string stage, string errorCode, string message, string? packageId)
    {
        return new PackageException(new GamePatchKitError(stage, errorCode, message, packageId));
    }
}
