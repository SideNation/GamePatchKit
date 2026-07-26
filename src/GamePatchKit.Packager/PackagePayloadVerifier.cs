using System.Security.Cryptography;
using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

public static class PackagePayloadVerifier
{
    private const string Stage = "package-verify";
    private const int StreamBufferSize = 64 * 1024;

    public static async Task VerifyAsync(
        string outputRoot,
        ReleaseManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await VerifyAsync(
            outputRoot,
            manifest,
            ZstdCompressionCodecFactory.Create(),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task VerifyAsync(
        string outputRoot,
        ReleaseManifest manifest,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root must not be empty.", nameof(outputRoot));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        ValidationResult semanticValidation = ManifestValidator.Validate(manifest);
        if (!semanticValidation.IsValid)
        {
            throw new PackageException(semanticValidation.Errors);
        }

        foreach (ManifestArtifact artifact in manifest.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await VerifyStoredObjectsAsync(outputRoot, artifact.GetPayloadObjects(), manifest.PackageId, cancellationToken).ConfigureAwait(false);

                if (artifact is ManifestArtifact.FileArtifact fileArtifact)
                {
                    VerifyFileArtifactDirectoryShape(outputRoot, manifest.PackageId, fileArtifact);
                    await VerifyCombinedPayloadHashAsync(outputRoot, manifest.PackageId, fileArtifact, cancellationToken).ConfigureAwait(false);
                    await VerifyFileArtifactAsync(outputRoot, manifest, fileArtifact, zstdCodec, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (PackageException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "An artifact payload failed byte verification.",
                    manifest.PackageId);
            }
        }
    }

    internal static async Task VerifyStoredObjectsAsync(
        string outputRoot,
        IReadOnlyList<ArtifactPayloadObject> objects,
        string packageId,
        CancellationToken cancellationToken)
    {
        foreach (ArtifactPayloadObject payloadObject in objects)
        {
            string path = PackagePath.Resolve(outputRoot, payloadObject.Path);
            var information = new FileInfo(path);

            if (!information.Exists
                || (information.Attributes & FileAttributes.ReparsePoint) != 0
                || information.Length != payloadObject.Size)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "An artifact object is missing or has an unexpected size.",
                    packageId);
            }

            string actualHash = await ComputeFileHashAsync(path, cancellationToken).ConfigureAwait(false);
            if (actualHash != payloadObject.ObjectHash)
            {
                throw Failure(
                    PackageErrorCodes.ArtifactCorrupted,
                    "An artifact object has an unexpected SHA-256.",
                    packageId);
            }
        }
    }

    private static void VerifyFileArtifactDirectoryShape(
        string outputRoot,
        string packageId,
        ManifestArtifact.FileArtifact artifact)
    {
        string directoryPath = PackagePath.Resolve(outputRoot, artifact.ContentAddressedSortKey(packageId));
        var directory = new DirectoryInfo(directoryPath);

        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(PackageErrorCodes.ArtifactCorrupted, "A file artifact directory is missing or is a link.", packageId);
        }

        HashSet<string> expectedNames = artifact.GetPayloadObjects()
            .Select(payload => Path.GetFileName(PackagePath.Resolve(outputRoot, payload.Path)))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> actualNames = directory.EnumerateFileSystemInfos()
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (!expectedNames.SetEquals(actualNames))
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "A file artifact directory contains an unexpected payload representation.",
                packageId);
        }
    }

    private static async Task VerifyFileArtifactAsync(
        string outputRoot,
        ReleaseManifest manifest,
        ManifestArtifact.FileArtifact artifact,
        ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        ManifestFileEntry? referencedFile = manifest.Files.FirstOrDefault(
            file => file.Source is FileSource.FileReference reference && reference.ArtifactHash == artifact.PrimaryArtifactHash);

        if (referencedFile == null)
        {
            throw Failure(PackageErrorCodes.ManifestInvalid, "A file artifact is not referenced by a file.", manifest.PackageId);
        }

        await using var payload = new ArtifactObjectReadStream(outputRoot, artifact.GetPayloadObjects());
        using var verifiedOutput = new HashingWriteStream();

        if (artifact.Compression == CompressionKind.None)
        {
            await payload.CopyToAsync(verifiedOutput, StreamBufferSize, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (zstdCodec == null || zstdCodec.CodecId != CompressionCodecIds.Zstd)
            {
                throw Failure(
                    PackageErrorCodes.MissingCompressionCodec,
                    "The manifest requires the zstd codec, but no compatible codec was supplied.",
                    manifest.PackageId);
            }

            await zstdCodec.DecompressAsync(payload, verifiedOutput, cancellationToken).ConfigureAwait(false);
        }

        string fileHash = verifiedOutput.FinalizeHash();
        if (verifiedOutput.BytesWritten != referencedFile.Size || fileHash != referencedFile.FileHash)
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "A file artifact does not reconstruct the declared source file.",
                manifest.PackageId,
                referencedFile.Path);
        }
    }

    private static async Task VerifyCombinedPayloadHashAsync(
        string outputRoot,
        string packageId,
        ManifestArtifact.FileArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (artifact.Payload is not FilePayload.Parts parts)
        {
            return;
        }

        await using var payload = new ArtifactObjectReadStream(outputRoot, artifact.GetPayloadObjects());
        using var hash = new HashingWriteStream();
        await payload.CopyToAsync(hash, StreamBufferSize, cancellationToken).ConfigureAwait(false);
        string actualHash = hash.FinalizeHash();

        if (hash.BytesWritten != parts.Size || actualHash != parts.ArtifactHash)
        {
            throw Failure(
                PackageErrorCodes.ArtifactCorrupted,
                "The ordered file parts do not reconstruct the declared artifact payload.",
                packageId);
        }
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[StreamBufferSize];

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static PackageException Failure(string code, string message, string packageId, string? relativePath = null)
    {
        return new PackageException(new GamePatchKitError(Stage, code, message, packageId, relativePath));
    }
}
