namespace GamePatchKit.Packager;

public sealed class ReleaseVerifyRequest
{
    private readonly byte[] _manifestBytes;

    public string OutputRoot { get; }

    public string PackageId { get; }

    // The manifestHash the caller is asking about, not one recomputed from the bytes. Verification exists to
    // answer "are the bytes at this reference the ones I was told to expect", so a caller-supplied hash is the
    // whole point; recomputing it would make every self-consistent manifest verify.
    public string ManifestHash { get; }

    // Null means an unsigned verification: a present manifest.sig is still parsed and schema-checked, but no
    // cryptographic claim is made about it. See IManifestSignatureVerifier.
    public IManifestSignatureVerifier? SignatureVerifier { get; }

    // Refuse to report success unless the signature was cryptographically verified. Requires a
    // SignatureVerifier: without one the strongest available answer is "a well-formed signature document
    // exists", and a forged manifest.sig with any correctly shaped keyId and any 64 bytes satisfies that.
    // Treating presence as a signing requirement would make this a gate that lets forgeries through.
    public bool RequireSignature { get; }

    public ReleaseVerifyRequest(
        string outputRoot,
        string packageId,
        string manifestHash,
        byte[] manifestBytes,
        IManifestSignatureVerifier? signatureVerifier = null,
        bool requireSignature = false)
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
        _manifestBytes = manifestBytes == null
            ? throw new ArgumentNullException(nameof(manifestBytes))
            : (byte[])manifestBytes.Clone();
        if (requireSignature && signatureVerifier == null)
        {
            throw new ArgumentException(
                "Requiring a signature needs a signature verifier; without one, verification cannot tell a real signature from a forged one.",
                nameof(requireSignature));
        }

        SignatureVerifier = signatureVerifier;
        RequireSignature = requireSignature;
    }

    public byte[] GetManifestBytes()
    {
        return (byte[])_manifestBytes.Clone();
    }
}
