using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core;
using GamePatchKit.Core.Downloads;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Tests.Downloads;

public class TestDownloadPlanner
{
    private static readonly string _coreHash = SampleManifests.Hash('a');
    private static readonly string _bundleHash = SampleManifests.Hash('b');
    private static readonly string _mapOneHash = SampleManifests.Hash('c');
    private static readonly string _mapTwoHash = SampleManifests.Hash('d');
    private static readonly string _partsHash = SampleManifests.Hash('e');
    private static readonly string _firstPartHash = SampleManifests.Hash('f');
    private static readonly string _secondPartHash = SampleManifests.Hash('0');
    private static readonly string _resplitFirstPartHash = SampleManifests.Hash('1');
    private static readonly string _resplitSecondPartHash = SampleManifests.Hash('2');

    private static readonly CachedArtifactObject[] _noCache = Array.Empty<CachedArtifactObject>();
    private static readonly LocalFileState[] _noFiles = Array.Empty<LocalFileState>();

    [Fact]
    public void ListsRequiredGroupsInManifestOrder()
    {
        Assert.Equal(new[] { "core" }, DownloadPlanner.RequiredGroupNames(Release()));
    }

    [Fact]
    public void PlansOnlyTheSelectedGroups()
    {
        DownloadPlan requiredOnly = DownloadPlanner.Plan(Release(), DownloadPlanner.RequiredGroupNames(Release()), _noFiles, _noCache);

        Assert.Equal(new[] { "data/core.bin" }, requiredOnly.MissingFiles.Select(file => file.Path));
        Assert.Equal(0, requiredOnly.BundleCount);
        Assert.DoesNotContain(requiredOnly.Artifacts, planned => planned.Artifact is ManifestArtifact.BundleArtifact);

        // A later optional-group request plans that group alone and does not drag the required group back in.
        DownloadPlan optionalOnly = DownloadPlanner.Plan(Release(), new[] { "maps" }, Installed(("data/core.bin", _coreHash)), _noCache);

        Assert.Equal(new[] { "maps/one.bin", "maps/two.bin" }, optionalOnly.MissingFiles.Select(file => file.Path));
        Assert.DoesNotContain(optionalOnly.Artifacts, planned => planned.Artifact is ManifestArtifact.FileArtifact);
    }

    [Fact]
    public void SkipsFilesAlreadyInstalledWithTheSameHash()
    {
        DownloadPlan plan = DownloadPlanner.Plan(Release(), new[] { "core" }, Installed(("data/core.bin", _coreHash)), _noCache);

        Assert.Empty(plan.MissingFiles);
        Assert.Empty(plan.Artifacts);
        Assert.Equal(0, plan.EstimatedDownloadBytes);
        Assert.Equal(0, plan.EstimatedTemporaryBytes);
    }

    [Fact]
    public void SkipsFilesWhoseArtifactMovedButWhoseBytesDidNot()
    {
        // A compacted release: the same file, same path and same fileHash, now reached through a bundle
        // instead of its own file artifact. Nothing to download.
        ReleaseManifest compacted = SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact> { SampleManifests.BundleArtifact("core", _bundleHash, 64, new[] { "data/core.bin" }) },
            new List<ManifestFileEntry> { SampleManifests.FileFromBundle("data/core.bin", "core", 10, _coreHash, _bundleHash) });

        DownloadPlan plan = DownloadPlanner.Plan(compacted, new[] { "core" }, Installed(("data/core.bin", _coreHash)), _noCache);

