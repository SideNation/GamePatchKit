using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core;
using GamePatchKit.Core.Diff;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.Diff;

public class TestReleaseDiff
{
    private const string Absent = "-";

    private static readonly string _hashA = SampleManifests.Hash('a');
    private static readonly string _hashB = SampleManifests.Hash('b');

    // One file at a fixed path, varied across the two releases. "-" means the file is not in that release.
    public static IEnumerable<object[]> SingleFileCases()
    {
        yield return new object[] { Absent, Absent, "core", "a", FileChangeKind.Added };
        yield return new object[] { "core", "a", Absent, Absent, FileChangeKind.Removed };
        yield return new object[] { "core", "a", "core", "b", FileChangeKind.ContentChanged };
        yield return new object[] { "core", "a", "extra", "a", FileChangeKind.GroupMoved };

        // Content wins when the file both changed and moved: it has to be re-materialized regardless.
        yield return new object[] { "core", "a", "extra", "b", FileChangeKind.ContentChanged };
    }

    [Theory]
    [MemberData(nameof(SingleFileCases))]
    public void ClassifiesFileChanges(string sourceGroup, string sourceHash, string targetGroup, string targetHash, FileChangeKind expected)
    {
        ReleaseManifest source = ReleaseWith(sourceGroup, sourceHash);
        ReleaseManifest target = ReleaseWith(targetGroup, targetHash);

        ReleaseDiff diff = ReleaseDiff.Compute(source, target);

        FileChange change = Assert.Single(diff.FileChanges);
        Assert.Equal(expected, change.Kind);
        Assert.Equal("data/file.bin", change.Path);
        Assert.Equal(sourceGroup == Absent ? null : sourceGroup, change.Source?.Group);
        Assert.Equal(targetGroup == Absent ? null : targetGroup, change.Target?.Group);
    }

    [Fact]
    public void FullArtifactReuseAfterAConfigurationChangeHasNoDifferenceAtAll()
    {
        // A release re-packaged after the configuration switched to zstd, where every file matched an existing
        // artifact: a reused artifact keeps its own payload and compression metadata, so nothing in the
        // manifest moved and neither identity value does either.
        ReleaseManifest before = TwoFileRelease();
        ReleaseManifest after = TwoFileRelease();

        ReleaseDiff diff = ReleaseDiff.Compute(before, after);

        Assert.Empty(diff.FileChanges);
        Assert.Empty(diff.ArtifactChanges);
        Assert.Equal(ReleaseIdentity.ComputeDataVersion(before), ReleaseIdentity.ComputeDataVersion(after));
        Assert.Equal(
            ReleaseIdentity.ComputeManifestHash(ReleaseIdentity.ComputeCanonicalBytes(before)),
            ReleaseIdentity.ComputeManifestHash(ReleaseIdentity.ComputeCanonicalBytes(after)));
    }

