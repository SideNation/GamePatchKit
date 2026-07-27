using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using Microsoft.Win32.SafeHandles;

namespace GamePatchKit.Packager;

internal sealed class BundleArtifactWriteResult
{
    public ManifestArtifact.BundleArtifact Artifact { get; }

    public bool WasCreated { get; }

    public long PayloadBytes { get; }

    public BundleArtifactWriteResult(ManifestArtifact.BundleArtifact artifact, bool wasCreated, long payloadBytes)
    {
        Artifact = artifact;
        WasCreated = wasCreated;
        PayloadBytes = payloadBytes;
    }
}

internal sealed class BundleFallbackWriteResult
{
    public SourceFileSnapshot File { get; }

    public FileArtifactWriteResult ArtifactResult { get; }

    public BundleFallbackWriteResult(SourceFileSnapshot file, FileArtifactWriteResult artifactResult)
    {
        File = file;
        ArtifactResult = artifactResult;
    }
}

internal sealed class BundleGroupWriteResult
{
    public IReadOnlyList<BundleArtifactWriteResult> Bundles { get; }

    public IReadOnlyList<BundleFallbackWriteResult> Fallbacks { get; }

    public BundleGroupWriteResult(
        IReadOnlyList<BundleArtifactWriteResult> bundles,
        IReadOnlyList<BundleFallbackWriteResult> fallbacks)
    {
        Bundles = bundles;
        Fallbacks = fallbacks;
    }
}

internal static class BundleArtifactWriter
{
    private const string Stage = "bundle-artifact";
    private const int StreamBufferSize = 64 * 1024;
    private const long TarBlockSize = 512;
    private const long TarEndBlockBytes = TarBlockSize * 2;
    private const long ConservativePaxEntryOverhead = TarBlockSize * 4;

    public static async Task<BundleGroupWriteResult> WriteAsync(
        IReadOnlyList<SourceFileSnapshot> files,
        string packageId,
        string group,
        CompressionKind compression,
        long maxArtifactBytes,
        string outputRoot,
        string stagingRoot,
        ICompressionCodec? zstdCodec,
        IDictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact> reusableFileArtifacts,
        CancellationToken cancellationToken)
    {
        var bundles = new List<BundleArtifactWriteResult>();
        var fallbacks = new List<BundleFallbackWriteResult>();
        int firstIndex = 0;

        while (firstIndex < files.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = SelectConservativeEntryCount(files, firstIndex, maxArtifactBytes);
            BundlePayloadCandidate candidate = await CreateCandidateAsync(
                files,
                firstIndex,
                count,
                packageId,
                compression,
                stagingRoot,
                zstdCodec,
                cancellationToken).ConfigureAwait(false);

            while (candidate.Size > maxArtifactBytes && count > 1)
            {
                candidate.Delete();
                count--;
                candidate = await CreateCandidateAsync(
                    files,
                    firstIndex,
                    count,
                    packageId,
                    compression,
                    stagingRoot,
                    zstdCodec,
                    cancellationToken).ConfigureAwait(false);
            }

            if (candidate.Size > maxArtifactBytes)
            {
                candidate.Delete();
                SourceFileSnapshot file = files[firstIndex];
                FileArtifactWriteResult fallback;

                if (reusableFileArtifacts.TryGetValue((file.Size, file.FileHash), out ManifestArtifact.FileArtifact? reusable))
                {
                    fallback = new FileArtifactWriteResult(reusable, wasCreated: false, payloadBytes: 0);
                }
                else
                {
                    fallback = await FileArtifactWriter.WriteAsync(
                        file,
                        packageId,
                        compression,
                        maxArtifactBytes,
                        outputRoot,
                        stagingRoot,
                        zstdCodec,
                        cancellationToken).ConfigureAwait(false);
                }

                reusableFileArtifacts[(file.Size, file.FileHash)] = fallback.Artifact;
                fallbacks.Add(new BundleFallbackWriteResult(file, fallback));
                firstIndex++;
                continue;
            }

            BundleArtifactWriteResult result = await StageCandidateAsync(
                candidate,
                files,
                firstIndex,
                count,
                packageId,
                group,
                compression,
                outputRoot,
                stagingRoot,
                cancellationToken).ConfigureAwait(false);
            bundles.Add(result);
            firstIndex += count;
        }

        return new BundleGroupWriteResult(bundles, fallbacks);
    }

    private static int SelectConservativeEntryCount(
        IReadOnlyList<SourceFileSnapshot> files,
        int firstIndex,
        long maxArtifactBytes)
    {
        long estimatedSize = TarEndBlockBytes;
        int count = 0;

        for (int index = firstIndex; index < files.Count; index++)
        {
            long paddedDataSize = RoundUpToTarBlock(files[index].Size);
            long entrySize = SaturatingAdd(paddedDataSize, ConservativePaxEntryOverhead);
            long candidateSize = SaturatingAdd(estimatedSize, entrySize);

            if (count > 0 && candidateSize > maxArtifactBytes)
            {
                break;
            }

            estimatedSize = candidateSize;
            count++;
        }

        return Math.Max(count, 1);
    }

