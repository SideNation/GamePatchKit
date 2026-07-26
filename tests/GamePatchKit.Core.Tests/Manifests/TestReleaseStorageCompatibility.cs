using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.Manifests;

public class TestReleaseStorageCompatibility
{
    private static readonly string _payloadHash = SampleManifests.Hash('a');
    private static readonly string _bundleHash = SampleManifests.Hash('b');

    [Fact]
    public void AcceptsReleasesThatShareStoredObjectsUnchanged()
    {
        ReleaseManifest release = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('d')) });

        Assert.True(ReleaseStorageCompatibility.Validate(release, release).IsValid);
    }

    [Fact]
    public void AcceptsReleasesWithNoStoredObjectInCommon()
    {
        ReleaseManifest single = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_payloadHash, 30) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, _payloadHash) });

        ReleaseManifest bundled = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.BundleArtifact("core", _bundleHash, 64, new[] { "data/big.bin" }) },
            new List<ManifestFileEntry> { SampleManifests.FileFromBundle("data/big.bin", "core", 30, _payloadHash, _bundleHash) });

        Assert.True(ReleaseStorageCompatibility.Validate(single, bundled).IsValid);
    }

    [Fact]
    public void RejectsAResplitPayloadBecauseItsPartsClaimPathsThatAlreadyHoldOtherBytes()
    {
        // Both releases hash to the same 30-byte payload, so both call the artifact _payloadHash and both write
        // part-00000 and part-00001 under it - but 20/10 and 16/14 put different bytes in those two files.
        // Publishing the second would have to overwrite bytes the first still needs.
        ReleaseManifest twentyTen = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('d')) });
        ReleaseManifest sixteenFourteen = PartitionedRelease(new[] { (16L, SampleManifests.Hash('e')), (14L, SampleManifests.Hash('f')) });

        ValidationResult result = ReleaseStorageCompatibility.Validate(twentyTen, sixteenFourteen);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count(error => error.Code == ManifestErrorCodes.StoragePathConflict));
    }

    [Fact]
    public void RejectsACandidateThatCollidesWithARetainedReleaseTheLatestOneDoesNotReference()
    {
        // Three releases of one package. The middle one stores payload _payloadHash as a single object, so it
        // occupies none of the part paths - which is exactly how a pairwise check gets bypassed.
        ReleaseManifest first = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('d')) });
        ReleaseManifest middle = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.SingleArtifact(_payloadHash, 30) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, _payloadHash) });
        ReleaseManifest latest = PartitionedRelease(new[] { (16L, SampleManifests.Hash('e')), (14L, SampleManifests.Hash('f')) });

        // Comparing only against its predecessor, the candidate looks publishable.
        Assert.True(ReleaseStorageCompatibility.Validate(middle, latest).IsValid);

        // Against everything the package still stores, it is overwriting the first release's parts.
        IEnumerable<ArtifactPayloadObject> retained = first.EnumeratePayloadObjects().Concat(middle.EnumeratePayloadObjects());
        ValidationResult result = ReleaseStorageCompatibility.Validate(retained, latest);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count(error => error.Code == ManifestErrorCodes.StoragePathConflict));
    }

    [Fact]
    public void ReportsRetainedStorageThatAlreadyDisagreesWithItself()
    {
        ReleaseManifest twentyTen = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('d')) });
        ReleaseManifest sixteenFourteen = PartitionedRelease(new[] { (16L, SampleManifests.Hash('e')), (14L, SampleManifests.Hash('f')) });

        // A history that should never have been published. The candidate matching one of the two entries must
        // not make the contradiction disappear.
        IEnumerable<ArtifactPayloadObject> brokenHistory = twentyTen.EnumeratePayloadObjects().Concat(sixteenFourteen.EnumeratePayloadObjects());
        ValidationResult result = ReleaseStorageCompatibility.Validate(brokenHistory, sixteenFourteen);

        Assert.False(result.IsValid);
        Assert.All(result.Errors, error => Assert.Equal(ManifestErrorCodes.StoragePathConflict, error.Code));
    }

    [Fact]
    public void ReportsOnlyTheConflictingObjectWhenAPartitionGrowsInPlace()
    {
        // part-00000 keeps its bytes and part-00001 is new, so only the tail moved.
        ReleaseManifest twoParts = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('d')) });
        ReleaseManifest resplitTail = PartitionedRelease(new[] { (20L, SampleManifests.Hash('c')), (10L, SampleManifests.Hash('e')) });

        ValidationResult result = ReleaseStorageCompatibility.Validate(twoParts, resplitTail);

        GamePatchKitError error = Assert.Single(result.Errors);
        Assert.Equal(ManifestErrorCodes.StoragePathConflict, error.Code);
        Assert.Contains("part-00001", error.Message);
    }

    private static ReleaseManifest PartitionedRelease(IReadOnlyList<(long Size, string PartHash)> parts)
    {
        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.PartsArtifact(_payloadHash, parts) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, _payloadHash) });
    }

    private static List<ManifestGroupEntry> Groups()
    {
        return new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) };
    }
}
