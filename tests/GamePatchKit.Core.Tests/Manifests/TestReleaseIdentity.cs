using System.Collections.Generic;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.Manifests;

public class TestReleaseIdentity
{
    private static readonly string _hashA = SampleManifests.Hash('a');
    private static readonly string _hashB = SampleManifests.Hash('b');
    private static readonly string _hashC = SampleManifests.Hash('c');
    private static readonly string _hashD = SampleManifests.Hash('d');

    [Fact]
    public void ComputesWellFormedDataVersion()
    {
        string dataVersion = ReleaseIdentity.ComputeDataVersion(Baseline());

        Assert.True(DataVersionFormat.IsValid(dataVersion), dataVersion);
    }

    [Fact]
    public void ChangedFileContentChangesBothIdentityValues()
    {
        FinalizedManifest baseline = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        // "data/a.bin" now holds different bytes, so it points at a different artifact.
        ReleaseManifest changed = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashC, 10), SampleManifests.SingleArtifact(_hashB, 20) },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromArtifact("data/a.bin", "core", 10, _hashC),
                SampleManifests.FileFromArtifact("data/b.bin", "extra", 20, _hashB),
            });

        FinalizedManifest result = ReleaseIdentity.Finalize(changed, CompactVersionRule.Initial);

        Assert.NotEqual(baseline.DataVersion, result.DataVersion);
        Assert.NotEqual(baseline.ManifestHash, result.ManifestHash);
    }

    [Fact]
    public void ChangedGroupPolicyChangesBothIdentityValues()
    {
        FinalizedManifest baseline = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        // Same files and same artifacts; only what "extra" means to a consumer changed.
        ReleaseManifest changed = SampleManifests.Manifest(
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true), new ManifestGroupEntry("extra", true) },
            Artifacts(),
            Files());

        FinalizedManifest result = ReleaseIdentity.Finalize(changed, CompactVersionRule.Initial);

        Assert.NotEqual(baseline.DataVersion, result.DataVersion);
        Assert.NotEqual(baseline.ManifestHash, result.ManifestHash);
    }

    [Fact]
    public void SameDataUnderDifferentCompressionKeepsDataVersion()
    {
        FinalizedManifest baseline = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        // Identical files (same paths, groups, sizes and fileHashes) stored as zstd payloads: different
        // artifact hashes, different artifact paths, same logical release.
        ReleaseManifest compressed = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.SingleArtifact(_hashC, 4, CompressionKind.Zstd),
                SampleManifests.SingleArtifact(_hashD, 7, CompressionKind.Zstd),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromArtifact("data/a.bin", "core", 10, _hashA, _hashC),
                SampleManifests.FileFromArtifact("data/b.bin", "extra", 20, _hashB, _hashD),
            });

        FinalizedManifest result = ReleaseIdentity.Finalize(compressed, CompactVersionRule.Initial);

        Assert.Equal(baseline.DataVersion, result.DataVersion);
        Assert.Equal(ReleaseIdentity.ComputeIdentityBytes(Baseline()), ReleaseIdentity.ComputeIdentityBytes(compressed));
        Assert.NotEqual(baseline.ManifestHash, result.ManifestHash);
    }

    [Fact]
    public void ChangedSchemaVersionKeepsDataVersionAndChangesManifestHash()
    {
        FinalizedManifest baseline = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        ReleaseManifest nextSchema = SampleManifests.Manifest(Groups(), Artifacts(), Files(), schemaVersion: 2);
        FinalizedManifest result = ReleaseIdentity.Finalize(nextSchema, CompactVersionRule.Initial);

        Assert.Equal(baseline.DataVersion, result.DataVersion);
        Assert.NotEqual(baseline.ManifestHash, result.ManifestHash);
    }

    [Fact]
    public void FinalizeIgnoresTheDraftsRecordedDataVersion()
    {
        ReleaseManifest draft = Baseline();
        ReleaseManifest misrecorded = new ReleaseManifest(
            draft.SchemaVersion,
            draft.PackageId,
            "v1-" + SampleManifests.Hash('f'),
            draft.CompactVersion,
            draft.Groups,
            draft.Artifacts,
            draft.Files);

        Assert.Equal(
            ReleaseIdentity.Finalize(draft, CompactVersionRule.Initial).DataVersion,
            ReleaseIdentity.Finalize(misrecorded, CompactVersionRule.Initial).DataVersion);
    }

    [Fact]
    public void FinalizeSettlesCompactVersionAndHashesItsOwnBytes()
    {
        FinalizedManifest finalized = ReleaseIdentity.Finalize(Baseline(), 3);

        Assert.Equal(3, finalized.CompactVersion);
        Assert.Equal(3, finalized.Manifest.CompactVersion);
        Assert.Equal(finalized.DataVersion, finalized.Manifest.DataVersion);
        Assert.Equal(CanonicalJsonWriter.Write(finalized.Manifest.ToJson()), finalized.GetCanonicalBytes());
        Assert.Equal(Sha256Hash.ComputeHex(finalized.GetCanonicalBytes()), finalized.ManifestHash);
    }

    [Fact]
    public void FinalizeRejectsNegativeCompactVersion()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => ReleaseIdentity.Finalize(Baseline(), -1));
    }

    [Fact]
    public void CanonicalBytesAreCopiedOnEveryRead()
    {
        FinalizedManifest finalized = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        byte[] first = finalized.GetCanonicalBytes();
        first[0] = (byte)'X';

        Assert.NotEqual(first, finalized.GetCanonicalBytes());
        Assert.Equal(Sha256Hash.ComputeHex(finalized.GetCanonicalBytes()), finalized.ManifestHash);
    }

    [Fact]
    public void FinalizedManifestDoesNotFollowLaterEditsToTheDraftsCollections()
    {
        var groups = new List<ManifestGroupEntry>(Groups());
        var artifacts = new List<ManifestArtifact>(Artifacts());
        var files = new List<ManifestFileEntry>(Files());
        var draft = new ReleaseManifest(1, SampleManifests.PackageId, SampleManifests.PlaceholderDataVersion, 0, groups, artifacts, files);

        FinalizedManifest finalized = ReleaseIdentity.Finalize(draft, CompactVersionRule.Initial);

        groups.Clear();
        artifacts.Clear();
        files.Clear();

        // The published bytes and the signed hash describe the release as it was finalized, so the model has
        // to keep describing that same release.
        Assert.Equal(2, finalized.Manifest.Groups.Count);
        Assert.Equal(2, finalized.Manifest.Artifacts.Count);
        Assert.Equal(2, finalized.Manifest.Files.Count);
        Assert.Equal(CanonicalJsonWriter.Write(finalized.Manifest.ToJson()), finalized.GetCanonicalBytes());
        Assert.Equal(Sha256Hash.ComputeHex(finalized.GetCanonicalBytes()), finalized.ManifestHash);
        Assert.Equal(ReleaseIdentity.ComputeDataVersion(finalized.Manifest), finalized.DataVersion);
    }

    [Fact]
    public void VerifiesManifestBytesAgainstExpectedHash()
    {
        FinalizedManifest finalized = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        Assert.True(ReleaseIdentity.VerifyManifestHash(finalized.GetCanonicalBytes(), finalized.ManifestHash).IsValid);
    }

    [Fact]
    public void RejectsManifestBytesThatHashToSomethingElse()
    {
        FinalizedManifest finalized = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);
        byte[] tampered = finalized.GetCanonicalBytes();
        tampered[tampered.Length - 2] = (byte)' ';

        ValidationResult result = ReleaseIdentity.VerifyManifestHash(tampered, finalized.ManifestHash);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ManifestErrorCodes.ManifestHashMismatch);
    }

    [Fact]
    public void RejectsMalformedExpectedManifestHash()
    {
        FinalizedManifest finalized = ReleaseIdentity.Finalize(Baseline(), CompactVersionRule.Initial);

        ValidationResult result = ReleaseIdentity.VerifyManifestHash(finalized.GetCanonicalBytes(), finalized.ManifestHash.ToUpperInvariant());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ManifestErrorCodes.InvalidManifestHashFormat);
    }

    private static ReleaseManifest Baseline()
    {
        return SampleManifests.Manifest(Groups(), Artifacts(), Files());
    }

    private static List<ManifestGroupEntry> Groups()
    {
        return new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true), new ManifestGroupEntry("extra", false) };
    }

    private static List<ManifestArtifact> Artifacts()
    {
        return new List<ManifestArtifact> { SampleManifests.SingleArtifact(_hashA, 10), SampleManifests.SingleArtifact(_hashB, 20) };
    }

    private static List<ManifestFileEntry> Files()
    {
        return new List<ManifestFileEntry>
        {
            SampleManifests.FileFromArtifact("data/a.bin", "core", 10, _hashA),
            SampleManifests.FileFromArtifact("data/b.bin", "extra", 20, _hashB),
        };
    }
}
