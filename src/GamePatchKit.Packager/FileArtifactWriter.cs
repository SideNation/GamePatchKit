using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using Microsoft.Win32.SafeHandles;

namespace GamePatchKit.Packager;

internal sealed class FileArtifactWriteResult
{
    public ManifestArtifact.FileArtifact Artifact { get; }

    public bool WasCreated { get; }

    public long PayloadBytes { get; }

    public FileArtifactWriteResult(ManifestArtifact.FileArtifact artifact, bool wasCreated, long payloadBytes)
    {
        Artifact = artifact;
        WasCreated = wasCreated;
        PayloadBytes = payloadBytes;
    }
}

internal static class FileArtifactWriter
{
    private const string Stage = "file-artifact";
    private const int StreamBufferSize = 64 * 1024;

    public static async Task<FileArtifactWriteResult> WriteAsync(
        SourceFileSnapshot file,
        string packageId,
        CompressionKind compression,
        long maxArtifactBytes,
        string outputRoot,
        string stagingRoot,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        string temporaryPayloadPath = Path.Combine(stagingRoot, $"payload-{Guid.NewGuid():N}");

        try
        {
            await CreatePayloadAsync(file, packageId, compression, temporaryPayloadPath, zstdCodec, cancellationToken).ConfigureAwait(false);

            var information = new FileInfo(temporaryPayloadPath);
            long payloadSize = information.Length;
            string artifactHash = await Sha256File.ComputeAsync(temporaryPayloadPath, cancellationToken).ConfigureAwait(false);
            ManifestArtifact.FileArtifact artifact = payloadSize <= maxArtifactBytes
                ? await CreateSingleAsync(
                    packageId,
                    compression,
                    artifactHash,
                    payloadSize,
                    temporaryPayloadPath,
                    stagingRoot,
                    cancellationToken).ConfigureAwait(false)
                : await CreatePartsAsync(
                    packageId,
                    compression,
                    artifactHash,
                    payloadSize,
                    maxArtifactBytes,
                    temporaryPayloadPath,
                    stagingRoot,
                    cancellationToken).ConfigureAwait(false);

            string destinationDirectory = PackagePath.Resolve(outputRoot, artifact.ContentAddressedSortKey(packageId));

            if (Directory.Exists(destinationDirectory))
            {
                await VerifyExistingArtifactDirectoryAsync(outputRoot, artifact, packageId, cancellationToken).ConfigureAwait(false);
                DeleteDirectoryIfExists(PackagePath.Resolve(stagingRoot, artifact.ContentAddressedSortKey(packageId)));
                return new FileArtifactWriteResult(artifact, wasCreated: false, payloadSize);
            }

            return new FileArtifactWriteResult(artifact, wasCreated: true, payloadSize);
        }
        finally
        {
            if (File.Exists(temporaryPayloadPath))
            {
                File.Delete(temporaryPayloadPath);
            }
        }
    }

