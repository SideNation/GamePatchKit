namespace GamePatchKit.Packager;

// The parts of the publish tree that are addressed by manifestHash rather than by payload digest. Artifact
// paths come from Core's ContentAddressedPath because a manifest records them; these do not appear in any
// manifest, so they live with the component that owns the local output tree.
//
// All returned paths are canonical: relative to the output root and '/'-separated, the same form manifests
// use, so they go through PackagePath.Resolve like any other package path.
public static class PackageLayout
{
    public const string ManifestFileName = "manifest.json";

    public const string CompressedManifestFileName = "manifest.json.zst";

    public const string SignatureFileName = "manifest.sig";

    // Matches the Runtime's own manifest limit so both sides refuse the same documents. The PRD's reference
    // fixture is 10,000 files and 1 GiB of source, whose canonical manifest is a few MiB.
    public const long MaximumManifestBytes = 64L * 1024 * 1024;

    // A canonical manifest.sig is a fixed shape - schemaVersion 1, algorithm "Ed25519", an 8+64 character
    // keyId and an 86 character signature - and comes to exactly 225 bytes today. The limit is deliberately
    // looser than that: it exists to stop an unbounded read, and pinning it to the exact figure would turn a
    // future schemaVersion into a confusing "too large" instead of a plain "not canonical".
    public const long MaximumSignatureBytes = 4L * 1024;

    public static string ManifestDirectory(string packageId, string manifestHash)
    {
        if (string.IsNullOrEmpty(packageId))
        {
            throw new ArgumentException("Package id must not be empty.", nameof(packageId));
        }

        if (string.IsNullOrEmpty(manifestHash))
        {
            throw new ArgumentException("Manifest hash must not be empty.", nameof(manifestHash));
        }

        return $"{packageId}/manifests/{manifestHash}";
    }

    public static string ManifestPath(string packageId, string manifestHash)
    {
        return $"{ManifestDirectory(packageId, manifestHash)}/{ManifestFileName}";
    }

    public static string SignaturePath(string packageId, string manifestHash)
    {
        return $"{ManifestDirectory(packageId, manifestHash)}/{SignatureFileName}";
    }
}
