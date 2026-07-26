namespace GamePatchKit.Core
{
    // Shared by package configuration policy (Configuration namespace) and manifest artifact metadata
    // (Manifests namespace). Level and frame options are fixed internally by
    // GamePatchKit.Compression.NativeCompressions (see docs/plan/04-zstd-codec-adapter.md) and are not
    // user-configurable, so there is no extra payload to carry beyond this discriminator.
    public enum CompressionKind
    {
        None,
        Zstd,
    }
}
