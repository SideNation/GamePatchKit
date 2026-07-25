using System.Collections.Generic;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.GoldenVectors;

// Mirrors the independent Python generator used to produce tests/fixtures/golden-vectors/*
// (see the scratchpad gen_golden_vectors.py this was cross-checked against).
public static class GoldenVectorManifests
{
    public const string HashA = "30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6";
    public const string HashB = "cd43d82673a50ede733a204a3db6997dd349fb9963ee34950682433e97b1b512";
    public const string HashC = "f932608e42632d2607d7d540a1e1d70e752ba192823e69e9eed0d4d307ff45d8";
    public const string HashD = "2dc778fd7c0bf27cda8936092f7fd981515c4f5bbf3f3d082211462f12f3060e";
    public const string HashE = "d8588ec425bb08feffaf3f8725f0bd497f7d82f5f940fd1c3578a6c4e8529284";
    public const string HashF = "22faad9315c7ad20e8d6b370258cacf8039de80805268ef047087841e9078c05";
    public const string HashG = "34fd11640cef8bb04218acfa66ebca39481bb9e7b17b5ffcbada5ed720a19e6c";

    public static ReleaseManifest SingleFile(string dataVersion)
    {
        const string packageId = "golden-single-file";

        var artifact = new ManifestArtifact.FileArtifact(
            CompressionKind.None,
            new FilePayload.Single($"{packageId}/artifacts/files/{HashA}/content", 14, HashA));

        var file = new ManifestFileEntry("data/config.json", "core", 14, HashA, new FileSource.FileReference(HashA));

        return new ReleaseManifest(
            1,
            packageId,
            dataVersion,
            0,
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact> { artifact },
            new List<ManifestFileEntry> { file });
    }

    public static ReleaseManifest MultipartFile(string dataVersion)
    {
        const string packageId = "golden-multipart-file";

        var parts = new List<FilePart>
        {
            new FilePart(0, $"{packageId}/artifacts/files/{HashC}/part-00000", 12, HashD),
            new FilePart(1, $"{packageId}/artifacts/files/{HashC}/part-00001", 8, HashE),
        };

        var artifact = new ManifestArtifact.FileArtifact(CompressionKind.Zstd, new FilePayload.Parts(20, HashC, parts));

        var file = new ManifestFileEntry("data/big-file.bin", "core", 28, HashB, new FileSource.FileReference(HashC));

        return new ReleaseManifest(
            1,
            packageId,
            dataVersion,
            0,
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact> { artifact },
            new List<ManifestFileEntry> { file });
    }

    public static ReleaseManifest BundleEntry(string dataVersion)
    {
        const string packageId = "golden-bundle-entry";

        var artifact = new ManifestArtifact.BundleArtifact(
            "maps",
            $"{packageId}/artifacts/bundles/maps/{HashG}.tar",
            512,
            HashG,
            CompressionKind.None,
            new List<BundleEntry> { new BundleEntry("maps/level1.bin") });

        var file = new ManifestFileEntry(
            "maps/level1.bin",
            "maps",
            14,
            HashF,
            new FileSource.BundleEntryReference(HashG, "maps/level1.bin"));

        return new ReleaseManifest(
            1,
            packageId,
            dataVersion,
            0,
            new List<ManifestGroupEntry> { new ManifestGroupEntry("maps", false) },
            new List<ManifestArtifact> { artifact },
            new List<ManifestFileEntry> { file });
    }

    public static ReleaseManifest Mixed(string dataVersion)
    {
        const string packageId = "golden-mixed";

        var bundleArtifact = new ManifestArtifact.BundleArtifact(
            "optional-assets",
            $"{packageId}/artifacts/bundles/optional-assets/{HashG}.tar",
            512,
            HashG,
            CompressionKind.None,
            new List<BundleEntry> { new BundleEntry("optional-assets/level1.bin") });

        var singleFileArtifact = new ManifestArtifact.FileArtifact(
            CompressionKind.None,
            new FilePayload.Single($"{packageId}/artifacts/files/{HashA}/content", 14, HashA));

        var parts = new List<FilePart>
        {
            new FilePart(0, $"{packageId}/artifacts/files/{HashC}/part-00000", 12, HashD),
            new FilePart(1, $"{packageId}/artifacts/files/{HashC}/part-00001", 8, HashE),
        };
        var partsFileArtifact = new ManifestArtifact.FileArtifact(CompressionKind.Zstd, new FilePayload.Parts(20, HashC, parts));

        // Declared in the same content-addressed sort order the manifest must already be in
        // ("bundles/..." < "files/..." ordinally); ManifestValidator rejects an out-of-order array.
        var artifacts = new List<ManifestArtifact> { bundleArtifact, singleFileArtifact, partsFileArtifact };

        var files = new List<ManifestFileEntry>
        {
            new ManifestFileEntry("core/config.json", "core", 14, HashA, new FileSource.FileReference(HashA)),
            new ManifestFileEntry("optional-assets/big.bin", "optional-assets", 28, HashB, new FileSource.FileReference(HashC)),
            new ManifestFileEntry(
                "optional-assets/level1.bin",
                "optional-assets",
                14,
                HashF,
                new FileSource.BundleEntryReference(HashG, "optional-assets/level1.bin")),
        };

        var groups = new List<ManifestGroupEntry>
        {
            new ManifestGroupEntry("core", true),
            new ManifestGroupEntry("optional-assets", false),
        };

        return new ReleaseManifest(1, packageId, dataVersion, 0, groups, artifacts, files);
    }
}