    private static async Task<BundlePayloadCandidate> CreateCandidateAsync(
        IReadOnlyList<SourceFileSnapshot> files,
        int firstIndex,
        int count,
        string packageId,
        CompressionKind compression,
        string stagingRoot,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        string tarPath = Path.Combine(stagingRoot, $"bundle-{Guid.NewGuid():N}.tar");
        string payloadPath = compression == CompressionKind.None
            ? tarPath
            : Path.Combine(stagingRoot, $"bundle-{Guid.NewGuid():N}.tar.zst");

        try
        {
            await WriteTarAsync(
                files,
                firstIndex,
                count,
                packageId,
                tarPath,
                cancellationToken).ConfigureAwait(false);

            if (compression == CompressionKind.Zstd)
            {
                if (zstdCodec == null || zstdCodec.CodecId != CompressionCodecIds.Zstd)
                {
                    throw Failure(
                        PackageErrorCodes.MissingCompressionCodec,
                        "A new bundle requires zstd, but no compatible codec was supplied.",
                        packageId);
                }

                await using var source = new FileStream(
                    tarPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(
                    payloadPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await zstdCodec.CompressAsync(source, destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                File.Delete(tarPath);
            }

            long size = new FileInfo(payloadPath).Length;
            string hash = await Sha256File.ComputeAsync(payloadPath, cancellationToken).ConfigureAwait(false);
            return new BundlePayloadCandidate(payloadPath, size, hash);
        }
        catch
        {
            DeleteFileIfExists(tarPath);
            DeleteFileIfExists(payloadPath);
            throw;
        }
    }

    private static async Task WriteTarAsync(
        IReadOnlyList<SourceFileSnapshot> files,
        int firstIndex,
        int count,
        string packageId,
        string tarPath,
        CancellationToken cancellationToken)
    {
        await using var archive = new FileStream(
            tarPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new DeterministicPaxTarWriter(archive);

        for (int index = firstIndex; index < firstIndex + count; index++)
        {
            SourceFileSnapshot file = files[index];
            using SafeFileHandle handle = SourceSnapshotter.OpenVerifiedFile(file, packageId);
            await using var source = new FileStream(handle, FileAccess.Read, StreamBufferSize, isAsync: false);
            using var hashingSource = new HashingReadStream(source);
            await writer.WriteFileAsync(file.RelativePath, file.Size, hashingSource, cancellationToken).ConfigureAwait(false);
            string actualHash = hashingSource.FinalizeHash();
            SourceSnapshotter.VerifyOpenedFile(handle, file, packageId);

            if (hashingSource.BytesRead != file.Size || actualHash != file.FileHash)
            {
                throw Failure(
                    PackageErrorCodes.SourceChanged,
                    "A source file changed while its bundle entry was written.",
                    packageId,
                    file.RelativePath);
            }
        }
    }

    private static async Task<BundleArtifactWriteResult> StageCandidateAsync(
        BundlePayloadCandidate candidate,
        IReadOnlyList<SourceFileSnapshot> files,
        int firstIndex,
        int count,
        string packageId,
        string group,
        CompressionKind compression,
        string outputRoot,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        string canonicalPath = ContentAddressedPath.BundleArtifactPath(packageId, group, candidate.Hash, compression);
        var artifact = new ManifestArtifact.BundleArtifact(
            group,
            canonicalPath,
            candidate.Size,
            candidate.Hash,
            compression,
            files.Skip(firstIndex).Take(count).Select(file => new BundleEntry(file.RelativePath)).ToList());
        string destinationPath = PackagePath.Resolve(outputRoot, canonicalPath);

        if (File.Exists(destinationPath))
        {
            await PackagePayloadVerifier.VerifyStoredObjectsAsync(
                outputRoot,
                artifact.GetPayloadObjects(),
                packageId,
                cancellationToken).ConfigureAwait(false);
            candidate.Delete();
            return new BundleArtifactWriteResult(artifact, wasCreated: false, candidate.Size);
        }

        string stagedPath = PackagePath.Resolve(stagingRoot, canonicalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        File.Move(candidate.Path, stagedPath);
        return new BundleArtifactWriteResult(artifact, wasCreated: true, candidate.Size);
    }

    private static long RoundUpToTarBlock(long size)
    {
        if (size > long.MaxValue - (TarBlockSize - 1))
        {
            return long.MaxValue;
        }

        return ((size + TarBlockSize - 1) / TarBlockSize) * TarBlockSize;
    }

    private static long SaturatingAdd(long left, long right)
    {
        return right > long.MaxValue - left ? long.MaxValue : left + right;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static PackageException Failure(string code, string message, string packageId, string? relativePath = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath));
    }

    private sealed class BundlePayloadCandidate
    {
        public string Path { get; }

        public long Size { get; }

        public string Hash { get; }

        public BundlePayloadCandidate(string path, long size, string hash)
        {
            Path = path;
            Size = size;
            Hash = hash;
        }

        public void Delete()
        {
            DeleteFileIfExists(Path);
        }
    }
}