using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

public sealed class FilePackageResult
{
    public FinalizedManifest Release { get; }

    public PackageBuildReport Report { get; }

    public bool ReusedManifest { get; }

    internal FilePackageResult(FinalizedManifest release, PackageBuildReport report, bool reusedManifest)
    {
        Release = release ?? throw new ArgumentNullException(nameof(release));
        Report = report ?? throw new ArgumentNullException(nameof(report));
        ReusedManifest = reusedManifest;
    }
}
