using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Tests;

// Builds valid release manifests for the identity, diff and download-plan tests. Content-addressed paths come
// from ContentAddressedPath and every array is placed in canonical order, and Manifest() asserts the result
// passes ManifestValidator - so a failing test is failing on the behaviour under test, not on a hand-built
// fixture that was quietly invalid.
internal static class SampleManifests
{
    public const string PackageId = "sample-package";

    public const string PlaceholderDataVersion = "v1-0000000000000000000000000000000000000000000000000000000000000000";

    // Readable stand-in digests: Hash('a') is "aaaa...". Distinct letters mean distinct content.
    public static string Hash(char hexDigit)
    {
        return new string(hexDigit, 64);
    }

    public static ManifestArtifact.FileArtifact SingleArtifact(
        string artifactHash,
        long size,
        CompressionKind compression = CompressionKind.None,
        string packageId = PackageId)
    {
        return new ManifestArtifact.FileArtifact(
            compression,
            new FilePayload.Single(ContentAddressedPath.FileSinglePayloadPath(packageId, artifactHash, compression), size, artifactHash));
    }

    public static ManifestArtifact.FileArtifact PartsArtifact(
        string artifactHash,
        IReadOnlyList<(long Size, string PartHash)> parts,
        CompressionKind compression = CompressionKind.None,
        string packageId = PackageId)
    {
        var partList = new List<FilePart>();
        long payloadSize = 0;

        for (int index = 0; index < parts.Count; index++)
        {
            partList.Add(new FilePart(index, ContentAddressedPath.FilePartPath(packageId, artifactHash, index), parts[index].Size, parts[index].PartHash));
            payloadSize += parts[index].Size;
        }

        return new ManifestArtifact.FileArtifact(compression, new FilePayload.Parts(payloadSize, artifactHash, partList));
    }

    public static ManifestArtifact.BundleArtifact BundleArtifact(
        string group,
        string artifactHash,
        long size,
        IReadOnlyList<string> entryPaths,
        CompressionKind compression = CompressionKind.None,
        string packageId = PackageId)
    {
        List<BundleEntry> entries = entryPaths
            .OrderBy(path => path, Utf8OrdinalStringComparer.Instance)
            .Select(path => new BundleEntry(path))
            .ToList();

        return new ManifestArtifact.BundleArtifact(
            group,
            ContentAddressedPath.BundleArtifactPath(packageId, group, artifactHash, compression),
            size,
            artifactHash,
            compression,
            entries);
    }

    // Uncompressed: the stored payload is the original file, so fileHash and artifactHash are the same digest.
    public static ManifestFileEntry FileFromArtifact(string path, string group, long size, string artifactHash)
    {
        return FileFromArtifact(path, group, size, artifactHash, artifactHash);
    }

    // Compressed: size and fileHash describe the original file, artifactHash the stored payload.
    public static ManifestFileEntry FileFromArtifact(string path, string group, long size, string fileHash, string artifactHash)
    {
        return new ManifestFileEntry(path, group, size, fileHash, new FileSource.FileReference(artifactHash));
    }

    public static ManifestFileEntry FileFromBundle(string path, string group, long size, string fileHash, string bundleHash)
    {
        return new ManifestFileEntry(path, group, size, fileHash, new FileSource.BundleEntryReference(bundleHash, path));
    }

    public static ReleaseManifest Manifest(
        IEnumerable<ManifestGroupEntry> groups,
        IEnumerable<ManifestArtifact> artifacts,
        IEnumerable<ManifestFileEntry> files,
        string packageId = PackageId,
        string dataVersion = PlaceholderDataVersion,
        long compactVersion = 0,
        int schemaVersion = 1)
    {
        var manifest = new ReleaseManifest(
            schemaVersion,
            packageId,
            dataVersion,
            compactVersion,
            groups.OrderBy(group => group.Name, Utf8OrdinalStringComparer.Instance).ToList(),
            artifacts.OrderBy(artifact => artifact.ContentAddressedSortKey(packageId), Utf8OrdinalStringComparer.Instance).ToList(),
            files.OrderBy(file => file.Path, Utf8OrdinalStringComparer.Instance).ToList());

        ValidationResult validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, "Sample manifest is not valid: " + string.Join("; ", validation.Errors));

        return manifest;
    }
}
