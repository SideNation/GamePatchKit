using System.Formats.Tar;
using System.Globalization;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

internal static class BundleArchiveReader
{
    private const string Stage = "bundle-verify";
    private const int StreamBufferSize = 64 * 1024;

    private static readonly UnixFileMode _expectedMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.OtherRead;

    public static Task VerifyAsync(
        string outputRoot,
        ReleaseManifest manifest,
        ManifestArtifact.BundleArtifact artifact,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        return ReadAsync(outputRoot, manifest, artifact, zstdCodec, destinationRoot: null, cancellationToken);
    }

    public static Task ExtractAsync(
        string outputRoot,
        ReleaseManifest manifest,
        ManifestArtifact.BundleArtifact artifact,
        ICompressionCodec? zstdCodec,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("Destination root must not be empty.", nameof(destinationRoot));
        }

        return ReadAsync(outputRoot, manifest, artifact, zstdCodec, destinationRoot, cancellationToken);
    }

    private static async Task ReadAsync(
        string outputRoot,
        ReleaseManifest manifest,
        ManifestArtifact.BundleArtifact artifact,
        ICompressionCodec? zstdCodec,
        string? destinationRoot,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ManifestFileEntry> filesByEntryPath = manifest.Files
            .Where(file => file.Group == artifact.Group
                && file.Source is FileSource.BundleEntryReference reference
                && reference.ArtifactHash == artifact.ArtifactHash)
            .ToDictionary(file => ((FileSource.BundleEntryReference)file.Source).EntryPath, StringComparer.Ordinal);

        if (filesByEntryPath.Count != artifact.Entries.Count)
        {
            throw Failure(
                PackageErrorCodes.ManifestInvalid,
                "The bundle entry references do not match the archive manifest.",
                manifest.PackageId,
                group: artifact.Group);
        }

        long expectedTarSize = DeterministicPaxTarWriter.ComputeArchiveSize(
            artifact.Entries.Select(entry => (entry.Path, filesByEntryPath[entry.Path].Size)));
        string? temporaryTarPath = null;
        string? canonicalTarPath = null;

        try
        {
            string payloadPath = PackagePath.Resolve(outputRoot, artifact.Path);
            string tarPath = payloadPath;

            if (artifact.Compression == CompressionKind.Zstd)
            {
                if (zstdCodec == null || zstdCodec.CodecId != CompressionCodecIds.Zstd)
                {
                    throw Failure(
                        PackageErrorCodes.MissingCompressionCodec,
                        "The bundle requires zstd, but no compatible codec was supplied.",
                        manifest.PackageId,
                        group: artifact.Group);
                }

                temporaryTarPath = Path.Combine(Path.GetTempPath(), $"gpk-bundle-{Guid.NewGuid():N}.tar");
                await using var compressed = new FileStream(
                    payloadPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var tar = new FileStream(
                    temporaryTarPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var limitedTar = new MaximumLengthWriteStream(tar, expectedTarSize);
                await zstdCodec.DecompressAsync(compressed, limitedTar, cancellationToken).ConfigureAwait(false);

                if (limitedTar.BytesWritten != expectedTarSize)
                {
                    throw new InvalidDataException("The decompressed bundle tar has an unexpected length.");
                }

                await tar.FlushAsync(cancellationToken).ConfigureAwait(false);
                tarPath = temporaryTarPath;
            }

            await using var archive = new FileStream(
                tarPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var reader = new TarReader(archive, leaveOpen: true);
            canonicalTarPath = Path.Combine(Path.GetTempPath(), $"gpk-canonical-bundle-{Guid.NewGuid():N}.tar");

            await using (var canonicalArchive = new FileStream(
                canonicalTarPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var canonicalWriter = new DeterministicPaxTarWriter(canonicalArchive))
            {
                foreach (BundleEntry expectedEntry in artifact.Entries)
                {
                    TarEntry? entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false);
                    if (entry == null)
                    {
                        throw Failure(
                            PackageErrorCodes.ArtifactCorrupted,
                            "The bundle archive ended before all declared entries were read.",
                            manifest.PackageId,
                            expectedEntry.Path,
                            artifact.Group);
                    }

                    ValidateEntryMetadata(entry, expectedEntry.Path, manifest.PackageId, artifact.Group);

                    if (!filesByEntryPath.TryGetValue(expectedEntry.Path, out ManifestFileEntry? file))
                    {
                        throw Failure(
                            PackageErrorCodes.ManifestInvalid,
                            "A bundle entry has no matching file reference.",
                            manifest.PackageId,
                            expectedEntry.Path,
                            artifact.Group);
                    }

                    await ReadEntryDataAsync(
                        entry,
                        file,
                        destinationRoot,
                        manifest.PackageId,
                        canonicalWriter,
                        cancellationToken).ConfigureAwait(false);
                }

                if (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) != null)
                {
                    throw Failure(
                        PackageErrorCodes.ArtifactCorrupted,
                        "The bundle archive contains an undeclared entry.",
                        manifest.PackageId,
                        group: artifact.Group);
                }
            }

            await VerifyCanonicalTarBytesAsync(
                tarPath,
                canonicalTarPath,
                manifest.PackageId,
                artifact.Group,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (temporaryTarPath != null && File.Exists(temporaryTarPath))
            {
                File.Delete(temporaryTarPath);
            }

            if (canonicalTarPath != null && File.Exists(canonicalTarPath))
            {
                File.Delete(canonicalTarPath);
            }
        }
    }

    private static void ValidateEntryMetadata(TarEntry entry, string expectedPath, string packageId, string group)
    {
        if (entry is not PaxTarEntry pax
            || entry.EntryType != TarEntryType.RegularFile
            || entry.Name != expectedPath
            || entry.ModificationTime != DateTimeOffset.UnixEpoch
            || entry.Uid != 0
            || entry.Gid != 0
            || entry.Mode != _expectedMode
            || pax.UserName != string.Empty
            || pax.GroupName != string.Empty
            || !HasOnlyCanonicalExtendedAttributes(pax))
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "A bundle entry violates the deterministic PAX metadata contract.",
                packageId,
                expectedPath,
                group);
        }
    }

    private static bool HasOnlyCanonicalExtendedAttributes(PaxTarEntry entry)
    {
        if (entry.ExtendedAttributes.Count != 3)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> attribute in entry.ExtendedAttributes)
        {
            switch (attribute.Key)
            {
                case "path":
                    if (attribute.Value != entry.Name)
                    {
                        return false;
                    }

                    break;
                case "mtime":
                    if (!decimal.TryParse(attribute.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal seconds)
                        || seconds != decimal.Zero)
                    {
                        return false;
                    }

                    break;
                case "size":
                    if (!long.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long size)
                        || size != entry.Length)
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static async Task ReadEntryDataAsync(
        TarEntry entry,
        ManifestFileEntry file,
        string? destinationRoot,
        string packageId,
        DeterministicPaxTarWriter canonicalWriter,
        CancellationToken cancellationToken)
    {
        if (entry.DataStream == null || entry.Length != file.Size)
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "A bundle entry has an unexpected data length.",
                packageId,
                file.Path,
                file.Group);
        }

        FileStream? destination = null;

        try
        {
            if (destinationRoot != null)
            {
                string destinationPath = PackagePath.Resolve(destinationRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                destination = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }

            using var verifiedSource = new HashingReadStream(entry.DataStream, destination);
            await canonicalWriter.WriteFileAsync(
                file.Path,
                file.Size,
                verifiedSource,
                cancellationToken).ConfigureAwait(false);

            if (destination != null)
            {
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string actualHash = verifiedSource.FinalizeHash();
            if (verifiedSource.BytesRead != file.Size || actualHash != file.FileHash)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "A bundle entry does not reconstruct the declared source file.",
                    packageId,
                    file.Path,
                    file.Group);
            }
        }
        finally
        {
            if (destination != null)
            {
                await destination.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task VerifyCanonicalTarBytesAsync(
        string actualTarPath,
        string canonicalTarPath,
        string packageId,
        string group,
        CancellationToken cancellationToken)
    {
        var actualInformation = new FileInfo(actualTarPath);
        var canonicalInformation = new FileInfo(canonicalTarPath);
        string actualHash = await Sha256File.ComputeAsync(actualTarPath, cancellationToken).ConfigureAwait(false);
        string canonicalHash = await Sha256File.ComputeAsync(canonicalTarPath, cancellationToken).ConfigureAwait(false);

        if (actualInformation.Length != canonicalInformation.Length || actualHash != canonicalHash)
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "The bundle archive bytes do not match the deterministic PAX contract.",
                packageId,
                group: group);
        }
    }

    private static PackageException Failure(
        string code,
        string message,
        string packageId,
        string? relativePath = null,
        string? group = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath, group));
    }

    private sealed class MaximumLengthWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumLength;

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public MaximumLengthWriteStream(Stream inner, long maximumLength)
        {
            _inner = inner;
            _maximumLength = maximumLength;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten += buffer.Length;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return WriteLegacyAsync(buffer, offset, count, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        private void EnsureCapacity(int count)
        {
            if (count > _maximumLength - BytesWritten)
            {
                throw new InvalidDataException("The decompressed bundle exceeds its declared deterministic tar size.");
            }
        }

        private async Task WriteLegacyAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesWritten += count;
        }
    }
}