using System.Text;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Runtime.Tests;

internal static class RuntimeFixture
{
    public const string PackageId = "runtime-package";

    public static readonly byte[] CoreBytes = Encoding.UTF8.GetBytes("core-data");
    public static readonly byte[] MapsBytes = Encoding.UTF8.GetBytes("maps-data");
    public static readonly byte[] AudioBytes = Encoding.UTF8.GetBytes("audio-data");

    public static FinalizedManifest CreateRelease(
        byte[]? coreBytes = null,
        byte[]? mapsBytes = null,
        bool mapsRequired = false,
        bool includeAudio = false,
        long compactVersion = 0)
    {
        coreBytes ??= CoreBytes;
        mapsBytes ??= MapsBytes;

        var groups = new List<ManifestGroupEntry>
        {
            new("core", required: true),
            new("maps", mapsRequired),
        };
        var files = new List<ManifestFileEntry>();
        var artifacts = new List<ManifestArtifact>();

        AddFile("data/core.bin", "core", coreBytes, artifacts, files);
        AddFile("data/maps.bin", "maps", mapsBytes, artifacts, files);

        if (includeAudio)
        {
            groups.Add(new ManifestGroupEntry("audio", required: false));
            AddFile("data/audio.bin", "audio", AudioBytes, artifacts, files);
        }

        groups.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Name, right.Name));
        files.Sort((left, right) => Utf8OrdinalStringComparer.Instance.Compare(left.Path, right.Path));
        artifacts.Sort(
            (left, right) => Utf8OrdinalStringComparer.Instance.Compare(
                left.ContentAddressedSortKey(PackageId),
                right.ContentAddressedSortKey(PackageId)));

        var draft = new ReleaseManifest(
            schemaVersion: 1,
            PackageId,
            "v1-" + new string('0', 64),
            compactVersion,
            groups,
            artifacts,
            files);

        Assert.True(ManifestValidator.Validate(draft).IsValid);
        return ReleaseIdentity.Finalize(draft, compactVersion);
    }

    public static TargetManifestReference Target(FinalizedManifest release)
    {
        return new TargetManifestReference(
            release.Manifest.PackageId,
            release.Manifest.DataVersion,
            release.ManifestHash);
    }

    public static string Hash(byte[] bytes)
    {
        return Sha256Hash.ComputeHex(bytes);
    }

    private static void AddFile(
        string path,
        string group,
        byte[] bytes,
        List<ManifestArtifact> artifacts,
        List<ManifestFileEntry> files)
    {
        string hash = Hash(bytes);
        artifacts.Add(
            new ManifestArtifact.FileArtifact(
                CompressionKind.None,
                new FilePayload.Single(
                    ContentAddressedPath.FileSinglePayloadPath(PackageId, hash, CompressionKind.None),
                    bytes.LongLength,
                    hash)));
        files.Add(
            new ManifestFileEntry(
                path,
                group,
                bytes.LongLength,
                hash,
                new FileSource.FileReference(hash)));
    }
}