        Assert.Empty(plan.MissingFiles);
        Assert.Empty(plan.Artifacts);
    }

    [Fact]
    public void IncludesAWholeBundleOnceForAllOfItsMissingFiles()
    {
        DownloadPlan plan = DownloadPlanner.Plan(Release(), new[] { "maps" }, _noFiles, _noCache);

        PlannedArtifact bundle = Assert.Single(plan.Artifacts);
        ArtifactPayloadObject payload = Assert.Single(bundle.ObjectsToDownload);

        Assert.Equal(_bundleHash, payload.ObjectHash);
        Assert.Equal(1, plan.BundleCount);
        Assert.Equal(0, plan.FileObjectCount);
        Assert.False(bundle.RequiresCombinedHashVerification);

        // 512 bundle bytes downloaded, plus the two staged files (14 + 16) written before promotion.
        Assert.Equal(512, plan.EstimatedDownloadBytes);
        Assert.Equal(512 + 14 + 16, plan.EstimatedTemporaryBytes);
    }

    [Fact]
    public void DownloadsOnlyTheMissingPartsButStillVerifiesTheJoinedPayload()
    {
        ReleaseManifest release = PartitionedRelease();

        DownloadPlan plan = DownloadPlanner.Plan(release, new[] { "core" }, _noFiles, Cached((PartPath(0), _firstPartHash)));

        PlannedArtifact planned = Assert.Single(plan.Artifacts);
        ArtifactPayloadObject payload = Assert.Single(planned.ObjectsToDownload);

        Assert.Equal(_secondPartHash, payload.ObjectHash);
        Assert.True(planned.RequiresCombinedHashVerification);
        Assert.Equal(1, plan.FileObjectCount);
        Assert.Equal(10, plan.EstimatedDownloadBytes);
    }

    [Fact]
    public void RefetchesAPartWhosePathIsCachedWithTheBytesOfAnotherSplit()
    {
        // The same 30-byte payload, so the same artifactHash and the same two part paths - but split 16/14
        // instead of 20/10. The cache holds the old part-00000, whose bytes belong to the other split; reusing
        // it on the strength of its path alone would join to a payload that can never match artifactHash.
        DownloadPlan plan = DownloadPlanner.Plan(ResplitPartitionedRelease(), new[] { "core" }, _noFiles, Cached((PartPath(0), _firstPartHash)));

        PlannedArtifact planned = Assert.Single(plan.Artifacts);

        Assert.Equal(
            new[] { _resplitFirstPartHash, _resplitSecondPartHash },
            planned.ObjectsToDownload.Select(payload => payload.ObjectHash));
        Assert.Equal(30, plan.EstimatedDownloadBytes);
    }

    [Fact]
    public void KeepsAFullyCachedArtifactInThePlanForTheFileItStillHasToProduce()
    {
        DownloadPlan plan = DownloadPlanner.Plan(
            PartitionedRelease(),
            new[] { "core" },
            _noFiles,
            Cached((PartPath(0), _firstPartHash), (PartPath(1), _secondPartHash)));

        PlannedArtifact planned = Assert.Single(plan.Artifacts);

        Assert.Empty(planned.ObjectsToDownload);
        Assert.True(planned.RequiresCombinedHashVerification);
        Assert.Equal(new[] { "data/big.bin" }, plan.MissingFiles.Select(file => file.Path));
        Assert.Equal(0, plan.EstimatedDownloadBytes);

        // Nothing to fetch, but the joined file still needs staging space.
        Assert.Equal(30, plan.EstimatedTemporaryBytes);
    }

    [Fact]
    public void RejectsGroupsTheManifestDoesNotDeclare()
    {
        Assert.Throws<ArgumentException>(() => DownloadPlanner.Plan(Release(), new[] { "audio" }, _noFiles, _noCache));
    }

    [Fact]
    public void RejectsAManifestWhoseFileReferenceDoesNotResolve()
    {
        // Built directly rather than through SampleManifests: ManifestValidator would reject this, and the
        // point is that the planner refuses it instead of dereferencing a missing artifact.
        var dangling = new ReleaseManifest(
            1,
            SampleManifests.PackageId,
            SampleManifests.PlaceholderDataVersion,
            0,
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact>(),
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/core.bin", "core", 10, _coreHash) });

        Assert.Throws<ArgumentException>(() => DownloadPlanner.Plan(dangling, new[] { "core" }, _noFiles, _noCache));
    }

    private static ReleaseManifest Release()
    {
        return SampleManifests.Manifest(
            Groups(),
            new List<ManifestArtifact>
            {
                SampleManifests.SingleArtifact(_coreHash, 10),
                SampleManifests.BundleArtifact("maps", _bundleHash, 512, new[] { "maps/one.bin", "maps/two.bin" }),
            },
            new List<ManifestFileEntry>
            {
                SampleManifests.FileFromArtifact("data/core.bin", "core", 10, _coreHash),
                SampleManifests.FileFromBundle("maps/one.bin", "maps", 14, _mapOneHash, _bundleHash),
                SampleManifests.FileFromBundle("maps/two.bin", "maps", 16, _mapTwoHash, _bundleHash),
            });
    }

    private static ReleaseManifest PartitionedRelease()
    {
        return PartitionedRelease(new[] { (20L, _firstPartHash), (10L, _secondPartHash) });
    }

    private static ReleaseManifest ResplitPartitionedRelease()
    {
        return PartitionedRelease(new[] { (16L, _resplitFirstPartHash), (14L, _resplitSecondPartHash) });
    }

    private static ReleaseManifest PartitionedRelease(IReadOnlyList<(long Size, string PartHash)> parts)
    {
        return SampleManifests.Manifest(
            new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true) },
            new List<ManifestArtifact> { SampleManifests.PartsArtifact(_partsHash, parts) },
            new List<ManifestFileEntry> { SampleManifests.FileFromArtifact("data/big.bin", "core", 30, _partsHash) });
    }

    private static string PartPath(long index)
    {
        return ContentAddressedPath.FilePartPath(SampleManifests.PackageId, _partsHash, index);
    }

    private static CachedArtifactObject[] Cached(params (string Path, string ObjectHash)[] objects)
    {
        return objects.Select(cached => new CachedArtifactObject(cached.Path, cached.ObjectHash)).ToArray();
    }

    private static List<ManifestGroupEntry> Groups()
    {
        return new List<ManifestGroupEntry> { new ManifestGroupEntry("core", true), new ManifestGroupEntry("maps", false) };
    }

    private static LocalFileState[] Installed(params (string Path, string FileHash)[] files)
    {
        return files.Select(file => new LocalFileState(file.Path, file.FileHash)).ToArray();
    }
}