    [Fact]
    public void SameDataStoredDifferentlyIsAPhysicalChangeOnly()
    {
        ReleaseManifest uncompressed = TwoFileRelease();

        // Identical files[] entries, stored as zstd payloads under different artifact hashes.
        ReleaseManifest compressed = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.SingleArtifact(SampleManifests.Hash('c'), 4, CompressionKind.Zstd),
                SampleManifests.SingleArtifact(SampleManifests.Hash('d'), 9, CompressionKind.Zstd),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromArtifact("data/one.bin", "core", 10, _hashA, SampleManifests.Hash('c')),
                SampleManifests.FileFromArtifact("data/two.bin", "extra", 20, _hashB, SampleManifests.Hash('d')),
            });

        ReleaseDiff diff = ReleaseDiff.Compute(uncompressed, compressed);

        Assert.Empty(diff.FileChanges);
        Assert.Equal(2, diff.ArtifactChanges.Count(change => change.Kind == ArtifactChangeKind.Added));
        Assert.Equal(2, diff.ArtifactChanges.Count(change => change.Kind == ArtifactChangeKind.Removed));
    }

    [Fact]
    public void ReportsEveryPartOfAMultipartArtifactSeparately()
    {
        ReleaseManifest source = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashA, 30) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/one.bin", "core", 30, _hashA) });

        // Same payload and same artifactHash, re-split into two parts under a smaller maxArtifactBytes: the
        // artifact's identity did not move but the objects it is stored as did.
        ReleaseManifest target = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.PartsArtifact(_hashA, new[] { (20L, SampleManifests.Hash('d')), (10L, SampleManifests.Hash('e')) }),
            },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/one.bin", "core", 30, _hashA) });

        ReleaseDiff diff = ReleaseDiff.Compute(source, target);

        Assert.Empty(diff.FileChanges);
        Assert.Equal(2, diff.ArtifactChanges.Count(change => change.Kind == ArtifactChangeKind.Added));
        Assert.Single(diff.ArtifactChanges, change => change.Kind == ArtifactChangeKind.Removed);
    }

    [Fact]
    public void RefusesToDiffReleasesThatWouldOverwriteEachOthersStoredBytes()
    {
        // The same 30-byte payload under the same artifactHash, split 20/10 and then 16/14: the part count and
        // both part paths are identical, and only the stored bytes differ. These two releases cannot both be
        // published, so there is no diff between them to report.
        ReleaseManifest source = PartitionedRelease(new[] { (20L, SampleManifests.Hash('d')), (10L, SampleManifests.Hash('e')) });
        ReleaseManifest target = PartitionedRelease(new[] { (16L, SampleManifests.Hash('1')), (14L, SampleManifests.Hash('2')) });

        Assert.Throws<ArgumentException>(() => ReleaseDiff.Compute(source, target));
    }

    [Fact]
    public void DiffsReleasesThatShareAnUnchangedStoredObject()
    {
        // The shared part must not be mistaken for a conflict: same path, same bytes.
        ReleaseManifest source = PartitionedRelease(new[] { (20L, SampleManifests.Hash('d')), (10L, SampleManifests.Hash('e')) });
        ReleaseManifest target = PartitionedRelease(new[] { (20L, SampleManifests.Hash('d')), (10L, SampleManifests.Hash('e')) });

        ReleaseDiff diff = ReleaseDiff.Compute(source, target);

        Assert.Empty(diff.FileChanges);
        Assert.Empty(diff.ArtifactChanges);
    }

    [Fact]
    public void OrdersChangesByPath()
    {
        ReleaseManifest source = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashA, 10) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/zeta.bin", "core", 10, _hashA) });

        ReleaseManifest target = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashB, 20) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/alpha.bin", "core", 20, _hashB) });

        ReleaseDiff diff = ReleaseDiff.Compute(source, target);

        Assert.Equal(new[] { "data/alpha.bin", "data/zeta.bin" }, diff.FileChanges.Select(change => change.Path));
        Assert.Equal(new[] { FileChangeKind.Added, FileChangeKind.Removed }, diff.FileChanges.Select(change => change.Kind));
    }

    [Fact]
    public void RefusesToDiffDifferentPackages()
    {
        ReleaseManifest other = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashA, 10, packageId: "other-package") },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/one.bin", "core", 10, _hashA) },
            packageId: "other-package");

        Assert.Throws<ArgumentException>(() => ReleaseDiff.Compute(TwoFileRelease(), other));
    }

    private static ReleaseManifest ReleaseWith(string group, string hashDigit)
    {
        if (group == Absent)
        {
            // A release cannot declare an unreferenced artifact, so an empty files[] means an empty artifacts[].
            return SampleManifests.Manifest(Groups(), new List<ManifestArtifact>(), new List<ManifestFileEntry>());
        }

        string hash = SampleManifests.Hash(hashDigit[0]);

        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(hash, 10) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/file.bin", group, 10, hash) });
    }

    private static ReleaseManifest PartitionedRelease(IReadOnlyList<(long Size, string PartHash)> parts)
    {
        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.PartsArtifact(_hashA, parts) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, _hashA) });
    }

    private static ReleaseManifest TwoFileRelease()
    {
        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashA, 10), SampleManifests.SingleArtifact(_hashB, 20) },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromArtifact("data/one.bin", "core", 10, _hashA),
                SampleManifests.FileFromArtifact("data/two.bin", "extra", 20, _hashB),
            });
    }

    private static List<ManifestGroupEntry> Groups()
    {
        return new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true), new ManifestGroupEntry("extra", false) };
    }
}
