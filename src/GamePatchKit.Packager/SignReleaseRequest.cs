namespace GamePatchKit.Packager;

public sealed class SignReleaseRequest
{
    public string OutputRoot { get; }

    public string PackageId { get; }

    public string ManifestHash { get; }

    public IManifestSigner Signer { get; }

    // Produce and compare the signature but publish nothing. An existing manifest.sig is still read and
    // compared, so a dry run reports the same reuse or conflict a real run would.
    public bool DryRun { get; }

    public SignReleaseRequest(
        string outputRoot,
        string packageId,
        string manifestHash,
        IManifestSigner signer,
        bool dryRun = false)
    {
        OutputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? throw new ArgumentException("Output root must not be empty.", nameof(outputRoot))
            : outputRoot;
        PackageId = string.IsNullOrWhiteSpace(packageId)
            ? throw new ArgumentException("Package id must not be empty.", nameof(packageId))
            : packageId;
        ManifestHash = string.IsNullOrWhiteSpace(manifestHash)
            ? throw new ArgumentException("Manifest hash must not be empty.", nameof(manifestHash))
            : manifestHash;
        Signer = signer ?? throw new ArgumentNullException(nameof(signer));
        DryRun = dryRun;
    }
}