    private static async Task CreatePayloadAsync(
        SourceFileSnapshot file,
        string packageId,
        CompressionKind compression,
        string payloadPath,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        using SafeFileHandle handle = SourceSnapshotter.OpenVerifiedFile(file, packageId);
        await using var source = new FileStream(handle, FileAccess.Read, StreamBufferSize, isAsync: false);
        using var hashingSource = new HashingReadStream(source);
        await using var destination = new FileStream(
            payloadPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (compression == CompressionKind.None)
        {
            await hashingSource.CopyToAsync(destination, StreamBufferSize, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (zstdCodec == null || zstdCodec.CodecId != CompressionCodecIds.Zstd)
            {
                throw Failure(
                    PackageErrorCodes.MissingCompressionCodec,
                    "The package configuration requires zstd, but no compatible codec was supplied.",
                    packageId,
                    file.RelativePath);
            }

            await zstdCodec.CompressAsync(hashingSource, destination, cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        SourceSnapshotter.VerifyOpenedFile(handle, file, packageId);
        string secondFileHash = hashingSource.FinalizeHash();

        if (hashingSource.BytesRead != file.Size || secondFileHash != file.FileHash)
        {
            throw Failure(
                PackageErrorCodes.SourceChanged,
                "The source file bytes changed after the initial snapshot hash.",
                packageId,
                file.RelativePath);
        }
    }

    private static async Task<ManifestArtifact.FileArtifact> CreateSingleAsync(
        string packageId,
        CompressionKind compression,
        string artifactHash,
        long payloadSize,
        string payloadPath,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        string canonicalPath = ContentAddressedPath.FileSinglePayloadPath(packageId, artifactHash, compression);
        string stagedPath = PackagePath.Resolve(stagingRoot, canonicalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        File.Move(payloadPath, stagedPath);
        await VerifyObjectAsync(stagedPath, payloadSize, artifactHash, packageId, cancellationToken).ConfigureAwait(false);

        return new ManifestArtifact.FileArtifact(
            compression,
            new FilePayload.Single(canonicalPath, payloadSize, artifactHash));
    }

    private static async Task<ManifestArtifact.FileArtifact> CreatePartsAsync(
        string packageId,
        CompressionKind compression,
        string artifactHash,
        long payloadSize,
        long maxArtifactBytes,
        string payloadPath,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var parts = new List<FilePart>();
        byte[] buffer = new byte[checked((int)Math.Min(maxArtifactBytes, StreamBufferSize))];
        await using var source = new FileStream(
            payloadPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long remaining = payloadSize;
        long partIndex = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long targetSize = Math.Min(maxArtifactBytes, remaining);
            string canonicalPath = ContentAddressedPath.FilePartPath(packageId, artifactHash, partIndex);
            string stagedPath = PackagePath.Resolve(stagingRoot, canonicalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);

            await using (var destination = new FileStream(
                stagedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                long written = 0;

                while (written < targetSize)
                {
                    int requested = (int)Math.Min(buffer.Length, targetSize - written);
                    int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw Failure(PackageErrorCodes.ArtifactCorrupted, "The staged payload ended before its declared size.", packageId);
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string partHash = await Sha256File.ComputeAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            parts.Add(new FilePart(partIndex, canonicalPath, targetSize, partHash));
            remaining -= targetSize;
            partIndex++;
        }

        return new ManifestArtifact.FileArtifact(
            compression,
            new FilePayload.Parts(payloadSize, artifactHash, parts));
    }

    private static async Task VerifyExistingArtifactDirectoryAsync(
        string outputRoot,
        ManifestArtifact.FileArtifact artifact,
        string packageId,
        CancellationToken cancellationToken)
    {
        string directoryPath = PackagePath.Resolve(outputRoot, artifact.ContentAddressedSortKey(packageId));
        HashSet<string> expectedNames = artifact.GetPayloadObjects()
            .Select(item => Path.GetFileName(PackagePath.Resolve(outputRoot, item.Path)))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> actualNames = Directory.GetFileSystemEntries(directoryPath)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal)!;

        if (!expectedNames.SetEquals(actualNames))
        {
            throw Failure(
                PackageErrorCodes.ImmutablePathConflict,
                "An existing artifact directory contains a different payload representation.",
                packageId);
        }

        await PackagePayloadVerifier.VerifyStoredObjectsAsync(
            outputRoot,
            artifact.GetPayloadObjects(),
            packageId,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyObjectAsync(
        string path,
        long expectedSize,
        string expectedHash,
        string packageId,
        CancellationToken cancellationToken)
    {
        var information = new FileInfo(path);
        string actualHash = await Sha256File.ComputeAsync(path, cancellationToken).ConfigureAwait(false);

        if (information.Length != expectedSize || actualHash != expectedHash)
        {
            throw Failure(PackageErrorCodes.ArtifactCorrupted, "A staged artifact object failed verification.", packageId);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static PackageException Failure(string code, string message, string packageId, string? relativePath = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath));
    }
}