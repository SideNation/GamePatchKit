using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

// The object paths a package still occupies because releases other than the compact source are being kept
// around - for rollback, or because clients are still on them.
//
// Built from those releases' manifests rather than from a directory listing: a listing cannot say which
// objects are still referenced, and a compact has to be checked against what is claimed, not against whatever
// happens to be lying in the tree. Each manifest is validated first, because an inventory read from a
// manifest that does not hold up is not evidence of anything.
public static class RetainedReleaseInventory
{
    public static IReadOnlyList<ArtifactPayloadObject> Collect(
        IEnumerable<PreviousRelease> retainedReleases,
        string packageId)
    {
        if (retainedReleases == null)
        {
            throw new ArgumentNullException(nameof(retainedReleases));
        }

        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package id must not be empty.", nameof(packageId));
        }

        var objects = new List<ArtifactPayloadObject>();

        foreach (PreviousRelease retained in retainedReleases)
        {
            ReleaseManifest manifest = ReleaseManifestReader.Read(
                retained.GetManifestBytes(),
                packageId,
                retained.ManifestHash);

            objects.AddRange(manifest.EnumeratePayloadObjects());
        }

        return objects;
    }
}
