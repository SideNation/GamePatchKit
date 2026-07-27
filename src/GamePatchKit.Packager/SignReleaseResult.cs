using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager;

public sealed class SignReleaseResult
{
    private readonly byte[] _canonicalBytes;

    public ManifestSignature Signature { get; }

    public string ManifestHash { get; }

    // False when manifest.sig was already there with exactly these bytes. Re-signing the same manifest with
    // the same key is a verified reuse, not a rewrite - the path is immutable.
    public bool Created { get; }

    public string KeyId => Signature.KeyId;

    internal SignReleaseResult(ManifestSignature signature, byte[] canonicalBytes, string manifestHash, bool created)
    {
        Signature = signature;
        _canonicalBytes = canonicalBytes;
        ManifestHash = manifestHash;
        Created = created;
    }

    public byte[] GetCanonicalBytes()
    {
        return (byte[])_canonicalBytes.Clone();
    }
}
