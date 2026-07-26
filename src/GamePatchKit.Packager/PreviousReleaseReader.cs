using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

internal sealed class PreviousReleaseContext
{
    public ReleaseManifest Manifest { get; }

    public FinalizedManifest FinalizedManifest { get; }

    public IReadOnlyDictionary<string, ManifestFileEntry> FilesByPath { get; }

    public IReadOnlyDictionary<string, ManifestArtifact.FileArtifact> FileArtifactsByHash { get; }

    public IReadOnlyDictionary<string, ManifestArtifact.BundleArtifact> BundleArtifactsByHash { get; }

    public IReadOnlyDictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact> FileArtifactsByContent { get; }

    public PreviousReleaseContext(
        ReleaseManifest manifest,
        FinalizedManifest finalizedManifest,
        IReadOnlyDictionary<string, ManifestFileEntry> filesByPath,
        IReadOnlyDictionary<string, ManifestArtifact.FileArtifact> fileArtifactsByHash,
        IReadOnlyDictionary<string, ManifestArtifact.BundleArtifact> bundleArtifactsByHash,
        IReadOnlyDictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact> fileArtifactsByContent)
    {
        Manifest = manifest;
        FinalizedManifest = finalizedManifest;
        FilesByPath = filesByPath;
        FileArtifactsByHash = fileArtifactsByHash;
        BundleArtifactsByHash = bundleArtifactsByHash;
        FileArtifactsByContent = fileArtifactsByContent;
    }
}

internal static class PreviousReleaseReader
{
    private const string Stage = "previous-manifest";

    public static async Task<PreviousReleaseContext> ReadAsync(
        PreviousRelease previousRelease,
        string expectedPackageId,
        string outputRoot,
        GamePatchKit.Core.ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        byte[] bytes = previousRelease.GetManifestBytes();
        ReleaseManifest manifest = ManifestDocumentReader.Read(
            bytes,
            expectedPackageId,
            previousRelease.ManifestHash,
            Stage,
            PackageErrorCodes.InvalidPreviousManifest);

        await PackagePayloadVerifier.VerifyAsync(outputRoot, manifest, zstdCodec, cancellationToken).ConfigureAwait(false);

        var filesByPath = manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var fileArtifactsByHash = manifest.Artifacts
            .OfType<ManifestArtifact.FileArtifact>()
            .ToDictionary(artifact => artifact.PrimaryArtifactHash, StringComparer.Ordinal);
        var bundleArtifactsByHash = manifest.Artifacts
            .OfType<ManifestArtifact.BundleArtifact>()
            .ToDictionary(artifact => artifact.ArtifactHash, StringComparer.Ordinal);
        var fileArtifactsByContent = new Dictionary<(long Size, string FileHash), ManifestArtifact.FileArtifact>();

        foreach (ManifestFileEntry file in manifest.Files)
        {
            if (file.Source is FileSource.FileReference reference
                && fileArtifactsByHash.TryGetValue(reference.ArtifactHash, out ManifestArtifact.FileArtifact? artifact))
            {
                fileArtifactsByContent.TryAdd((file.Size, file.FileHash), artifact);
            }
        }

        return new PreviousReleaseContext(
            manifest,
            new FinalizedManifest(manifest, bytes, previousRelease.ManifestHash),
            filesByPath,
            fileArtifactsByHash,
            bundleArtifactsByHash,
            fileArtifactsByContent);
    }
}
