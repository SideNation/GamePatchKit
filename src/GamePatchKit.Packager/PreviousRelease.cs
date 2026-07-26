namespace GamePatchKit.Packager;

public sealed class PreviousRelease
{
    private readonly byte[] _manifestBytes;

    public string ManifestHash { get; }

    public PreviousRelease(byte[] manifestBytes, string manifestHash)
    {
        _manifestBytes = manifestBytes == null
            ? throw new ArgumentNullException(nameof(manifestBytes))
            : (byte[])manifestBytes.Clone();
        ManifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
    }

    public byte[] GetManifestBytes()
    {
        return (byte[])_manifestBytes.Clone();
    }
}
