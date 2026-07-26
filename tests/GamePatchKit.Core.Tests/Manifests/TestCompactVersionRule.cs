using System;
using System.Collections.Generic;
using GamePatchKit.Core;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.Manifests;

public class TestCompactVersionRule
{
    private static readonly string _hashA = SampleManifests.Hash('a');
    private static readonly string _hashB = SampleManifests.Hash('b');
    private static readonly string _bundleHash = SampleManifests.Hash('c');
    private static readonly string _rebuiltBundleHash = SampleManifests.Hash('d');

    // These cases have no release in storage other than the source, which Resolve always includes itself.
    private static readonly ArtifactPayloadObject[] _noOtherRetainedObjects = Array.Empty<ArtifactPayloadObject>();

    [Fact]
    public void FirstPackageStartsAtZero()
    {
        Assert.Equal(0, CompactVersionRule.Initial);
        Assert.Equal(0, ReleaseIdentity.Finalize(BundledRelease(_bundleHash), CompactVersionRule.Initial).CompactVersion);
    }

    [Fact]
    public void IncrementalPackageInheritsCompactVersionWhileDataVersionMoves()
    {
        FinalizedManifest previous = ReleaseIdentity.Finalize(BundledRelease(_bundleHash), 4);

        // One file's content changed; an incremental package reuses the source's physical generation.
        ReleaseManifest next = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.BundleArtifact("maps", _bundleHash, 512, new[] { "maps/level1.bin" }),
                SampleManifests.SingleArtifact(_hashB, 10),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromBundle("maps/level1.bin", "maps", 14, SampleManifests.Hash('e'), _bundleHash),
                SampleManifests.FileFromArtifact("data/a.bin", "core", 10, _hashB),
            });

        FinalizedManifest result = ReleaseIdentity.Finalize(next, previous.CompactVersion);

        Assert.Equal(previous.CompactVersion, result.CompactVersion);
        Assert.NotEqual(previous.DataVersion, result.DataVersion);
        Assert.NotEqual(previous.ManifestHash, result.ManifestHash);
    }

    [Fact]
    public void IdenticalPhysicalLayoutIsANoOpThatReusesAllThreeValues()
    {
        ReleaseManifest source = ReleaseIdentity.Finalize(BundledRelease(_bundleHash), 2).Manifest;

        // A compact run that ends up rebuilding exactly the same bundle: rebuilt from scratch, byte-identical.
        CompactDecision decision = CompactVersionRule.Resolve(source, BundledRelease(_bundleHash), _noOtherRetainedObjects);

        Assert.False(decision.Changed);
        Assert.Equal(source.DataVersion, decision.Result.DataVersion);
        Assert.Equal(source.CompactVersion, decision.Result.CompactVersion);
        Assert.Equal(ReleaseIdentity.ComputeManifestHash(ReleaseIdentity.ComputeCanonicalBytes(source)), decision.Result.ManifestHash);
        Assert.Equal(ReleaseIdentity.ComputeCanonicalBytes(source), decision.Result.GetCanonicalBytes());
    }

    [Fact]
    public void ChangedPhysicalLayoutAdvancesCompactVersionAndManifestHashOnly()
    {
        ReleaseManifest source = ReleaseIdentity.Finalize(BundledRelease(_bundleHash), 2).Manifest;

        // Same files, repacked into a differently-hashed bundle.
        CompactDecision decision = CompactVersionRule.Resolve(source, BundledRelease(_rebuiltBundleHash), _noOtherRetainedObjects);

        Assert.True(decision.Changed);
        Assert.Equal(source.DataVersion, decision.Result.DataVersion);
        Assert.Equal(3, decision.Result.CompactVersion);
        Assert.NotEqual(ReleaseIdentity.ComputeManifestHash(ReleaseIdentity.ComputeCanonicalBytes(source)), decision.Result.ManifestHash);
    }

    [Fact]
    public void RejectsCandidateWhoseLogicalStateChanged()
    {
        ReleaseManifest source = ReleaseIdentity.Finalize(BundledRelease(_bundleHash), 2).Manifest;

        ReleaseManifest differentData = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.BundleArtifact("maps", _bundleHash, 512, new[] { "maps/level1.bin" }),
                SampleManifests.SingleArtifact(_hashA, 10),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromBundle("maps/level1.bin", "maps", 14, SampleManifests.Hash('e'), _bundleHash),

                // A file the source release does not have.
                SampleManifests.FileFromArtifact("data/added.bin", "core", 10, _hashA),
            });

        Assert.Throws<ArgumentException>(() => CompactVersionRule.Resolve(source, differentData, _noOtherRetainedObjects));
    }

    [Fact]
    public void RejectsCandidateThatWouldOverwriteTheSourcesStoredBytes()
    {
        // Same payload and same artifactHash, re-split: the candidate's parts land on the source's part paths
        // with different bytes, and a compact publishes alongside the source rather than replacing it.
        ReleaseManifest source = ReleaseIdentity.Finalize(PartitionedRelease((20L, SampleManifests.Hash('1')), (10L, SampleManifests.Hash('2'))), 2).Manifest;
        ReleaseManifest resplit = PartitionedRelease((16L, SampleManifests.Hash('3')), (14L, SampleManifests.Hash('4')));

        Assert.Throws<ArgumentException>(() => CompactVersionRule.Resolve(source, resplit, _noOtherRetainedObjects));
    }

    [Fact]
    public void RejectsCandidateThatCollidesWithARetainedReleaseTheSourceDoesNotReference()
    {
        // The source stores the payload as one object, so it holds none of the part paths - checking the
        // candidate against the source alone says nothing about the older release that does.
        ReleaseManifest olderRelease = PartitionedRelease((20L, SampleManifests.Hash('1')), (10L, SampleManifests.Hash('2')));
        ReleaseManifest source = ReleaseIdentity.Finalize(SingleObjectRelease(), 2).Manifest;
        ReleaseManifest resplit = PartitionedRelease((16L, SampleManifests.Hash('3')), (14L, SampleManifests.Hash('4')));

        // Told the source is all that is stored, the compact is accepted.
        Assert.True(CompactVersionRule.Resolve(source, resplit, _noOtherRetainedObjects).Changed);

        // Told what is actually still there, it is overwriting bytes that release needs for rollback.
        Assert.Throws<ArgumentException>(() => CompactVersionRule.Resolve(source, resplit, olderRelease.EnumeratePayloadObjects()));
    }

    private static ReleaseManifest SingleObjectRelease()
    {
        string payloadHash = SampleManifests.Hash('9');

        return SampleManifests.Manifest(
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(payloadHash, 30) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, payloadHash) });
    }

    private static ReleaseManifest PartitionedRelease(params (long Size, string PartHash)[] parts)
    {
        string payloadHash = SampleManifests.Hash('9');

        return SampleManifests.Manifest(
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact> { SampleManifests.PartsArtifact(payloadHash, parts) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, payloadHash) });
    }

    private static ReleaseManifest BundledRelease(string bundleHash)
    {
        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.BundleArtifact("maps", bundleHash, 512, new[] { "maps/level1.bin" }),
                SampleManifests.SingleArtifact(_hashA, 10),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromBundle("maps/level1.bin", "maps", 14, SampleManifests.Hash('e'), bundleHash),
                SampleManifests.FileFromArtifact("data/a.bin", "core", 10, _hashA),
            });
    }

    private static List<ManifestGroupEntry> Groups()
    {
        return new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true), new ManifestGroupEntry("maps", false) };
    }
}
