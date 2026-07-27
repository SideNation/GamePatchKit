using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

// Reads a release manifest and checks everything that can be checked about the document itself: schema, the
// declared manifestHash, canonical bytes, packageId, and Core's semantic and reference rules.
//
// Deliberately stops short of the payloads. A diff, a download plan or a retained-object inventory is a
// statement about what manifests declare, and making each of them re-hash every stored object would turn a
// question about two documents into a full verification of the whole package. Use ReleaseVerifier when the
// bytes themselves are what is in question.
public static class ReleaseManifestReader
{
    private const string Stage = "release-manifest";

    public static async Task<byte[]> ReadPublishedBytesAsync(
        string outputRoot,
        string packageId,
        string manifestHash,
        CancellationToken cancellationToken = default)
    {
        string path = PackagePath.Resolve(outputRoot, PackageLayout.ManifestPath(packageId, manifestHash));

        if (!File.Exists(path))
        {
            throw new PackageException(new GamePatchKitError(
                Stage,
                PackageErrorCodes.ManifestNotFound,
                "The output tree has no manifest at the requested manifestHash.",
                packageId));
        }

        return await BoundedFile.ReadAllBytesAsync(
            path,
            PackageLayout.MaximumManifestBytes,
            Stage,
            PackageErrorCodes.InvalidManifestDocument,
            $"The published manifest is larger than the {PackageLayout.MaximumManifestBytes} byte limit.",
            packageId,
            cancellationToken).ConfigureAwait(false);
    }

    public static ReleaseManifest Read(byte[] manifestBytes, string packageId, string manifestHash)
    {
        if (manifestBytes == null)
        {
            throw new ArgumentNullException(nameof(manifestBytes));
        }

        return ManifestDocumentReader.Read(
            manifestBytes,
            packageId,
            manifestHash,
            Stage,
            PackageErrorCodes.InvalidManifestDocument);
    }

    public static async Task<ReleaseManifest> ReadPublishedAsync(
        string outputRoot,
        string packageId,
        string manifestHash,
        CancellationToken cancellationToken = default)
    {
        byte[] manifestBytes = await ReadPublishedBytesAsync(outputRoot, packageId, manifestHash, cancellationToken)
            .ConfigureAwait(false);

        return Read(manifestBytes, packageId, manifestHash);
    }
}
