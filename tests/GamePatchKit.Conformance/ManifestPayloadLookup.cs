using GamePatchKit.Core.Manifests;
using GamePatchKit.Runtime;

namespace GamePatchKit.Conformance;

public static class ManifestPayloadLookup
{
    public static TargetManifestReference Target(FinalizedManifest release)
    {
        return new TargetManifestReference(release.Manifest.PackageId, release.Manifest.DataVersion, release.ManifestHash);
    }

    // The one payload object a single (unsplit) file artifact reconstructs from - the same relative path
    // HttpArtifactTransport requests and FileSystemRuntimeStorage caches under.
    public static string PayloadPathFor(FinalizedManifest release, string filePath)
    {
        ManifestFileEntry file = release.Manifest.Files.Single(entry => entry.Path == filePath);
        string artifactHash = ((FileSource.FileReference)file.Source).ArtifactHash;

        return release.Manifest.Artifacts
            .OfType<ManifestArtifact.FileArtifact>()
            .Single(artifact => artifact.PrimaryArtifactHash == artifactHash)
            .GetPayloadObjects()
            .Single()
            .Path;
    }
}
