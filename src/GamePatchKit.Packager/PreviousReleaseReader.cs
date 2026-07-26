using System.Text;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<PreviousReleaseContext> ReadAsync(
        PreviousRelease previousRelease,
        string expectedPackageId,
        string outputRoot,
        GamePatchKit.Core.ICompressionCodec? zstdCodec,
        CancellationToken cancellationToken)
    {
        byte[] bytes = previousRelease.GetManifestBytes();
        GamePatchKitSchemaValidator.ValidateReleaseManifest(bytes, expectedPackageId);
        ReleaseManifest manifest;

        try
        {
            string json = _strictUtf8.GetString(bytes);
            var loadSettings = new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore,
            };
            JToken token = JToken.Parse(json, loadSettings);

            if (token is not JObject obj)
            {
                throw Failure(PackageErrorCodes.InvalidPreviousManifest, "The previous manifest root must be an object.", expectedPackageId);
            }

            if (!ReleaseManifest.TryParse(obj, out ReleaseManifest? parsed, out IReadOnlyList<GamePatchKitError> parseErrors))
            {
                throw new PackageException(parseErrors);
            }

            manifest = parsed!;
        }
        catch (PackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw Failure(PackageErrorCodes.InvalidPreviousManifest, "The previous manifest is not strict UTF-8 JSON.", expectedPackageId);
        }

        if (manifest.PackageId != expectedPackageId)
        {
            throw Failure(PackageErrorCodes.PackageIdMismatch, "The previous manifest belongs to a different package.", expectedPackageId);
        }

        ValidationResult hashValidation = ReleaseIdentity.VerifyManifestHash(bytes, previousRelease.ManifestHash);
        if (!hashValidation.IsValid)
        {
            throw new PackageException(hashValidation.Errors);
        }

        byte[] canonicalBytes = ReleaseIdentity.ComputeCanonicalBytes(manifest);
        if (!bytes.AsSpan().SequenceEqual(canonicalBytes))
        {
            throw Failure(PackageErrorCodes.InvalidPreviousManifest, "The previous manifest bytes are not canonical.", expectedPackageId);
        }

        ValidationResult semanticValidation = ManifestValidator.Validate(manifest);
        if (!semanticValidation.IsValid)
        {
            throw new PackageException(semanticValidation.Errors);
        }

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

    private static PackageException Failure(string code, string message, string packageId)
    {
        return new PackageException(Error(code, message, packageId));
    }

    private static GamePatchKitError Error(string code, string message, string packageId)
    {
        return new GamePatchKitError(Stage, code, message, packageId);
    }
}
