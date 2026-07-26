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
        CompactDecision decision = CompactVersionRule.Resolve(source, BundledRelease(_bundleHash));

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
        CompactDecision decision = CompactVersionRule.Resolve(source, BundledRelease(_rebuiltBundleHash));

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

        Assert.Throws<ArgumentException>(() => CompactVersionRule.Resolve(source, differentData));
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
