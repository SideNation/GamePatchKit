using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Packager;

// What a completed verification found. A report only exists when every check passed - a failed verification
// throws PackageException with the specific errors - so every field here describes a release that is known
// good to the depth SignatureState reports.
public sealed class ReleaseVerifyReport
{
    public ReleaseManifest Manifest { get; }

    public string PackageId => Manifest.PackageId;

    public string DataVersion => Manifest.DataVersion;

    public long CompactVersion => Manifest.CompactVersion;

    public string ManifestHash { get; }

    public ReleaseTotals Totals { get; }

    public SignatureState SignatureState { get; }

    // The signing key's fingerprint when a manifest.sig was found. Never the key itself.
    public string? KeyId { get; }

    internal ReleaseVerifyReport(
        ReleaseManifest manifest,
        string manifestHash,
        ReleaseTotals totals,
        SignatureState signatureState,
        string? keyId)
    {
        Manifest = manifest;
        ManifestHash = manifestHash;
        Totals = totals;
        SignatureState = signatureState;
        KeyId = keyId;
    }
}
