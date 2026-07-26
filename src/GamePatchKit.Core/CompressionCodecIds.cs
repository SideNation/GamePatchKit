namespace GamePatchKit.Core
{
    // Wire identifiers for ICompressionCodec implementations. This is the value a manifest records as
    // compression.codecId and the key a host uses to hand Runtime the matching codec, so it must stay
    // stable independently of which library implements it.
    public static class CompressionCodecIds
    {
        public const string Zstd = "zstd";
    }
}
